using System;
using UnityEngine;

namespace Skyrift
{
    // ─── Mission type enum ────────────────────────────────────────────────────
    public enum MissionType
    {
        Elimination,   // destroy all enemies
        Escort,        // protect a target to a destination
        Defence,       // hold position for a time limit
        Intercept,     // destroy enemies before they reach a target
        TimeTrial      // complete objectives within time limit
    }

    /// <summary>
    /// ScriptableObject that fully describes a single SKYRIFT mission.
    /// Create instances via Assets > Create > Skyrift > Mission Data.
    ///
    /// MissionManager reads this asset at runtime to drive spawning,
    /// objectives, success/failure logic, and the briefing UI.
    /// </summary>
    [CreateAssetMenu(fileName = "MissionData_New",
                     menuName  = "Skyrift/Mission Data",
                     order     = 2)]
    public class MissionData_SO : ScriptableObject
    {
        // ─── Identity ─────────────────────────────────────────────────────────
        [Header("Identity")]
        public int    missionID     = 1;
        public string missionName   = "Operation Gridlock";
        public string missionCode   = "SKYRIFT-001";

        [Header("Briefing")]
        [TextArea(4, 12)]
        public string briefingText  = "Intercept enemy formation approaching the northern grid.";

        [Tooltip("2D overhead map shown in the briefing screen.")]
        public Sprite missionMapSprite;

        // ─── Type & limits ────────────────────────────────────────────────────
        [Header("Mission Parameters")]
        public MissionType missionType       = MissionType.Elimination;

        [Tooltip("Time limit in seconds (0 = no limit).")]
        public float       timeLimitSeconds  = 0f;

        [Tooltip("Minimum score required for S-rank.")]
        public int         sRankThreshold    = 50000;

        // ─── Objectives ───────────────────────────────────────────────────────
        [Header("Objectives")]
        public string[] objectives;   // Human-readable summary for briefing text

        [Tooltip("Structured objective definitions consumed by ObjectiveTracker.")]
        public ObjectiveDefinition[] objectiveDefinitions;

        // ─── Waves ────────────────────────────────────────────────────────────
        [Header("Enemy Waves")]
        public WaveDefinition[] waves;

        // ─── Rewards ─────────────────────────────────────────────────────────
        [Header("Rewards")]
        public int    baseScoreReward = 5000;
        public string unlocksAfter    = "";    // aircraft/mission name unlocked on completion

        // ─── Nested types ─────────────────────────────────────────────────────

        /// <summary>Defines a single trackable objective within the mission.</summary>
        [Serializable]
        public class ObjectiveDefinition
        {
            [Tooltip("Label displayed in the HUD and briefing.")]
            public string        label         = "Destroy enemy aircraft";

            public ObjectiveType type          = ObjectiveType.DestroyAll;

            [Tooltip("How many enemies/events to satisfy this objective.")]
            public int           requiredCount = 0;

            [Tooltip("If true, failing this objective fails the whole mission.")]
            public bool          isPrimary     = true;
        }

        /// <summary>Defines a group of enemies that spawn together at a given delay.</summary>
        [Serializable]
        public class WaveDefinition
        {
            [Tooltip("Seconds after mission start (or previous wave clear) before this wave spawns.")]
            public float         spawnDelay = 0f;

            [Tooltip("If true, the next wave waits until this wave is fully cleared.")]
            public bool          waitForClear = false;

            public EnemyEntry[]  enemies;
        }

        /// <summary>A single enemy aircraft entry within a wave.</summary>
        [Serializable]
        public class EnemyEntry
        {
            [Tooltip("Enemy aircraft prefab to instantiate.")]
            public GameObject     prefab;

            [Tooltip("Optional stats override (null = use prefab's own AircraftStats_SO).")]
            public AircraftStats_SO statsOverride;

            [Tooltip("Difficulty modifier for this specific enemy (multiplies damage dealt to player).")]
            [Range(0.5f, 3f)]
            public float          difficultyScale = 1f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (timeLimitSeconds < 0f) timeLimitSeconds = 0f;
            if (sRankThreshold   < 0)  sRankThreshold   = 0;
        }
#endif
    }
}
