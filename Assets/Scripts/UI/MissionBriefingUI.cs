using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Skyrift
{
    /// <summary>
    /// Displays the mission briefing screen before a mission starts.
    /// Reads data from the active <see cref="MissionData_SO"/> loaded by
    /// <see cref="MissionManager"/> and presents it in a typewriter animation.
    /// </summary>
    public class MissionBriefingUI : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Panel")]
        [SerializeField] private GameObject briefingPanel;

        [Header("Text")]
        [SerializeField] private TMP_Text missionNameText;
        [SerializeField] private TMP_Text missionCodeText;
        [SerializeField] private TMP_Text briefingBodyText;
        [SerializeField] private TMP_Text objectivesText;
        [SerializeField] private TMP_Text difficultyText;
        [SerializeField] private float    typewriterSpeed = 40f;   // chars/second

        [Header("Mission Image")]
        [SerializeField] private Image missionMapImage;

        [Header("Aircraft Selection")]
        [SerializeField] private TMP_Dropdown aircraftDropdown;

        [Header("Buttons")]
        [SerializeField] private Button startMissionButton;
        [SerializeField] private Button mainMenuButton;

        // ─── Private ──────────────────────────────────────────────────────────
        private MissionData_SO _missionData;
        private Coroutine      _typewriterRoutine;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (startMissionButton != null)
                startMissionButton.onClick.AddListener(OnStartMission);
            if (mainMenuButton != null)
                mainMenuButton.onClick.AddListener(OnMainMenu);

            if (briefingPanel != null)
                briefingPanel.SetActive(false);
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

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Populates and shows the briefing for the given mission data.</summary>
        public void ShowBriefing(MissionData_SO data)
        {
            _missionData = data;
            briefingPanel?.SetActive(true);

            if (missionNameText != null) missionNameText.text = data.missionName;
            if (missionCodeText != null) missionCodeText.text = $"MISSION {data.missionID:D3}";
            if (missionMapImage != null && data.missionMapSprite != null)
                missionMapImage.sprite = data.missionMapSprite;
            if (difficultyText  != null)
                difficultyText.text = GameManager.Instance?.CurrentDifficulty.ToString() ?? "";

            // Build objectives string
            string objStr = "";
            if (data.objectives != null)
            {
                for (int i = 0; i < data.objectives.Length; i++)
                    objStr += $"{i + 1}. {data.objectives[i]}\n";
            }
            if (objectivesText != null) objectivesText.text = objStr;

            // Typewriter for briefing body
            if (_typewriterRoutine != null) StopCoroutine(_typewriterRoutine);
            _typewriterRoutine = StartCoroutine(TypewriterRoutine(data.briefingText));

            startMissionButton?.gameObject.SetActive(false);  // hide until typewriter done
        }

        // ─── Private ─────────────────────────────────────────────────────────
        private IEnumerator TypewriterRoutine(string fullText)
        {
            if (briefingBodyText == null) yield break;

            briefingBodyText.text = "";
            float accum = 0f;
            int   charIndex = 0;
            float delay     = 1f / typewriterSpeed;

            while (charIndex < fullText.Length)
            {
                accum += Time.deltaTime;
                while (accum >= delay && charIndex < fullText.Length)
                {
                    briefingBodyText.text += fullText[charIndex];
                    charIndex++;
                    accum -= delay;
                }
                yield return null;
            }

            startMissionButton?.gameObject.SetActive(true);
        }

        private void OnStartMission()
        {
            briefingPanel?.SetActive(false);
            GameManager.Instance?.SetGameState(GameState.Flying);
            MissionManager.Instance?.BeginMission();
        }

        private void OnMainMenu()
        {
            GameManager.Instance?.GoToMainMenu();
        }

        private void OnStateChanged(GameStateContext ctx)
        {
            if (ctx.NewState == GameState.MissionBriefing)
            {
                var mm = MissionManager.Instance;
                if (mm != null && mm.CurrentMission != null)
                    ShowBriefing(mm.CurrentMission);
            }
            else
            {
                briefingPanel?.SetActive(false);
            }
        }
    }
}
