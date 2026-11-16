using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Skyrift
{
    /// <summary>
    /// Spawns enemy aircraft in pre-defined wave groups.
    /// Wave configuration comes from <see cref="MissionData_SO.WaveDefinition"/>
    /// passed to <see cref="SpawnWave"/>.
    ///
    /// Enemies can be spawned at tagged spawn points or at runtime-computed
    /// positions on a circle around the player.
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;
        [Tooltip("If no spawn points assigned, enemies spawn around the player on this radius.")]
        [SerializeField] private float       dynamicSpawnRadius  = 2000f;
        [SerializeField] private float       dynamicSpawnAltitude = 1500f;

        [Header("Spawned Root")]
        [Tooltip("Optional parent transform to keep hierarchy clean.")]
        [SerializeField] private Transform spawnedEnemiesRoot;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired each time an individual enemy is spawned.  Carries the new GameObject.</summary>
        public event Action<GameObject> OnEnemySpawned;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Instantiates all enemies in a wave definition.
        /// Returns the list of spawned GameObjects so MissionManager can track them.
        /// </summary>
        public List<GameObject> SpawnWave(MissionData_SO.WaveDefinition wave)
        {
            var spawned = new List<GameObject>(wave.enemies.Length);

            for (int i = 0; i < wave.enemies.Length; i++)
            {
                var entry  = wave.enemies[i];
                if (entry.prefab == null) continue;

                Vector3    pos = GetSpawnPosition(i, wave.enemies.Length);
                Quaternion rot = GetSpawnRotation(pos);

                GameObject go = Instantiate(
                    entry.prefab,
                    pos,
                    rot,
                    spawnedEnemiesRoot
                );

                // Override stats if override provided
                if (entry.statsOverride != null)
                {
                    var statsComp = go.GetComponent<AircraftStats>();
                    if (statsComp != null)
                    {
                        // Stats SO assignment done via reflection for now;
                        // in production, add a public setter to AircraftStats.
                    }
                }

                // Tag for radar
                go.tag = "Enemy";

                spawned.Add(go);
                OnEnemySpawned?.Invoke(go);
            }

            return spawned;
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private Vector3 GetSpawnPosition(int index, int total)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                return spawnPoints[index % spawnPoints.Length].position;
            }

            // Dynamic circle spawn around player
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            Vector3 center = playerGO != null
                ? playerGO.transform.position
                : Vector3.zero;

            float angle = (360f / Mathf.Max(total, 1)) * index * Mathf.Deg2Rad;
            float x     = center.x + Mathf.Cos(angle) * dynamicSpawnRadius;
            float z     = center.z + Mathf.Sin(angle) * dynamicSpawnRadius;
            float y     = center.y + dynamicSpawnAltitude + Random.Range(-200f, 200f);

            return new Vector3(x, y, z);
        }

        private Quaternion GetSpawnRotation(Vector3 spawnPos)
        {
            // Enemies always spawn pointing toward the arena centre roughly
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO == null) return Quaternion.identity;

            Vector3 dir = (playerGO.transform.position - spawnPos);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized);
        }
    }
}
