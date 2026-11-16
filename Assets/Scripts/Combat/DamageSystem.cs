using System;
using UnityEngine;
using UnityEngine.Events;

namespace Skyrift
{
    /// <summary>
    /// Receives damage from projectiles, missiles, and collisions.
    /// Notifies listeners and coordinates with <see cref="AircraftStats"/>
    /// for health tracking.  Spawns destruction VFX and raises kill events
    /// consumed by <see cref="MissionManager"/> and <see cref="GameManager"/>.
    /// </summary>
    public class DamageSystem : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Settings")]
        [SerializeField] private bool       isPlayer          = false;
        [SerializeField] private float      invincibilityTime = 0.1f;   // brief window after a hit
        [SerializeField] private float      collisionDamagePerMs = 0.8f; // damage per m/s of impact

        [Header("VFX / SFX")]
        [SerializeField] private GameObject smokeTrailPrefab;
        [SerializeField] private GameObject firePrefab;
        [SerializeField] private GameObject explosionPrefab;
        [SerializeField] private float      smokeHealthThreshold = 0.5f;
        [SerializeField] private float      fireHealthThreshold  = 0.25f;

        [Header("Unity Events (inspector wiring)")]
        public UnityEvent OnDamageTakenEvent;
        public UnityEvent OnDestroyedEvent;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired each time damage is applied.  Carries remaining health 0..1.</summary>
        public event Action<float> OnDamageTaken;

        /// <summary>Fired once when health reaches zero.  Carries this DamageSystem.</summary>
        public event Action<DamageSystem> OnDestroyed;

        // ─── Runtime state ────────────────────────────────────────────────────
        public bool IsDestroyed { get; private set; }

        // ─── Private ──────────────────────────────────────────────────────────
        private AircraftStats _stats;
        private float         _iFrameTimer;
        private bool          _smokeActive;
        private bool          _fireActive;
        private GameObject    _smokeInstance;
        private GameObject    _fireInstance;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _stats = GetComponent<AircraftStats>();
        }

        private void Update()
        {
            if (_iFrameTimer > 0f)
                _iFrameTimer -= Time.deltaTime;

            if (_stats == null || IsDestroyed) return;

            UpdateDamageVFX();
        }

        private void OnCollisionEnter(Collision col)
        {
            if (IsDestroyed) return;

            float impactSpeed = col.relativeVelocity.magnitude;
            float dmg = impactSpeed * collisionDamagePerMs;
            if (dmg > 10f)
                TakeDamage(dmg);
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Applies <paramref name="amount"/> damage.
        /// Respects i-frame window set after the previous hit.
        /// </summary>
        public void TakeDamage(float amount)
        {
            if (IsDestroyed || _iFrameTimer > 0f) return;

            _iFrameTimer = invincibilityTime;

            bool destroyed = false;
            if (_stats != null)
                destroyed = _stats.ApplyDamage(amount);
            else
                destroyed = true;   // no stats component — treat as one-shot

            float normHealth = _stats != null ? _stats.HealthNormalized : 0f;
            OnDamageTaken?.Invoke(normHealth);
            OnDamageTakenEvent?.Invoke();

            if (destroyed) Destroy_Internal();
        }

        /// <summary>Instantly destroys the aircraft regardless of current health.</summary>
        public void ForceDestroy()
        {
            if (IsDestroyed) return;
            if (_stats != null) _stats.ApplyDamage(_stats.MaxHealth);
            Destroy_Internal();
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void Destroy_Internal()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;

            SpawnExplosion();
            CleanupDamageVFX();

            OnDestroyed?.Invoke(this);
            OnDestroyedEvent?.Invoke();

            if (isPlayer)
            {
                GameManager.Instance?.SetGameState(GameState.GameOver);
            }
            else
            {
                GameManager.Instance?.AddKillScore();
            }

            // Disable flight and physics
            FlightPhysics fp = GetComponent<FlightPhysics>();
            if (fp != null) fp.enabled = false;

            // Delay actual GameObject removal
            Destroy(gameObject, 4f);
        }

        private void UpdateDamageVFX()
        {
            float hp = _stats.HealthNormalized;

            if (!_smokeActive && hp < smokeHealthThreshold)
            {
                _smokeActive   = true;
                if (smokeTrailPrefab != null)
                    _smokeInstance = Instantiate(smokeTrailPrefab, transform);
            }

            if (!_fireActive && hp < fireHealthThreshold)
            {
                _fireActive = true;
                if (firePrefab != null)
                    _fireInstance = Instantiate(firePrefab, transform);
            }
        }

        private void CleanupDamageVFX()
        {
            if (_smokeInstance != null) Destroy(_smokeInstance);
            if (_fireInstance  != null) Destroy(_fireInstance);
        }

        private void SpawnExplosion()
        {
            if (explosionPrefab != null)
                Instantiate(explosionPrefab, transform.position + Vector3.up * 2f, Quaternion.identity);
        }
    }
}
