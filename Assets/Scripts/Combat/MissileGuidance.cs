using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Proportional Navigation (PN) missile guidance.
    ///
    /// Classic PN law: acceleration command = N * Vc * LOS_rate
    /// where:
    ///   N   = navigation constant (typically 3–5)
    ///   Vc  = closing velocity (relative speed toward target)
    ///   LOS_rate = rate of change of the line-of-sight angle
    ///
    /// The missile tracks the target with lead pursuit — it steers to where
    /// the target will be, not where it is now.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MissileGuidance : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Navigation")]
        [SerializeField] private float navConstant        = 4f;    // N in PN law
        [SerializeField] private float maxTurnRateDeg     = 35f;   // deg/s maneuver limit

        [Header("Propulsion")]
        [SerializeField] private float motorSpeed         = 550f;  // m/s cruise
        [SerializeField] private float burnTime           = 4.5f;  // seconds motor burns
        [SerializeField] private float armingDistance     = 80f;   // metres from launch

        [Header("Lifetime")]
        [SerializeField] private float maxFlightTime      = 12f;

        [Header("Damage")]
        [SerializeField] private float damage             = 300f;
        [SerializeField] private float blastRadius        = 18f;
        [SerializeField] private float proximityFuseRange = 8f;

        [Header("VFX")]
        [SerializeField] private GameObject explosionPrefab;
        [SerializeField] private ParticleSystem motorFlame;
        [SerializeField] private TrailRenderer  trail;

        // ─── Runtime state ────────────────────────────────────────────────────
        public bool     IsActive   { get; private set; }
        public bool     HasTarget  => _target != null;
        public Transform Target    => _target;

        // ─── Private ──────────────────────────────────────────────────────────
        private Rigidbody  _rb;
        private Transform  _target;
        private Vector3    _launchPos;
        private Vector3    _prevLOS;         // previous line-of-sight vector (PN)
        private float      _flightTimer;
        private bool       _armed;
        private bool       _motorBurning;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _rb             = GetComponent<Rigidbody>();
            _rb.useGravity  = false;
            _rb.isKinematic = false;
            _rb.linearDamping        = 0f;
            _rb.angularDamping = 0f;
        }

        private void FixedUpdate()
        {
            if (!IsActive) return;

            _flightTimer += Time.fixedDeltaTime;

            // Arm check
            if (!_armed)
            {
                float distFromLaunch = Vector3.Distance(transform.position, _launchPos);
                _armed = distFromLaunch >= armingDistance;
            }

            // Motor
            _motorBurning = _flightTimer <= burnTime;
            if (_motorBurning)
            {
                _rb.linearVelocity = transform.forward * motorSpeed;
                if (motorFlame != null && !motorFlame.isPlaying)
                    motorFlame.Play();
            }
            else
            {
                // Coast with a small drag
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity,
                    transform.forward * (_rb.linearVelocity.magnitude * 0.995f),
                    Time.fixedDeltaTime);

                if (motorFlame != null && motorFlame.isPlaying)
                    motorFlame.Stop();
            }

            // Guidance
            if (_target != null && _armed)
                ApplyProportionalNavigation();
            else
                FlyForward();

            // Proximity fuze
            if (_armed && _target != null)
            {
                float dist = Vector3.Distance(transform.position, _target.position);
                if (dist < proximityFuseRange)
                    Detonate();
            }

            // Flight time limit
            if (_flightTimer > maxFlightTime)
                Detonate();
        }

        private void OnCollisionEnter(Collision col)
        {
            if (!IsActive || !_armed) return;
            Detonate();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Arms and fires the missile at <paramref name="target"/>.</summary>
        public void Launch(Transform target, Vector3 inheritVelocity)
        {
            _target      = target;
            _launchPos   = transform.position;
            _flightTimer = 0f;
            _armed       = false;
            IsActive     = true;

            _rb.linearVelocity = transform.forward * motorSpeed + inheritVelocity;
            _prevLOS     = (_target != null)
                ? (_target.position - transform.position).normalized
                : transform.forward;

            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }

            gameObject.SetActive(true);
        }

        /// <summary>Updates the guidance target mid-flight (e.g. after flare deploy).</summary>
        public void UpdateTarget(Transform newTarget)
        {
            _target  = newTarget;
            if (_target != null)
                _prevLOS = (_target.position - transform.position).normalized;
        }

        // ─── Private guidance ────────────────────────────────────────────────
        private void ApplyProportionalNavigation()
        {
            Vector3 relPos = _target.position - transform.position;
            Vector3 relVel = _target.GetComponent<Rigidbody>() != null
                ? _target.GetComponent<Rigidbody>().linearVelocity - _rb.linearVelocity
                : -_rb.linearVelocity;

            // Current LOS unit vector
            float   dist        = relPos.magnitude;
            Vector3 los         = relPos / Mathf.Max(dist, 0.1f);

            // LOS rate (cross product gives angular rotation axis of LOS)
            Vector3 losRate     = Vector3.Cross(los, (_prevLOS - los) / Time.fixedDeltaTime);

            // Closing velocity (positive = approaching)
            float closingVel    = -Vector3.Dot(relVel, los);

            // PN acceleration command
            Vector3 aCmd        = navConstant * closingVel * losRate;

            // Clamp to max lateral G
            float maxAccel = maxTurnRateDeg * Mathf.Deg2Rad * motorSpeed;
            aCmd = Vector3.ClampMagnitude(aCmd, maxAccel);

            // Rotate missile toward acceleration command direction
            if (aCmd.sqrMagnitude > 0.001f)
            {
                Vector3 newForward = (_rb.linearVelocity + aCmd * Time.fixedDeltaTime).normalized;
                Quaternion targetRot = Quaternion.LookRotation(newForward, transform.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRot,
                    maxTurnRateDeg * Time.fixedDeltaTime
                );
            }

            // Keep velocity aligned with nose
            float spd = _rb.linearVelocity.magnitude;
            _rb.linearVelocity = transform.forward * spd;

            _prevLOS = los;
        }

        private void FlyForward()
        {
            // Maintain straight-line flight when unguided or not armed
            _rb.linearVelocity = transform.forward * Mathf.Max(_rb.linearVelocity.magnitude, motorSpeed * 0.5f);
        }

        private void Detonate()
        {
            IsActive = false;

            // Blast damage
            Collider[] hits = Physics.OverlapSphere(transform.position, blastRadius);
            foreach (var c in hits)
            {
                float dist    = Vector3.Distance(c.transform.position, transform.position);
                float falloff = 1f - Mathf.Clamp01(dist / blastRadius);
                var   dmg     = c.GetComponentInParent<DamageSystem>();
                if (dmg != null)
                    dmg.TakeDamage(damage * falloff);
            }

            if (explosionPrefab != null)
                Instantiate(explosionPrefab, transform.position, Quaternion.identity);

            if (trail != null)
                trail.emitting = false;

            gameObject.SetActive(false);
        }
    }
}
