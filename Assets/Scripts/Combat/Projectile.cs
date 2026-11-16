using System;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Simple kinematic projectile (cannon rounds, unguided rockets).
    /// Moved via Rigidbody; no guidance logic — see <see cref="MissileGuidance"/>
    /// for guided variants.
    ///
    /// Returned to a pool by <see cref="WeaponSystem"/> when lifetime expires
    /// or on collision.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Ballistics")]
        [SerializeField] private float speed         = 800f;    // m/s muzzle velocity
        [SerializeField] private float lifetime      = 2.5f;    // seconds
        [SerializeField] private float damage        = 35f;
        [SerializeField] private float blastRadius   = 0f;      // 0 = point damage only

        [Header("VFX")]
        [SerializeField] private GameObject hitEffect;
        [SerializeField] private TrailRenderer trailRenderer;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired when the projectile hits something. Passes world hit point.</summary>
        public event Action<Vector3> OnHit;

        /// <summary>Fired when lifetime expires or on explicit recycle.</summary>
        public event Action<Projectile> OnExpired;

        // ─── Private ──────────────────────────────────────────────────────────
        private Rigidbody _rb;
        private float     _timer;
        private bool      _active;
        private int       _ownerLayer;   // ignore collisions with the firing aircraft

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _rb             = GetComponent<Rigidbody>();
            _rb.useGravity  = false;
            _rb.isKinematic = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private void FixedUpdate()
        {
            if (!_active) return;

            _timer += Time.fixedDeltaTime;
            if (_timer >= lifetime)
            {
                Expire();
            }
        }

        private void OnCollisionEnter(Collision col)
        {
            if (!_active) return;

            // Skip collisions with the owner's layer
            if (col.gameObject.layer == _ownerLayer) return;

            Vector3 hitPoint = col.GetContact(0).point;
            ApplyDamage(col.gameObject, hitPoint);
            SpawnHitEffect(hitPoint);
            OnHit?.Invoke(hitPoint);
            Expire();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Initialises and launches the projectile from a pool.
        /// </summary>
        /// <param name="origin">Muzzle world position.</param>
        /// <param name="direction">Normalised fire direction.</param>
        /// <param name="inheritVelocity">Velocity of the firing platform (added to muzzle).</param>
        /// <param name="ownerLayer">GameObject layer of the firing aircraft (ignored for self-collision).</param>
        public void Launch(Vector3 origin, Vector3 direction, Vector3 inheritVelocity, int ownerLayer)
        {
            transform.position  = origin;
            transform.rotation  = Quaternion.LookRotation(direction);
            _rb.linearVelocity  = direction * speed + inheritVelocity;
            _ownerLayer         = ownerLayer;
            _timer              = 0f;
            _active             = true;
            gameObject.SetActive(true);

            if (trailRenderer != null)
            {
                trailRenderer.Clear();
                trailRenderer.emitting = true;
            }
        }

        /// <summary>Returns the projectile to the pool (called by WeaponSystem).</summary>
        public void Recycle()
        {
            Expire();
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void ApplyDamage(GameObject hit, Vector3 point)
        {
            if (blastRadius > 0f)
            {
                // Splash damage
                Collider[] hits = Physics.OverlapSphere(point, blastRadius);
                foreach (var c in hits)
                {
                    float dist    = Vector3.Distance(c.transform.position, point);
                    float falloff = 1f - Mathf.Clamp01(dist / blastRadius);
                    TryDamage(c.gameObject, damage * falloff);
                }
            }
            else
            {
                TryDamage(hit, damage);
            }
        }

        private static void TryDamage(GameObject go, float amount)
        {
            var dmg = go.GetComponentInParent<DamageSystem>();
            if (dmg != null)
                dmg.TakeDamage(amount);
        }

        private void SpawnHitEffect(Vector3 point)
        {
            if (hitEffect != null)
                Instantiate(hitEffect, point, Quaternion.identity);
        }

        private void Expire()
        {
            _active = false;
            if (trailRenderer != null)
                trailRenderer.emitting = false;

            gameObject.SetActive(false);
            OnExpired?.Invoke(this);
        }
    }
}
