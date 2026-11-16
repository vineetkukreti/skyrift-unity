using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Radar / lock-on system.  Scans for targets within a cone in front of
    /// the aircraft, progressively acquires a lock, and raises events for the
    /// HUD and WeaponSystem to react to.
    /// </summary>
    public class LockOnSystem : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Scan Cone")]
        [SerializeField] private float scanRange      = 3000f;  // metres
        [SerializeField] private float scanAngle      = 40f;    // half-angle degrees
        [SerializeField] private float lockAcquireTime = 1.8f;  // seconds to full lock
        [SerializeField] private float lockLossTime   = 0.6f;   // seconds out-of-cone to lose lock

        [Header("Target Filter")]
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private string    targetTag  = "Enemy";

        [Header("Flares")]
        [Tooltip("Probability (0–1) that a flare successfully breaks a missile lock.")]
        [SerializeField] private float flareLockBreakChance = 0.75f;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Lock-on progress 0..1; 1 = full lock.  Fired every frame while locking.</summary>
        public event Action<float>     OnLockProgress;

        /// <summary>Fired once when a full lock is achieved.</summary>
        public event Action<Transform> OnLockAcquired;

        /// <summary>Fired when lock is broken (out of cone, target dead, or flares).</summary>
        public event Action            OnLockLost;

        // ─── Runtime state (readable by HUD and WeaponSystem) ─────────────────
        public Transform CurrentTarget  { get; private set; }
        public bool      HasLock        { get; private set; }
        public float     LockProgress   { get; private set; }   // 0..1

        // ─── Private ──────────────────────────────────────────────────────────
        private float  _lockTimer;
        private float  _lossTimer;
        private readonly List<Transform> _candidates = new List<Transform>(16);

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Update()
        {
            ScanForCandidates();
            UpdateLock();
        }

        // ─── Private methods ─────────────────────────────────────────────────
        private void ScanForCandidates()
        {
            _candidates.Clear();
            Collider[] hits = Physics.OverlapSphere(transform.position, scanRange, targetLayers);
            foreach (var col in hits)
            {
                if (!col.CompareTag(targetTag)) continue;
                if (!IsInScanCone(col.transform)) continue;
                if (!HasLineOfSight(col.transform)) continue;
                _candidates.Add(col.transform);
            }
        }

        private void UpdateLock()
        {
            // If no current target, pick closest candidate
            if (CurrentTarget == null && _candidates.Count > 0)
            {
                CurrentTarget = GetClosestCandidate();
                _lockTimer    = 0f;
                _lossTimer    = 0f;
            }

            if (CurrentTarget == null)
            {
                ResetLock();
                return;
            }

            bool inCone = _candidates.Contains(CurrentTarget);

            if (inCone)
            {
                _lossTimer = 0f;
                _lockTimer += Time.deltaTime;
                LockProgress = Mathf.Clamp01(_lockTimer / lockAcquireTime);
                OnLockProgress?.Invoke(LockProgress);

                if (!HasLock && LockProgress >= 1f)
                {
                    HasLock = true;
                    OnLockAcquired?.Invoke(CurrentTarget);
                }
            }
            else
            {
                _lossTimer += Time.deltaTime;
                if (_lossTimer >= lockLossTime)
                {
                    BreakLock();
                }
            }
        }

        private bool IsInScanCone(Transform t)
        {
            Vector3 dir   = (t.position - transform.position).normalized;
            float   angle = Vector3.Angle(transform.forward, dir);
            return angle <= scanAngle;
        }

        private bool HasLineOfSight(Transform t)
        {
            Vector3 origin = transform.position;
            Vector3 dir    = t.position - origin;
            float   dist   = dir.magnitude;

            if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                // LoS blocked if the hit transform isn't the target itself
                return hit.transform == t || hit.transform.IsChildOf(t);
            }
            return true;
        }

        private Transform GetClosestCandidate()
        {
            Transform best = null;
            float     bestDist = float.MaxValue;
            foreach (var t in _candidates)
            {
                float d = Vector3.Distance(transform.position, t.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best     = t;
                }
            }
            return best;
        }

        private void ResetLock()
        {
            LockProgress   = 0f;
            _lockTimer     = 0f;
            _lossTimer     = 0f;
            bool hadLock   = HasLock;
            HasLock        = false;
            CurrentTarget  = null;
            if (hadLock) OnLockLost?.Invoke();
        }

        private void BreakLock()
        {
            CurrentTarget = null;
            bool hadLock  = HasLock;
            HasLock       = false;
            LockProgress  = 0f;
            _lockTimer    = 0f;
            if (hadLock) OnLockLost?.Invoke();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Forces the system to attempt a flare-break on the current lock.
        /// Called by WeaponSystem when the target deploys countermeasures.
        /// </summary>
        public void TryFlareBreak()
        {
            if (!HasLock) return;
            if (UnityEngine.Random.value < flareLockBreakChance)
                BreakLock();
        }

        /// <summary>Immediately breaks any current lock (e.g. target destroyed).</summary>
        public void ForceBreakLock()
        {
            BreakLock();
        }

        /// <summary>
        /// Manually cycles to the next valid target (player-initiated target-cycle button).
        /// </summary>
        public void CycleTarget()
        {
            if (_candidates.Count == 0) return;

            int idx = CurrentTarget != null ? _candidates.IndexOf(CurrentTarget) : -1;
            idx = (idx + 1) % _candidates.Count;

            BreakLock();
            CurrentTarget = _candidates[idx];
            _lockTimer    = 0f;
        }
    }
}
