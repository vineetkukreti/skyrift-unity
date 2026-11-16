using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Runtime wrapper around an AircraftStats_SO ScriptableObject.
    /// Attach to the aircraft root GameObject. FlightPhysics and
    /// WeaponSystem read from this component so they stay data-driven.
    /// </summary>
    public class AircraftStats : MonoBehaviour
    {
        [SerializeField] private AircraftStats_SO data;

        // ─── Forwarded properties (read-only at runtime) ──────────────────────
        public string AircraftName    => data != null ? data.aircraftName    : "Unknown";
        public float  MaxSpeed        => data != null ? data.maxSpeed        : 300f;
        public float  MinSpeed        => data != null ? data.minSpeed        : 60f;
        public float  AfterburnerSpeed=> data != null ? data.afterburnerSpeed: 400f;
        public float  EngineThrust    => data != null ? data.engineThrust    : 120000f;
        public float  AfterburnerThrust=> data != null ? data.afterburnerThrust: 200000f;
        public float  LiftCoefficient => data != null ? data.liftCoefficient : 2.5f;
        public float  DragCoefficient => data != null ? data.dragCoefficient : 0.03f;
        public float  Mass            => data != null ? data.mass            : 15000f;
        public float  PitchRate       => data != null ? data.pitchRate       : 60f;
        public float  RollRate        => data != null ? data.rollRate        : 120f;
        public float  YawRate         => data != null ? data.yawRate         : 30f;
        public float  MaxHealth       => data != null ? data.maxHealth       : 1000f;
        public int    MissileCount    => data != null ? data.missileCount    : 4;
        public int    GunAmmo         => data != null ? data.gunAmmo         : 500;
        public float  GunFireRate     => data != null ? data.gunFireRate     : 20f;
        public float  TurnRadius      => data != null ? data.turnRadius      : 800f;
        public Sprite HUDIcon         => data != null ? data.hudIcon         : null;

        // ─── Mutable runtime health ───────────────────────────────────────────
        private float _currentHealth;
        public  float CurrentHealth => _currentHealth;
        public  bool  IsDestroyed   => _currentHealth <= 0f;

        private void Awake()
        {
            _currentHealth = MaxHealth;
        }

        /// <summary>Applies damage to the aircraft. Returns true if the aircraft is destroyed.</summary>
        public bool ApplyDamage(float amount)
        {
            _currentHealth = Mathf.Max(0f, _currentHealth - amount);
            return IsDestroyed;
        }

        /// <summary>Restores health by the given amount, clamped to MaxHealth.</summary>
        public void Repair(float amount)
        {
            _currentHealth = Mathf.Min(MaxHealth, _currentHealth + amount);
        }

        /// <summary>Returns health as a 0–1 normalised fraction.</summary>
        public float HealthNormalized => MaxHealth > 0f ? _currentHealth / MaxHealth : 0f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (data == null)
                Debug.LogWarning($"[AircraftStats] No AircraftStats_SO assigned on {gameObject.name}.", this);
        }
#endif
    }
}
