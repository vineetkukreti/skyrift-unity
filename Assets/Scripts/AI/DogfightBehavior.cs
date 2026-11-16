using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// High-quality dogfight maneuver library used by <see cref="EnemyAI"/>
    /// when in ATTACK or EVADE state.
    ///
    /// Provides: Immelmann turn, Split-S, barrel roll, break turn, high-yo-yo,
    /// and energy management.  Each maneuver runs as a timed sub-routine and
    /// drives <see cref="FlightPhysics"/> inputs directly.
    /// </summary>
    [RequireComponent(typeof(FlightPhysics))]
    public class DogfightBehavior : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Maneuver Thresholds")]
        [SerializeField] private float immelmannMinSpeed   = 180f;  // m/s
        [SerializeField] private float splitSMaxAltitude   = 4000f; // metres
        [SerializeField] private float highYoYoMinAngle    = 60f;   // degrees off-target

        [Header("Energy Management")]
        [SerializeField] private float optimalFightingSpeed = 250f;  // m/s
        [SerializeField] private float lowSpeedThreshold    = 120f;

        // ─── Enum ─────────────────────────────────────────────────────────────
        private enum Maneuver { None, Immelmann, SplitS, BarrelRoll, BreakTurn, HighYoYo }

        // ─── Private ──────────────────────────────────────────────────────────
        private FlightPhysics _physics;
        private Maneuver      _current = Maneuver.None;
        private float         _maneuverTimer;
        private float         _maneuverDuration;
        private int           _phase;             // step within a multi-phase maneuver

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _physics = GetComponent<FlightPhysics>();
        }

        private void Update()
        {
            if (_current != Maneuver.None)
            {
                _maneuverTimer += Time.deltaTime;
                if (_maneuverTimer >= _maneuverDuration)
                    EndManeuver();
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Picks and initiates the best maneuver given current flight and target data.
        /// Returns false if already executing a maneuver.
        /// </summary>
        public bool SelectManeuver(Transform target)
        {
            if (_current != Maneuver.None) return false;
            if (target == null) return false;

            float speed = _physics.SpeedMS;
            float alt   = _physics.AltitudeMeters;
            float angleToTarget = Vector3.Angle(transform.forward,
                                                target.position - transform.position);

            // Energy recovery first
            if (speed < lowSpeedThreshold)
            {
                StartManeuver(Maneuver.SplitS, 6f);
                return true;
            }

            // Overshoot → high Yo-Yo
            if (angleToTarget > highYoYoMinAngle && speed > optimalFightingSpeed * 0.9f)
            {
                StartManeuver(Maneuver.HighYoYo, 4f);
                return true;
            }

            // Immelmann if going fast and want to reverse
            if (speed >= immelmannMinSpeed && alt < splitSMaxAltitude - 500f)
            {
                StartManeuver(Maneuver.Immelmann, 5f);
                return true;
            }

            // Default: aggressive break turn
            StartManeuver(Maneuver.BreakTurn, 3.5f);
            return true;
        }

        /// <summary>Returns true if a maneuver is currently being executed.</summary>
        public bool IsExecutingManeuver => _current != Maneuver.None;

        // ─── Maneuver execution ───────────────────────────────────────────────
        private void FixedUpdate()
        {
            switch (_current)
            {
                case Maneuver.Immelmann: ExecuteImmelmann(); break;
                case Maneuver.SplitS:    ExecuteSplitS();    break;
                case Maneuver.BarrelRoll:ExecuteBarrelRoll();break;
                case Maneuver.BreakTurn: ExecuteBreakTurn(); break;
                case Maneuver.HighYoYo:  ExecuteHighYoYo();  break;
            }
        }

        /// <summary>Pull-up half-loop followed by roll to normal flight.  Reverses direction.</summary>
        private void ExecuteImmelmann()
        {
            if (_phase == 0)
            {
                // Pull up hard
                _physics.PitchInput    = -1f;
                _physics.RollInput     =  0f;
                _physics.ThrottleInput =  1f;
                if (_maneuverTimer > _maneuverDuration * 0.5f) _phase = 1;
            }
            else
            {
                // Roll upright
                _physics.PitchInput = 0f;
                _physics.RollInput  = 1f;
            }
        }

        /// <summary>Roll inverted then pull through.  Good energy regain and direction reversal.</summary>
        private void ExecuteSplitS()
        {
            if (_phase == 0)
            {
                _physics.RollInput  =  1f;   // roll inverted
                _physics.PitchInput =  0f;
                if (_maneuverTimer > 1.2f) _phase = 1;
            }
            else
            {
                _physics.RollInput     = 0f;
                _physics.PitchInput    = -1f;   // pull through (now pulling down = inverted nose up)
                _physics.ThrottleInput = 1f;
            }
        }

        /// <summary>Corkscrew roll to throw off a pursuer.</summary>
        private void ExecuteBarrelRoll()
        {
            float t = _maneuverTimer / _maneuverDuration;
            _physics.RollInput  = Mathf.Sin(t * Mathf.PI * 2f);
            _physics.PitchInput = Mathf.Cos(t * Mathf.PI * 2f) * 0.5f;
            _physics.ThrottleInput = 0.8f;
        }

        /// <summary>Maximum-rate turn to defeat a close trailing enemy.</summary>
        private void ExecuteBreakTurn()
        {
            // Pick break direction (always break right for now; could randomise)
            _physics.RollInput     =  1f;
            _physics.PitchInput    = -1f;
            _physics.ThrottleInput =  1f;
            _physics.YawInput      =  0.3f;
        }

        /// <summary>Climb above the target's flight path then swoop back down on them.</summary>
        private void ExecuteHighYoYo()
        {
            if (_phase == 0)
            {
                // Pull up into a high arc
                _physics.PitchInput    = -1f;
                _physics.ThrottleInput =  1f;
                _physics.AfterburnerInput = 1f;
                if (_maneuverTimer > _maneuverDuration * 0.4f) _phase = 1;
            }
            else
            {
                // Roll and pull back toward target heading
                _physics.RollInput  =  1f;
                _physics.PitchInput = -0.7f;
                _physics.AfterburnerInput = 0f;
            }
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void StartManeuver(Maneuver m, float duration)
        {
            _current          = m;
            _maneuverTimer    = 0f;
            _maneuverDuration = duration;
            _phase            = 0;
        }

        private void EndManeuver()
        {
            _current = Maneuver.None;
            // Reset control inputs so the parent AI takes back control cleanly
            _physics.PitchInput       = 0f;
            _physics.RollInput        = 0f;
            _physics.YawInput         = 0f;
            _physics.AfterburnerInput = 0f;
        }
    }
}
