using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

namespace Skyrift
{
    /// <summary>
    /// Pause menu shown when the player presses Escape/Start.
    /// Provides: Resume, Restart, Settings, Main Menu.
    /// Settings panel exposes volume sliders and difficulty.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Panels")]
        [SerializeField] private GameObject pauseRootPanel;
        [SerializeField] private GameObject settingsPanel;

        [Header("Main Pause Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button quitButton;

        [Header("Settings")]
        [SerializeField] private Slider     masterVolumeSlider;
        [SerializeField] private Slider     musicVolumeSlider;
        [SerializeField] private Slider     sfxVolumeSlider;
        [SerializeField] private TMP_Dropdown difficultyDropdown;
        [SerializeField] private Button     settingsBackButton;

        [Header("Info Labels")]
        [SerializeField] private TMP_Text   scoreLabel;
        [SerializeField] private TMP_Text   timeLabel;
        [SerializeField] private TMP_Text   versionLabel;

        // ─── Private ──────────────────────────────────────────────────────────
        private bool _settingsOpen;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            pauseRootPanel?.SetActive(false);
            settingsPanel?.SetActive(false);

            if (resumeButton   != null) resumeButton.onClick.AddListener(OnResume);
            if (restartButton  != null) restartButton.onClick.AddListener(OnRestart);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnMainMenu);
            if (quitButton     != null) quitButton.onClick.AddListener(OnQuit);
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(OnSettingsBack);

            if (masterVolumeSlider != null)
                masterVolumeSlider.onValueChanged.AddListener(v =>
                    AudioManager.Instance?.SetMasterVolume(v));

            if (musicVolumeSlider != null)
                musicVolumeSlider.onValueChanged.AddListener(v =>
                    AudioManager.Instance?.SetMusicVolume(v));

            if (sfxVolumeSlider != null)
                sfxVolumeSlider.onValueChanged.AddListener(v =>
                    AudioManager.Instance?.SetSFXVolume(v));

            if (difficultyDropdown != null)
                difficultyDropdown.onValueChanged.AddListener(v =>
                    GameManager.Instance?.SetDifficulty((DifficultyLevel)v));

            if (versionLabel != null)
                versionLabel.text = $"v{Application.version}";
        }

        private void Update()
        {
            // Toggle pause via keyboard or gamepad
            bool pausePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool gpPause      = Gamepad.current  != null && Gamepad.current.startButton.wasPressedThisFrame;

            if (pausePressed || gpPause)
                TogglePause();
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged -= OnStateChanged;
        }

        // ─── Button handlers ─────────────────────────────────────────────────
        private void TogglePause()
        {
            GameManager.Instance?.TogglePause();
        }

        private void OnResume()
        {
            GameManager.Instance?.ResumeFromPause();
        }

        private void OnRestart()
        {
            GameManager.Instance?.RestartMission();
        }

        private void OnSettings()
        {
            _settingsOpen = true;
            settingsPanel?.SetActive(true);

            // Load current values into sliders
            if (AudioManager.Instance != null)
            {
                if (masterVolumeSlider != null) masterVolumeSlider.value = AudioManager.Instance.MasterVolume;
                if (musicVolumeSlider  != null) musicVolumeSlider.value  = AudioManager.Instance.MusicVolume;
                if (sfxVolumeSlider    != null) sfxVolumeSlider.value    = AudioManager.Instance.SFXVolume;
            }
            if (difficultyDropdown != null && GameManager.Instance != null)
                difficultyDropdown.value = (int)GameManager.Instance.CurrentDifficulty;
        }

        private void OnSettingsBack()
        {
            _settingsOpen = false;
            settingsPanel?.SetActive(false);
        }

        private void OnMainMenu()
        {
            GameManager.Instance?.GoToMainMenu();
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─── State listener ───────────────────────────────────────────────────
        private void OnStateChanged(GameStateContext ctx)
        {
            bool show = ctx.NewState == GameState.Paused;
            pauseRootPanel?.SetActive(show);

            if (show && GameManager.Instance != null)
            {
                if (scoreLabel != null) scoreLabel.text = $"SCORE  {GameManager.Instance.CurrentScore:N0}";
                float t   = GameManager.Instance.MissionTimer;
                int   min = (int)(t / 60f);
                int   sec = (int)(t % 60f);
                if (timeLabel != null) timeLabel.text = $"TIME  {min:D2}:{sec:D2}";
            }

            if (!show)
            {
                settingsPanel?.SetActive(false);
                _settingsOpen = false;
            }
        }
    }
}
