using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Skyrift
{
    /// <summary>
    /// Handles async scene loading with an optional progress-bar UI.
    /// Attach to a persistent "Loading Screen" canvas in the Loading scene.
    /// GameManager delegates async loads here via the static API.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────
        public static SceneLoader Instance { get; private set; }

        // ─── Inspector fields ─────────────────────────────────────────────────
        [Header("UI References")]
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private Slider     progressBar;
        [SerializeField] private Text       progressLabel;
        [SerializeField] private Text       loadingTipText;

        [Header("Loading Tips")]
        [SerializeField] private string[] loadingTips = new[]
        {
            "Use afterburner sparingly — it drains fuel fast.",
            "Deploy flares when you hear the missile lock tone.",
            "High altitude favors missile range. Low altitude favors escape.",
            "Wingmen follow formation orders via the command menu.",
            "Roll inverted to tighten your turn radius in a dogfight."
        };

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired when a scene load completes. Passes the loaded scene name.</summary>
        public event Action<string> OnSceneLoaded;

        // ─── Private ──────────────────────────────────────────────────────────
        private AsyncOperation _pendingOp;
        private bool           _isLoading;

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

            if (loadingPanel != null)
                loadingPanel.SetActive(false);
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Begins an async load of <paramref name="sceneName"/>.
        /// Shows the loading panel and streams progress to the UI.
        /// </summary>
        public void LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (_isLoading) return;
            StartCoroutine(LoadRoutine(sceneName, mode));
        }

        /// <summary>
        /// Preloads a scene in the background without activating it.
        /// Call <see cref="ActivatePreloaded"/> when ready to switch.
        /// </summary>
        public void PreloadScene(string sceneName)
        {
            if (_isLoading) return;
            StartCoroutine(PreloadRoutine(sceneName));
        }

        /// <summary>Activates a previously preloaded scene.</summary>
        public void ActivatePreloaded()
        {
            if (_pendingOp == null) return;
            _pendingOp.allowSceneActivation = true;
        }

        // ─── Coroutines ───────────────────────────────────────────────────────
        private IEnumerator LoadRoutine(string sceneName, LoadSceneMode mode)
        {
            _isLoading = true;
            ShowLoadingPanel(true);
            SetRandomTip();

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, mode);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                float progress = Mathf.Clamp01(op.progress / 0.9f);
                UpdateProgress(progress);
                yield return null;
            }

            UpdateProgress(1f);
            yield return new WaitForSecondsRealtime(0.3f);

            op.allowSceneActivation = true;
            yield return new WaitUntil(() => op.isDone);

            ShowLoadingPanel(false);
            _isLoading = false;
            OnSceneLoaded?.Invoke(sceneName);
        }

        private IEnumerator PreloadRoutine(string sceneName)
        {
            _isLoading = true;

            _pendingOp = SceneManager.LoadSceneAsync(sceneName);
            _pendingOp.allowSceneActivation = false;

            while (_pendingOp.progress < 0.9f)
                yield return null;

            _isLoading = false;
            // Scene is ready; caller decides when to activate via ActivatePreloaded().
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void ShowLoadingPanel(bool show)
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(show);
        }

        private void UpdateProgress(float t)
        {
            if (progressBar  != null) progressBar.value = t;
            if (progressLabel != null) progressLabel.text = $"{Mathf.RoundToInt(t * 100f)}%";
        }

        private void SetRandomTip()
        {
            if (loadingTipText == null || loadingTips == null || loadingTips.Length == 0) return;
            loadingTipText.text = loadingTips[UnityEngine.Random.Range(0, loadingTips.Length)];
        }
    }
}
