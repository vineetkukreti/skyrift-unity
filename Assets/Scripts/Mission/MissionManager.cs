using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Drives the active mission from a <see cref="MissionData_SO"/> asset.
    /// Spawns wave groups, tracks objective completion, evaluates win/fail
    /// conditions, and reports results to <see cref="GameManager"/>.
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────
        public static MissionManager Instance { get; private set; }

        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Mission Data")]
        [SerializeField] private MissionData_SO missionData;

        [Header("Player Spawn")]
        [SerializeField] private Transform playerSpawnPoint;
        [SerializeField] private GameObject playerAircraftPrefab;

        [Header("References")]
        [SerializeField] private ObjectiveTracker objectiveTracker;
        [SerializeField] private WaveSpawner      waveSpawner;
        [SerializeField] private HUDController    hudController;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired when all objectives are complete.</summary>
        public event Action             OnMissionSuccess;

        /// <summary>Fired when the mission is failed.</summary>
        public event Action<string>     OnMissionFailed;

        /// <summary>Fired when the next wave is triggered.  Carries wave index.</summary>
        public event Action<int>        OnWaveStarted;

        // ─── Runtime state ────────────────────────────────────────────────────
        public MissionData_SO CurrentMission   { get; private set; }
        public bool            MissionStarted  { get; private set; }
        public bool            MissionComplete { get; private set; }
        public int             EnemiesKilled   { get; private set; }
        public int             CurrentWave     { get; private set; }
        public GameObject      PlayerAircraft  { get; private set; }

        // ─── Private ──────────────────────────────────────────────────────────
        private float _missionTimer;
        private readonly List<GameObject> _spawnedEnemies = new List<GameObject>(32);
        private Coroutine _waveRoutine;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            CurrentMission = missionData;
        }

        private void Start()
        {
            // Subscribe to damage/destroy on existing enemies
            SubscribeToEnemies();
        }

        private void Update()
        {
            if (!MissionStarted || MissionComplete) return;

            _missionTimer += Time.deltaTime;

            // Time-trial check
            if (CurrentMission != null
                && CurrentMission.missionType == MissionType.TimeTrial
                && _missionTimer > CurrentMission.timeLimitSeconds)
            {
                FailMission("Time expired.");
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Kicks off the mission after the briefing.  Spawns player, begins
        /// wave timer, and starts the objective tracker.
        /// </summary>
        public void BeginMission()
        {
            if (MissionStarted) return;
            MissionStarted  = true;
            EnemiesKilled   = 0;
            CurrentWave     = 0;
            _missionTimer   = 0f;

            SpawnPlayer();

            if (waveSpawner != null && CurrentMission != null)
                _waveRoutine = StartCoroutine(RunWaves());

            objectiveTracker?.InitialiseObjectives(CurrentMission);
            if (objectiveTracker != null)
                objectiveTracker.OnAllObjectivesComplete += OnAllObjectivesComplete;
        }

        /// <summary>Called when any tracked enemy is destroyed.</summary>
        public void RegisterKill(DamageSystem victim)
        {
            EnemiesKilled++;
            _spawnedEnemies.Remove(victim.gameObject);

            objectiveTracker?.NotifyEnemyKilled(victim);

            // Threat-level heuristic: fewer enemies alive → less intense music
            float threat = Mathf.Clamp01(_spawnedEnemies.Count / 6f);
            AudioManager.Instance?.SetThreatLevel(threat);
        }

        /// <summary>Immediately ends the mission as a failure.</summary>
        public void FailMission(string reason)
        {
            if (MissionComplete) return;
            MissionComplete = true;

            if (_waveRoutine != null) StopCoroutine(_waveRoutine);

            GameManager.Instance?.SetGameState(GameState.GameOver);
            OnMissionFailed?.Invoke(reason);
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void SpawnPlayer()
        {
            if (playerAircraftPrefab == null || playerSpawnPoint == null) return;

            PlayerAircraft = Instantiate(
                playerAircraftPrefab,
                playerSpawnPoint.position,
                playerSpawnPoint.rotation
            );

            hudController?.BindToAircraft(PlayerAircraft);

            // Wire up player death
            var dmg = PlayerAircraft.GetComponent<DamageSystem>();
            if (dmg != null)
                dmg.OnDestroyed += _ => FailMission("Aircraft destroyed.");
        }

        private IEnumerator RunWaves()
        {
            if (CurrentMission == null || CurrentMission.waves == null) yield break;

            foreach (var wave in CurrentMission.waves)
            {
                yield return new WaitForSeconds(wave.spawnDelay);

                if (MissionComplete) yield break;

                CurrentWave++;
                OnWaveStarted?.Invoke(CurrentWave);

                List<GameObject> spawned = waveSpawner.SpawnWave(wave);
                foreach (var go in spawned)
                {
                    _spawnedEnemies.Add(go);
                    var dmg = go.GetComponent<DamageSystem>();
                    if (dmg != null)
                        dmg.OnDestroyed += RegisterKill;
                }

                AudioManager.Instance?.SetThreatLevel(
                    Mathf.Clamp01(spawned.Count / 6f));

                // Wait until wave is cleared before spawning next
                if (wave.waitForClear)
                {
                    yield return new WaitUntil(() =>
                        !_spawnedEnemies.Exists(go => go != null));
                }
            }
        }

        private void SubscribeToEnemies()
        {
            foreach (var go in _spawnedEnemies)
            {
                var dmg = go?.GetComponent<DamageSystem>();
                if (dmg != null) dmg.OnDestroyed += RegisterKill;
            }
        }

        private void OnAllObjectivesComplete()
        {
            if (MissionComplete) return;
            MissionComplete = true;

            GameManager.Instance?.AddObjectiveScore();
            GameManager.Instance?.SetGameState(GameState.Victory);
            OnMissionSuccess?.Invoke();
        }
    }
}
