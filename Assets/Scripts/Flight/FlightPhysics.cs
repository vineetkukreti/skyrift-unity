using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Arcade-realistic flight physics engine.
    /// Applies lift, drag, gravity, and control-surface torques to the aircraft
    /// Rigidbody.  Designed to feel like Ace Combat — physically grounded but
    /// never frustrating.
    ///
    /// Attach to the aircraft root that also has a Rigidbody component.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(AircraftStats))]
    public class FlightPhysics : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Lift / Drag")]
        [Tooltip("Multiplier applied on top of the stats SO lift coefficient.")]
        [SerializeField] private float liftMultiplier    = 1f;

        [Tooltip("AnimationCurve mapping normalised speed (0=stall, 1=max) to drag factor.")]
        [SerializeField] private AnimationCurve dragCurve = AnimationCurve.EaseInOut(0f, 2f, 1f, 0.4f);

        [Tooltip("AnimationCurve mapping normalised AoA (0=level, 1=90°) to lift factor.")]
        [SerializeField] private AnimationCurve liftAoACurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Stall")]
        [Tooltip("Speed below which the aircraft enters the stall state (m/s).")]
        [SerializeField] private float stallSpeedFactor  = 0.3f;   // fraction of MinSpeed
        [SerializeField] private float stallRecoveryTime = 2f;

        [Header("Airbrakes")]
        [SerializeField] private float airbrakeDragMultiplier = 4f;

        [Header("Ground Effect")]
        [SerializeField] private float groundEffectHeight    = 20f;
        [SerializeField] private float groundEffectLiftBonus = 0.25f;

        [Header("Control Response")]
        [SerializeField] private float controlResponseTime = 0.12f;   // seconds to reach target input

        // ─── Runtime state (readable by other systems) ────────────────────────
        public float  SpeedMS         { get; private set; }
        public float  SpeedKnots      => SpeedMS * 1.944f;
        public float  AltitudeMeters  => transform.position.y;
        public float  ThrottleInput   { get; set; }   // 0..1 set by FlightController
        public float  AfterburnerInput { get; set; }  // 0 or 1
        public float  AirbrakeInput   { get; set; }   // 0 or 1
        public bool   IsStalled       { get; private set; }
        public Vector3 LocalVelocity  { get; private set; }

        // Control surface target values written by FlightController
        public float  PitchInput  { get; set; }   // -1..1
        public float  RollInput   { get; set; }   // -1..1
        public float  YawInput    { get; set; }   // -1..1

        // ─── Private ──────────────────────────────────────────────────────────
        private Rigidbody     _rb;
        private AircraftStats _stats;
        private float         _stallTimer;
        private Vector3       _smoothedAngularTarget;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _rb    = GetComponent<Rigidbody>();
            _stats = GetComponent<AircraftStats>();

            _rb.mass                = _stats.Mass;
            _rb.linearDamping       = 0f;   // We handle drag ourselves
            _rb.angularDamping      = 2.5f;
            _rb.useGravity          = true;
            _rb.interpolation       = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void FixedUpdate()
        {
            SpeedMS      = _rb.linearVelocity.magnitude;
            LocalVelocity = transform.InverseTransformDirection(_rb.linearVelocity);

            UpdateStallState();
            ApplyThrust();
            ApplyLift();
            ApplyDrag();
            ApplyControlTorques();
            ClampMaxSpeed();
        }

        // ─── Physics sub-systems ─────────────────────────────────────────────

        private void ApplyThrust()
        {
            float baseThrust = _stats.EngineThrust * ThrottleInput;
            float abThrust   = _stats.AfterburnerThrust * AfterburnerInput;
            float totalThrust = (baseThrust + abThrust) * Time.fixedDeltaTime;

            _rb.AddForce(transform.forward * totalThrust, ForceMode.Force);
        }

        private void ApplyLift()
        {
            if (IsStalled) return;

            // Angle of attack: angle between forward and velocity direction
            float speed = Mathf.Max(SpeedMS, 0.1f);
            Vector3 velocityDir = _rb.linearVelocity / speed;
            float aoa = Vector3.Angle(transform.forward, velocityDir) / 90f;   // normalised 0–1

            float liftAoaFactor = liftAoACurve.Evaluate(Mathf.Clamp01(aoa));

            // Lift = ½ · ρ · v² · Cl · S  (simplified: mass-equivalent units)
            float liftForce = 0.5f
                * _stats.LiftCoefficient
                * liftMultiplier
                * liftAoaFactor
                * speed * speed
                * Time.fixedDeltaTime;

            // Ground effect bonus near terrain
            float geBonus = 1f;
            if (AltitudeMeters < groundEffectHeight)
            {
                float t = 1f - (AltitudeMeters / groundEffectHeight);
                geBonus = 1f + groundEffectLiftBonus * t;
            }

            _rb.AddForce(transform.up * liftForce * geBonus, ForceMode.Force);
        }

        private void ApplyDrag()
        {
            float normSpeed = Mathf.Clamp01(SpeedMS / _stats.MaxSpeed);
            float dragFactor = dragCurve.Evaluate(normSpeed) * _stats.DragCoefficient;

            if (AirbrakeInput > 0.5f)
                dragFactor *= airbrakeDragMultiplier;

            Vector3 dragForce = -_rb.linearVelocity.normalized
                                 * dragFactor
                                 * SpeedMS * SpeedMS
                                 * Time.fixedDeltaTime;

            _rb.AddForce(dragForce, ForceMode.Force);
        }

        private void ApplyControlTorques()
        {
            // Build target angular velocity in local space from pilot inputs
            float effectivePitch = _stats.PitchRate * PitchInput;
            float effectiveRoll  = _stats.RollRate  * RollInput;
            float effectiveYaw   = _stats.YawRate   * YawInput;

            // Scale authority with speed (low speed = reduced authority)
            float normSpeed    = Mathf.Clamp01(SpeedMS / _stats.MaxSpeed);
            float authority    = Mathf.Lerp(0.2f, 1f, normSpeed);

            Vector3 targetAngVel = new Vector3(-effectivePitch, effectiveYaw, -effectiveRoll)
                                   * authority * Mathf.Deg2Rad;

            // Smooth to target so controls feel weighty
            _smoothedAngularTarget = Vector3.Lerp(
                _smoothedAngularTarget,
                transform.TransformDirection(targetAngVel),
                Time.fixedDeltaTime / controlResponseTime
            );

            _rb.angularVelocity = _smoothedAngularTarget;
        }

        private void ClampMaxSpeed()
        {
            float cap = AfterburnerInput > 0.5f ? _stats.AfterburnerSpeed : _stats.MaxSpeed;
            if (_rb.linearVelocity.magnitude > cap)
            {
                _rb.linearVelocity = _rb.linearVelocity.normalized * cap;
            }
        }

        private void UpdateStallState()
        {
            float stallThreshold = _stats.MinSpeed * stallSpeedFactor;

            if (SpeedMS < stallThreshold)
            {
                _stallTimer += Time.fixedDeltaTime;
                IsStalled = _stallTimer > 0.2f;
            }
            else
            {
                _stallTimer = Mathf.Max(0f, _stallTimer - Time.fixedDeltaTime * (1f / stallRecoveryTime));
                if (_stallTimer <= 0f) IsStalled = false;
            }
        }

        // ─── Public helpers ───────────────────────────────────────────────────

        /// <summary>Instantly teleports and zeroes velocity (used for mission respawn).</summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            _rb.position   = position;
            _rb.rotation   = rotation;
            _rb.linearVelocity        = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        /// <summary>Gives an immediate velocity kick (catapult launch, explosion knockback).</summary>
        public void ApplyImpulse(Vector3 impulse)
        {
            _rb.AddForce(impulse, ForceMode.Impulse);
        }
    }
}
