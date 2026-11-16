using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Enemy aircraft AI.  Drives <see cref="FlightPhysics"/> using
    /// an <see cref="AIStateMachine"/> to cycle through
    /// PATROL → ENGAGE → ATTACK → EVADE → RETREAT.
    ///
    /// Steering uses a pure-pursuit / lead-pursuit blend so the aircraft
    /// curves naturally toward its target rather than snapping.
    /// </summary>
    [RequireComponent(typeof(FlightPhysics))]
    [RequireComponent(typeof(WeaponSystem))]
    [RequireComponent(typeof(DamageSystem))]
    [RequireComponent(typeof(AircraftStats))]
    public class EnemyAI : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Detection")]
        [SerializeField] private float detectionRange   = 2500f;
        [SerializeField] private float disengageRange   = 3500f;
        [SerializeField] private float attackRange      = 800f;
        [SerializeField] private float attackConeAngle  = 25f;    // degrees half-angle

        [Header("Patrol")]
        [SerializeField] private Transform[] patrolWaypoints;
        [SerializeField] private float       waypointRadius = 150f;

        [Header("Combat Behaviour")]
        [SerializeField] private float pursuitLeadFactor = 1.2f;  // 1=pure pursuit, 2=full lead
        [SerializeField] private float extensionDistance = 1200f; // break-away distance
        [SerializeField] private float burstDuration     = 0.6f;
        [SerializeField] private float burstCooldown     = 1.4f;
        [SerializeField] private float retreatHealthPct  = 0.25f;

        [Header("Formation")]
        [SerializeField] private bool             isWingman     = false;
        [SerializeField] private FormationFlying  formationCtrl;

        // ─── Runtime state (readable by AIStateMachine transitions) ──────────
        public float DistanceToPlayer { get; private set; }
        public float AngleToPlayer    { get; private set; }
        public float StateTimer       => _stateMachine?.StateTimer ?? 0f;

        // ─── Private ──────────────────────────────────────────────────────────
        private FlightPhysics  _physics;
        private WeaponSystem   _weapons;
        private DamageSystem   _damage;
        private AircraftStats  _stats;
        private AIStateMachine _stateMachine;
        private Transform      _player;

        private int   _patrolIndex;
        private float _burstTimer;
        private bool  _burstFiring;
        private Vector3 _evadeTarget;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _physics = GetComponent<FlightPhysics>();
            _weapons = GetComponent<WeaponSystem>();
            _damage  = GetComponent<DamageSystem>();
            _stats   = GetComponent<AircraftStats>();

            BuildStateMachine();

            _damage.OnDamageTaken += OnDamageTaken;
            _damage.OnDestroyed   += _ => _stateMachine.ForceState(AIState.Dead);
        }

        private void Start()
        {
            // Find player – assumes player aircraft is tagged "Player"
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null) _player = playerGO.transform;
        }

        private void Update()
        {
            if (_stateMachine.CurrentState == AIState.Dead) return;

            UpdateSensorReadings();
            _stateMachine.Tick(Time.deltaTime);
            ExecuteState();
        }

        // ─── State machine setup ─────────────────────────────────────────────
        private void BuildStateMachine()
        {
            _stateMachine = new AIStateMachine(this, AIState.Patrol);
            _stateMachine.OnStateChanged += OnStateChanged;

            // Patrol → Engage
            _stateMachine.AddTransition(AIState.Patrol,
                new TransitionPlayerInRange(detectionRange, AIState.Engage));

            // Engage → Attack
            _stateMachine.AddTransition(AIState.Engage,
                new TransitionInAttackCone(attackRange, attackConeAngle, AIState.Attack));

            // Engage → Retreat (low health)
            _stateMachine.AddTransition(AIState.Engage,
                new TransitionHealthBelow(retreatHealthPct, AIState.Retreat));

            // Attack → Evade (player escapes attack range)
            _stateMachine.AddTransition(AIState.Attack,
                new TransitionPlayerOutOfRange(attackRange * 1.5f, AIState.Engage));

            // Attack → Retreat
            _stateMachine.AddTransition(AIState.Attack,
                new TransitionHealthBelow(retreatHealthPct, AIState.Retreat));

            // Evade → Engage (after evade duration)
            _stateMachine.AddTransition(AIState.Evade,
                new TransitionEvadeCooldown(3.5f, AIState.Engage));

            // Evade → Retreat
            _stateMachine.AddTransition(AIState.Evade,
                new TransitionHealthBelow(retreatHealthPct * 0.5f, AIState.Retreat));
        }

        // ─── Per-frame behaviour ──────────────────────────────────────────────
        private void ExecuteState()
        {
            switch (_stateMachine.CurrentState)
            {
                case AIState.Patrol:  ExecutePatrol();  break;
                case AIState.Engage:  ExecuteEngage();  break;
                case AIState.Attack:  ExecuteAttack();  break;
                case AIState.Evade:   ExecuteEvade();   break;
                case AIState.Retreat: ExecuteRetreat(); break;
            }
        }

        private void ExecutePatrol()
        {
            if (patrolWaypoints == null || patrolWaypoints.Length == 0) return;

            Transform wp = patrolWaypoints[_patrolIndex % patrolWaypoints.Length];
            float dist = Vector3.Distance(transform.position, wp.position);
            if (dist < waypointRadius)
                _patrolIndex++;

            SteerToward(wp.position, throttle: 0.6f);
        }

        private void ExecuteEngage()
        {
            if (_player == null) return;

            // Climb to attacker altitude (1000m above player)
            Vector3 attackPos = _player.position + Vector3.up * 1000f;
            SteerToward(attackPos, throttle: 1f);
        }

        private void ExecuteAttack()
        {
            if (_player == null) return;

            // Lead-pursuit: aim ahead of target
            Rigidbody playerRb = _player.GetComponent<Rigidbody>();
            Vector3 leadPos    = _player.position;
            if (playerRb != null)
            {
                float timeToTarget = DistanceToPlayer / Mathf.Max(_physics.SpeedMS, 1f);
                leadPos += playerRb.linearVelocity * timeToTarget * pursuitLeadFactor;
            }

            SteerToward(leadPos, throttle: 1f);

            // Burst fire
            UpdateBurstFire();

            // Extension break — if we overshoot, break off
            if (DistanceToPlayer < 200f)
            {
                _evadeTarget = transform.position - transform.forward * extensionDistance
                             + transform.right * extensionDistance * 0.5f;
                _stateMachine.ForceState(AIState.Evade);
            }
        }

        private void ExecuteEvade()
        {
            // Break hard turn away from player
            SteerToward(_evadeTarget, throttle: 1f);
            _physics.AfterburnerInput = 1f;
        }

        private void ExecuteRetreat()
        {
            // Fly away from player at max speed
            if (_player == null) return;
            Vector3 awayDir    = (transform.position - _player.position).normalized;
            Vector3 retreatDst = transform.position + awayDir * 5000f;
            SteerToward(retreatDst, throttle: 1f);
            _physics.AfterburnerInput = 1f;
        }

        // ─── Steering helper ─────────────────────────────────────────────────
        /// <summary>Drives FlightPhysics inputs to steer toward a world-space target.</summary>
        private void SteerToward(Vector3 worldTarget, float throttle)
        {
            Vector3 localTarget = transform.InverseTransformPoint(worldTarget);

            // Normalise
            float mag = localTarget.magnitude;
            if (mag < 0.1f) return;
            localTarget /= mag;

            // Derive pitch/roll/yaw from local target direction
            float pitchErr = -localTarget.y;                          // positive = nose up
            float rollErr  =  localTarget.x;                          // bank into turn
            float yawErr   =  localTarget.x * 0.3f;                   // coordination

            _physics.PitchInput    = Mathf.Clamp(pitchErr  * 2.5f, -1f, 1f);
            _physics.RollInput     = Mathf.Clamp(rollErr   * 2f,   -1f, 1f);
            _physics.YawInput      = Mathf.Clamp(yawErr,           -1f, 1f);
            _physics.ThrottleInput = throttle;
        }

        // ─── Burst fire ───────────────────────────────────────────────────────
        private void UpdateBurstFire()
        {
            _burstTimer += Time.deltaTime;

            if (_burstFiring)
            {
                _weapons.TryFireGun();
                if (_burstTimer >= burstDuration)
                {
                    _burstFiring = false;
                    _burstTimer  = 0f;
                }
            }
            else
            {
                if (_burstTimer >= burstCooldown && AngleToPlayer < attackConeAngle)
                {
                    _burstFiring = true;
                    _burstTimer  = 0f;
                }
            }
        }

        // ─── Sensor update ────────────────────────────────────────────────────
        private void UpdateSensorReadings()
        {
            if (_player == null) return;
            DistanceToPlayer = Vector3.Distance(transform.position, _player.position);
            AngleToPlayer    = Vector3.Angle(transform.forward,
                                             _player.position - transform.position);
        }

        // ─── Event callbacks ─────────────────────────────────────────────────
        private void OnDamageTaken(float normHealth)
        {
            if (_stateMachine.CurrentState != AIState.Evade
                && _stateMachine.CurrentState != AIState.Retreat
                && normHealth < 0.6f)
            {
                _evadeTarget = transform.position
                             + (-transform.forward + transform.right * 0.7f).normalized
                             * extensionDistance;
                _stateMachine.ForceState(AIState.Evade);
            }
        }

        private void OnStateChanged(AIState prev, AIState next)
        {
            // Reset afterburner unless we explicitly need it
            if (next != AIState.Evade && next != AIState.Retreat)
                _physics.AfterburnerInput = 0f;
        }
    }
}
