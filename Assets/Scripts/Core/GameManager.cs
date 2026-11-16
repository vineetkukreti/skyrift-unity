using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Skyrift
{
    /// <summary>
    /// Central singleton that owns game-state transitions, difficulty, and
    /// top-level save/load.  All other systems query or subscribe to this class
    /// rather than managing state themselves.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ─── Inspector fields ─────────────────────────────────────────────────
        [Header("Scene Names")]
        [SerializeField] private string mainMenuScene    = "MainMenu";
        [SerializeField] private string missionScene     = "Mission_01";
        [SerializeField] private string loadingScene     = "Loading";

        [Header("Difficulty")]
        [SerializeField] private DifficultyLevel startingDifficulty = DifficultyLevel.Pilot;

        [Header("Score")]
        [SerializeField] private int scorePerKill      = 1000;
        [SerializeField] private int scorePerObjective = 5000;

        // ─── Runtime state ────────────────────────────────────────────────────
        public GameState      CurrentState      { get; private set; } = GameState.MainMenu;
        public DifficultyLevel CurrentDifficulty { get; private set; }
        public int             CurrentScore      { get; private set; }
        public int             HighScore         { get; private set; }
        public float           MissionTimer      { get; private set; }
        public bool            IsPaused          => CurrentState == GameState.Paused;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired whenever the game transitions between states.</summary>
        public event Action<GameStateContext> OnGameStateChanged;

        /// <summary>Fired each time the player's score changes.</summary>
        public event Action<int> OnScoreChanged;

        // ─── Private ──────────────────────────────────────────────────────────
        private GameState _stateBeforePause;
        private bool      _missionTimerRunning;
        private const string HighScoreKey = "SKYRIFT_HIGHSCORE";

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            CurrentDifficulty = startingDifficulty;
            HighScore         = PlayerPrefs.GetInt(HighScoreKey, 0);
        }

        private void Update()
        {
            if (_missionTimerRunning && CurrentState == GameState.Flying)
            {
                MissionTimer += Time.deltaTime;
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Transitions to a new game state, broadcasting the context event.</summary>
        public void SetGameState(GameState newState)
        {
            if (newState == CurrentState) return;

            var ctx = new GameStateContext(CurrentState, newState, CurrentDifficulty);
            CurrentState = newState;

            switch (newState)
            {
                case GameState.Flying:
                    Time.timeScale       = 1f;
                    _missionTimerRunning = true;
                    break;

                case GameState.Paused:
                    _stateBeforePause    = ctx.PreviousState;
                    Time.timeScale       = 0f;
                    _missionTimerRunning = false;
                    break;

                case GameState.GameOver:
                case GameState.Victory:
                    Time.timeScale       = 1f;
                    _missionTimerRunning = false;
                    SaveHighScore();
                    break;

                case GameState.MainMenu:
                    Time.timeScale       = 1f;
                    _missionTimerRunning = false;
                    CurrentScore         = 0;
                    MissionTimer         = 0f;
                    break;
            }

            OnGameStateChanged?.Invoke(ctx);
        }

        /// <summary>Resumes from pause back to whatever state was active before.</summary>
        public void ResumeFromPause()
        {
            if (CurrentState != GameState.Paused) return;
            SetGameState(_stateBeforePause);
        }

        /// <summary>Toggles pause on/off during flight.</summary>
        public void TogglePause()
        {
            if (CurrentState == GameState.Flying)
                SetGameState(GameState.Paused);
            else if (CurrentState == GameState.Paused)
                ResumeFromPause();
        }

        /// <summary>Adds points to the current score and fires OnScoreChanged.</summary>
        public void AddKillScore(int multiplier = 1)
        {
            int points = scorePerKill * multiplier * DifficultyMultiplier();
            AddScore(points);
        }

        /// <summary>Adds objective completion score.</summary>
        public void AddObjectiveScore()
        {
            AddScore(scorePerObjective * DifficultyMultiplier());
        }

        /// <summary>Sets difficulty at runtime (e.g. from Settings menu).</summary>
        public void SetDifficulty(DifficultyLevel level)
        {
            CurrentDifficulty = level;
        }

        /// <summary>Loads a specific mission scene by name, going through the loading screen.</summary>
        public void LoadMission(string sceneName)
        {
            missionScene = sceneName;
            SetGameState(GameState.Loading);
            StartCoroutine(LoadSceneAsync(sceneName));
        }

        /// <summary>Loads the main menu scene.</summary>
        public void GoToMainMenu()
        {
            SetGameState(GameState.MainMenu);
            StartCoroutine(LoadSceneAsync(mainMenuScene));
        }

        /// <summary>Restarts the currently loaded mission scene.</summary>
        public void RestartMission()
        {
            CurrentScore = 0;
            MissionTimer = 0f;
            SetGameState(GameState.Loading);
            StartCoroutine(LoadSceneAsync(SceneManager.GetActiveScene().name));
        }

        // ─── Save / Load ──────────────────────────────────────────────────────

        /// <summary>Persists the high score to PlayerPrefs.</summary>
        public void SaveHighScore()
        {
            if (CurrentScore > HighScore)
            {
                HighScore = CurrentScore;
                PlayerPrefs.SetInt(HighScoreKey, HighScore);
                PlayerPrefs.Save();
            }
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void AddScore(int points)
        {
            CurrentScore += points;
            OnScoreChanged?.Invoke(CurrentScore);
        }

        private int DifficultyMultiplier()
        {
            return CurrentDifficulty switch
            {
                DifficultyLevel.Rookie => 1,
                DifficultyLevel.Pilot  => 2,
                DifficultyLevel.Ace    => 3,
                DifficultyLevel.Legend => 4,
                _                      => 1
            };
        }

        private IEnumerator LoadSceneAsync(string sceneName)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
                yield return null;

            // Brief artificial pause so the loading screen is visible.
            yield return new WaitForSecondsRealtime(0.5f);

            op.allowSceneActivation = true;

            yield return new WaitUntil(() => op.isDone);

            if (sceneName == missionScene)
                SetGameState(GameState.MissionBriefing);
        }
    }
}
