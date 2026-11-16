using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Skyrift
{
    /// <summary>
    /// Top-down radar display rendered on a UI RawImage or custom canvas.
    /// Detects objects by tag/layer, plots them as colored blips relative to
    /// the player aircraft, and rotates the radar with the aircraft heading.
    ///
    /// Blips are spawned from a pool of Image objects parented to the radar panel.
    /// </summary>
    public class RadarSystem : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Radar Panel")]
        [SerializeField] private RectTransform radarPanel;
        [SerializeField] private float         radarRadius     = 80f;   // UI pixels
        [SerializeField] private float         worldRange      = 3000f; // metres

        [Header("Blip Prefabs")]
        [SerializeField] private Image enemyBlipPrefab;
        [SerializeField] private Image allyBlipPrefab;
        [SerializeField] private Image objectiveBlipPrefab;

        [Header("Tags")]
        [SerializeField] private string enemyTag     = "Enemy";
        [SerializeField] private string allyTag      = "Ally";
        [SerializeField] private string objectiveTag = "Objective";

        [Header("Colors")]
        [SerializeField] private Color enemyColor     = Color.red;
        [SerializeField] private Color allyColor      = Color.cyan;
        [SerializeField] private Color objectiveColor = Color.yellow;

        [Header("Refresh")]
        [SerializeField] private float refreshInterval = 0.15f;   // seconds between scans

        // ─── Private ──────────────────────────────────────────────────────────
        private Transform   _playerTransform;
        private float       _refreshTimer;

        private readonly List<(Transform target, Image blip)> _activeBlips
            = new List<(Transform, Image)>(32);
        private readonly Queue<Image> _enemyPool   = new Queue<Image>();
        private readonly Queue<Image> _allyPool    = new Queue<Image>();
        private readonly Queue<Image> _objPool     = new Queue<Image>();

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            PrewarmPool(_enemyPool,   enemyBlipPrefab,     10);
            PrewarmPool(_allyPool,    allyBlipPrefab,       6);
            PrewarmPool(_objPool,     objectiveBlipPrefab,  4);
        }

        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= refreshInterval)
            {
                _refreshTimer = 0f;
                ScanContacts();
            }

            UpdateBlipPositions();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Assigns the player aircraft transform the radar is centred on.</summary>
        public void SetPlayerTransform(Transform player)
        {
            _playerTransform = player;
        }

        // ─── Private methods ──────────────────────────────────────────────────
        private void ScanContacts()
        {
            // Return all blips to pools
            foreach (var (_, blip) in _activeBlips)
                ReturnBlip(blip);
            _activeBlips.Clear();

            if (_playerTransform == null) return;

            // Scan by tag — in production prefer Physics.OverlapSphere with layers
            AddTaggedObjects(enemyTag,     _enemyPool,   enemyColor);
            AddTaggedObjects(allyTag,      _allyPool,    allyColor);
            AddTaggedObjects(objectiveTag, _objPool,     objectiveColor);
        }

        private void AddTaggedObjects(string tag, Queue<Image> pool, Color color)
        {
            GameObject[] objs = GameObject.FindGameObjectsWithTag(tag);
            foreach (var go in objs)
            {
                float dist = Vector3.Distance(go.transform.position, _playerTransform.position);
                if (dist > worldRange) continue;

                Image blip = GetBlip(pool, color);
                if (blip == null) continue;
                blip.gameObject.SetActive(true);
                _activeBlips.Add((go.transform, blip));
            }
        }

        private void UpdateBlipPositions()
        {
            if (_playerTransform == null || radarPanel == null) return;

            float heading = _playerTransform.eulerAngles.y;

            for (int i = _activeBlips.Count - 1; i >= 0; i--)
            {
                var (target, blip) = _activeBlips[i];
                if (target == null)
                {
                    ReturnBlip(blip);
                    _activeBlips.RemoveAt(i);
                    continue;
                }

                // World offset from player
                Vector3 offset = target.position - _playerTransform.position;

                // Project onto XZ plane, rotate by -heading so forward is up
                float worldX =  offset.x;
                float worldZ =  offset.z;

                float rad   = heading * Mathf.Deg2Rad;
                float cosH  = Mathf.Cos(-rad);
                float sinH  = Mathf.Sin(-rad);

                float localX = worldX * cosH - worldZ * sinH;
                float localZ = worldX * sinH + worldZ * cosH;

                // Normalise to radar radius
                float normX = localX / worldRange * radarRadius;
                float normY = localZ / worldRange * radarRadius;

                // Clamp to circle
                Vector2 pos2D = Vector2.ClampMagnitude(new Vector2(normX, normY), radarRadius);

                blip.rectTransform.anchoredPosition = pos2D;
            }
        }

        // ─── Pool helpers ─────────────────────────────────────────────────────
        private void PrewarmPool(Queue<Image> pool, Image prefab, int count)
        {
            if (prefab == null) return;
            for (int i = 0; i < count; i++)
            {
                Image img = Instantiate(prefab, radarPanel);
                img.gameObject.SetActive(false);
                pool.Enqueue(img);
            }
        }

        private Image GetBlip(Queue<Image> pool, Color color)
        {
            if (pool.Count == 0) return null;
            Image blip  = pool.Dequeue();
            blip.color  = color;
            return blip;
        }

        private void ReturnBlip(Image blip)
        {
            if (blip == null) return;
            blip.gameObject.SetActive(false);

            // Return to correct pool by color heuristic
            if (blip.color == enemyColor     && enemyBlipPrefab     != null) _enemyPool.Enqueue(blip);
            else if (blip.color == allyColor && allyBlipPrefab      != null) _allyPool.Enqueue(blip);
            else                                                              _objPool.Enqueue(blip);
        }
    }
}
