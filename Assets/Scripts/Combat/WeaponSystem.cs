using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Manages all weapons on an aircraft: machine gun (object pool),
    /// guided missiles, and countermeasure flares.
    ///
    /// Reads fire commands from <see cref="FlightController"/> (player) or
    /// directly from AI.  Works alongside <see cref="LockOnSystem"/> for
    /// missile targeting.
    /// </summary>
    public class WeaponSystem : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Gun Settings")]
        [SerializeField] private Projectile     bulletPrefab;
        [SerializeField] private Transform[]    gunMuzzles;
        [SerializeField] private int            poolSize       = 50;
        [SerializeField] private float          gunFireRate    = 20f;    // rounds/second
        [SerializeField] private float          gunSpread      = 0.4f;   // degrees cone

        [Header("Missile Settings")]
        [SerializeField] private MissileGuidance missilePrefab;
        [SerializeField] private Transform[]     missileHardpoints;
        [SerializeField] private float           missileReloadTime = 2.5f;

        [Header("Countermeasures")]
        [SerializeField] private int   flareCount      = 24;
        [SerializeField] private float flareCooldown   = 1.2f;
        [SerializeField] private GameObject flarePrefab;

        [Header("References")]
        [SerializeField] private LockOnSystem  lockOnSystem;
        [SerializeField] private FlightController flightController;
        [SerializeField] private AircraftStats    aircraftStats;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired when the gun fires a round.  Carries muzzle position.</summary>
        public event Action<Vector3>  OnGunFired;

        /// <summary>Fired when a missile is launched.  Carries the MissileGuidance instance.</summary>
        public event Action<MissileGuidance> OnMissileLaunched;

        /// <summary>Fired when a flare is deployed.</summary>
        public event Action OnFlareDeployed;

        /// <summary>Fired when gun ammo hits zero.</summary>
        public event Action OnGunEmpty;

        /// <summary>Fired when missile count changes.  Carries new count.</summary>
        public event Action<int> OnMissileCountChanged;

        // ─── Runtime state ────────────────────────────────────────────────────
        public int   GunAmmoRemaining     { get; private set; }
        public int   MissilesRemaining    { get; private set; }
        public int   FlaresRemaining      { get; private set; }
        public bool  IsReloading          { get; private set; }
        public bool  GunFiring            { get; private set; }

        // ─── Private ──────────────────────────────────────────────────────────
        private readonly Queue<Projectile>      _bulletPool     = new Queue<Projectile>();
        private readonly List<MissileGuidance>  _activeMissiles = new List<MissileGuidance>();

        private int   _muzzleIndex;
        private int   _hardpointIndex;
        private float _gunCooldown;
        private float _flareCooldownTimer;
        private Coroutine _reloadRoutine;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (aircraftStats == null)
                aircraftStats = GetComponent<AircraftStats>();

            GunAmmoRemaining  = aircraftStats != null ? aircraftStats.GunAmmo    : 500;
            MissilesRemaining = aircraftStats != null ? aircraftStats.MissileCount : 4;
            FlaresRemaining   = flareCount;

            BuildBulletPool();
        }

        private void Update()
        {
            if (_gunCooldown > 0f)
                _gunCooldown      -= Time.deltaTime;
            if (_flareCooldownTimer > 0f)
                _flareCooldownTimer -= Time.deltaTime;

            // Player input
            if (flightController != null)
            {
                if (flightController.FiringGun)
                    TryFireGun();

                if (flightController.FiringMissile)
                    TryFireMissile();
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Attempts to fire the gun.  Respects fire rate and ammo limits.</summary>
        public bool TryFireGun()
        {
            if (_gunCooldown > 0f || GunAmmoRemaining <= 0 || gunMuzzles.Length == 0)
            {
                GunFiring = false;
                if (GunAmmoRemaining <= 0) OnGunEmpty?.Invoke();
                return false;
            }

            _gunCooldown = 1f / gunFireRate;
            GunFiring    = true;

            Transform muzzle = gunMuzzles[_muzzleIndex % gunMuzzles.Length];
            _muzzleIndex++;

            // Spread
            Vector3 spreadDir = ApplySpread(muzzle.forward, gunSpread);

            Projectile bullet = GetFromPool();
            if (bullet != null)
            {
                Rigidbody ownerRb = GetComponent<Rigidbody>();
                Vector3 inherit   = ownerRb != null ? ownerRb.linearVelocity : Vector3.zero;
                bullet.Launch(muzzle.position, spreadDir, inherit, gameObject.layer);
            }

            GunAmmoRemaining--;
            OnGunFired?.Invoke(muzzle.position);
            return true;
        }

        /// <summary>Attempts to fire a guided missile at the current lock-on target.</summary>
        public bool TryFireMissile()
        {
            if (MissilesRemaining <= 0 || IsReloading) return false;
            if (missilePrefab == null || missileHardpoints.Length == 0) return false;

            Transform target = lockOnSystem != null && lockOnSystem.HasLock
                ? lockOnSystem.CurrentTarget
                : null;

            Transform hp = missileHardpoints[_hardpointIndex % missileHardpoints.Length];
            _hardpointIndex++;

            MissileGuidance missile = Instantiate(missilePrefab, hp.position, hp.rotation);
            Rigidbody ownerRb = GetComponent<Rigidbody>();
            Vector3 inherit   = ownerRb != null ? ownerRb.linearVelocity : Vector3.zero;
            missile.Launch(target, inherit);
            _activeMissiles.Add(missile);

            MissilesRemaining--;
            OnMissileLaunched?.Invoke(missile);
            OnMissileCountChanged?.Invoke(MissilesRemaining);

            if (_reloadRoutine == null)
                _reloadRoutine = StartCoroutine(ReloadMissiles());

            return true;
        }

        /// <summary>Deploys a flare to attempt breaking incoming missile locks.</summary>
        public bool DeployFlare()
        {
            if (FlaresRemaining <= 0 || _flareCooldownTimer > 0f) return false;

            FlaresRemaining--;
            _flareCooldownTimer = flareCooldown;

            if (flarePrefab != null)
            {
                GameObject flare = Instantiate(flarePrefab, transform.position, Quaternion.identity);
                Destroy(flare, 5f);
            }

            // Notify any missiles that may be tracking this aircraft
            OnFlareDeployed?.Invoke();
            return true;
        }

        /// <summary>Immediately replenishes ammo (called by re-arm zones or GameManager).</summary>
        public void Rearm()
        {
            GunAmmoRemaining  = aircraftStats != null ? aircraftStats.GunAmmo     : 500;
            MissilesRemaining = aircraftStats != null ? aircraftStats.MissileCount : 4;
            FlaresRemaining   = flareCount;
            OnMissileCountChanged?.Invoke(MissilesRemaining);
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void BuildBulletPool()
        {
            if (bulletPrefab == null) return;
            for (int i = 0; i < poolSize; i++)
            {
                Projectile p = Instantiate(bulletPrefab);
                p.gameObject.SetActive(false);
                p.OnExpired += ReturnToPool;
                _bulletPool.Enqueue(p);
            }
        }

        private Projectile GetFromPool()
        {
            if (_bulletPool.Count == 0) return null;
            Projectile p = _bulletPool.Dequeue();
            return p;
        }

        private void ReturnToPool(Projectile p)
        {
            p.gameObject.SetActive(false);
            _bulletPool.Enqueue(p);
        }

        private Vector3 ApplySpread(Vector3 dir, float spreadDeg)
        {
            float angle = UnityEngine.Random.Range(-spreadDeg * 0.5f, spreadDeg * 0.5f);
            Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up)
                           * Quaternion.AngleAxis(UnityEngine.Random.Range(-spreadDeg * 0.5f, spreadDeg * 0.5f), Vector3.right);
            return rot * dir;
        }

        private IEnumerator ReloadMissiles()
        {
            IsReloading = true;
            int maxMissiles = aircraftStats != null ? aircraftStats.MissileCount : 4;

            while (MissilesRemaining < maxMissiles)
            {
                yield return new WaitForSeconds(missileReloadTime);
                MissilesRemaining++;
                OnMissileCountChanged?.Invoke(MissilesRemaining);
            }

            IsReloading    = false;
            _reloadRoutine = null;
        }
    }
}
