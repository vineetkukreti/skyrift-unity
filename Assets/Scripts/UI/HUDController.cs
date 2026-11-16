using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Skyrift
{
    /// <summary>
    /// Drives all flight HUD elements: airspeed, altitude, heading tape,
    /// throttle indicator, health bar, ammo counters, lock warning,
    /// stall warning, and targeting reticle.
    ///
    /// Expects a Canvas with the HUD layer at the root.  All Text/Image
    /// references must be assigned in the inspector.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        // ─── Speed / Altitude / Heading ───────────────────────────────────────
        [Header("Speed / Alt / Heading")]
        [SerializeField] private TMP_Text speedText;
        [SerializeField] private TMP_Text altitudeText;
        [SerializeField] private TMP_Text headingText;
        [SerializeField] private RectTransform headingTape;       // scrolling tape
        [SerializeField] private float          headingTapeScale  = 4f;

        // ─── Throttle indicator ───────────────────────────────────────────────
        [Header("Throttle")]
        [SerializeField] private Image throttleBarFill;
        [SerializeField] private Image afterburnerGlow;
        [SerializeField] private Color afterburnerColor = new Color(1f, 0.5f, 0f);

        // ─── Health ───────────────────────────────────────────────────────────
        [Header("Health")]
        [SerializeField] private Image    healthBarFill;
        [SerializeField] private TMP_Text healthText;
        [SerializeField] private Color    healthFullColor   = Color.green;
        [SerializeField] private Color    healthLowColor    = Color.red;

        // ─── Weapons ──────────────────────────────────────────────────────────
        [Header("Weapons")]
        [SerializeField] private TMP_Text gunAmmoText;
        [SerializeField] private TMP_Text missileCountText;
        [SerializeField] private TMP_Text flareCountText;

        // ─── Lock-on ──────────────────────────────────────────────────────────
        [Header("Lock-On")]
        [SerializeField] private GameObject lockWarningPanel;
        [SerializeField] private Image      lockProgressRing;     // Image.fillAmount
        [SerializeField] private TMP_Text   lockWarningText;
        [SerializeField] private Color      lockingColor  = Color.yellow;
        [SerializeField] private Color      lockedColor   = Color.red;

        // ─── Targeting reticle ────────────────────────────────────────────────
        [Header("Targeting Reticle")]
        [SerializeField] private RectTransform reticle;
        [SerializeField] private float         reticleLeadScale = 0.3f;

        // ─── Warnings ─────────────────────────────────────────────────────────
        [Header("Warnings")]
        [SerializeField] private GameObject stallWarning;
        [SerializeField] private GameObject pullUpWarning;
        [SerializeField] private TMP_Text   scoreText;
        [SerializeField] private TMP_Text   missionTimerText;

        // ─── Minimap ──────────────────────────────────────────────────────────
        [Header("Minimap")]
        [SerializeField] private RectTransform playerBlip;
        [SerializeField] private RectTransform minimapRoot;
        [SerializeField] private float         minimapScale  = 0.02f;   // world units → minimap px

        // ─── References injected at runtime ──────────────────────────────────
        private FlightPhysics   _physics;
        private FlightController _controller;
        private AircraftStats   _stats;
        private DamageSystem    _damage;
        private WeaponSystem    _weapons;
        private LockOnSystem    _lockOn;
        private Camera          _mainCam;

        // ─── Private ──────────────────────────────────────────────────────────
        private float  _lockWarningBlink;
        private bool   _lockWarningVisible;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _mainCam = Camera.main;
        }

        private void Start()
        {
            // Subscribe to GameManager events
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnScoreChanged    += UpdateScore;
                GameManager.Instance.OnGameStateChanged += OnStateChanged;
            }

            if (lockWarningPanel != null)
                lockWarningPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnScoreChanged    -= UpdateScore;
                GameManager.Instance.OnGameStateChanged -= OnStateChanged;
            }

            UnbindSystems();
        }

        private void LateUpdate()
        {
            if (_physics == null) return;

            UpdateSpeedAltitude();
            UpdateHeading();
            UpdateThrottle();
            UpdateHealth();
            UpdateWeapons();
            UpdateLockOn();
            UpdateWarnings();
            UpdateReticle();
            UpdateMinimap();
            UpdateMissionTimer();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Binds the HUD to a specific aircraft's components.
        /// Call when the player aircraft is spawned or reassigned.
        /// </summary>
        public void BindToAircraft(GameObject aircraft)
        {
            UnbindSystems();

            _physics    = aircraft.GetComponent<FlightPhysics>();
            _controller = aircraft.GetComponent<FlightController>();
            _stats      = aircraft.GetComponent<AircraftStats>();
            _damage     = aircraft.GetComponent<DamageSystem>();
            _weapons    = aircraft.GetComponent<WeaponSystem>();
            _lockOn     = aircraft.GetComponent<LockOnSystem>();

            if (_lockOn != null)
            {
                _lockOn.OnLockProgress  += OnLockProgress;
                _lockOn.OnLockAcquired  += _ => OnLockAcquired();
                _lockOn.OnLockLost      += OnLockLost;
            }
        }

        // ─── HUD update methods ───────────────────────────────────────────────
        private void UpdateSpeedAltitude()
        {
            if (speedText    != null) speedText.text    = $"{_physics.SpeedKnots:F0}\nKTS";
            if (altitudeText != null) altitudeText.text = $"{_physics.AltitudeMeters:F0}\nFT";
        }

        private void UpdateHeading()
        {
            if (_physics == null) return;
            float yaw = (_physics.transform.eulerAngles.y + 360f) % 360f;

            if (headingText  != null) headingText.text = $"{yaw:F0}°";

            // Scroll heading tape: move by yaw degrees * scale
            if (headingTape != null)
            {
                Vector3 anchoredPos = headingTape.anchoredPosition;
                anchoredPos.x = -yaw * headingTapeScale;
                headingTape.anchoredPosition = anchoredPos;
            }
        }

        private void UpdateThrottle()
        {
            if (_controller == null) return;

            if (throttleBarFill != null)
                throttleBarFill.fillAmount = _controller.Throttle;

            if (afterburnerGlow != null)
                afterburnerGlow.enabled = _controller.AfterburnerActive;
        }

        private void UpdateHealth()
        {
            if (_stats == null) return;

            float hp = _stats.HealthNormalized;

            if (healthBarFill != null)
            {
                healthBarFill.fillAmount = hp;
                healthBarFill.color      = Color.Lerp(healthLowColor, healthFullColor, hp);
            }
            if (healthText != null)
                healthText.text = $"{Mathf.CeilToInt(_stats.CurrentHealth)}/{Mathf.CeilToInt(_stats.MaxHealth)}";
        }

        private void UpdateWeapons()
        {
            if (_weapons == null) return;
            if (gunAmmoText     != null) gunAmmoText.text      = _weapons.GunAmmoRemaining.ToString();
            if (missileCountText!= null) missileCountText.text = _weapons.MissilesRemaining.ToString();
            if (flareCountText  != null) flareCountText.text   = _weapons.FlaresRemaining.ToString();
        }

        private void UpdateLockOn()
        {
            if (_lockOn == null) return;

            bool active = _lockOn.LockProgress > 0f;
            if (lockWarningPanel != null)
                lockWarningPanel.SetActive(active);

            if (!active) return;

            if (lockProgressRing != null)
                lockProgressRing.fillAmount = _lockOn.LockProgress;

            if (_lockOn.HasLock)
            {
                // Blink locked indicator
                _lockWarningBlink += Time.deltaTime * 8f;
                bool show = Mathf.Sin(_lockWarningBlink) > 0f;
                if (lockWarningText != null)
                {
                    lockWarningText.text  = "LOCKED";
                    lockWarningText.color = lockedColor;
                    lockWarningText.gameObject.SetActive(show);
                }
                if (lockProgressRing != null)
                    lockProgressRing.color = lockedColor;
            }
            else
            {
                if (lockWarningText != null)
                {
                    lockWarningText.text  = "LOCKING";
                    lockWarningText.color = lockingColor;
                    lockWarningText.gameObject.SetActive(true);
                }
                if (lockProgressRing != null)
                    lockProgressRing.color = lockingColor;
            }
        }

        private void UpdateWarnings()
        {
            if (_physics == null) return;

            if (stallWarning != null)
                stallWarning.SetActive(_physics.IsStalled);

            // Pull-up warning: descending rapidly below 300m
            bool pullUp = _physics.AltitudeMeters < 300f
                       && _physics.LocalVelocity.y < -30f;
            if (pullUpWarning != null)
                pullUpWarning.SetActive(pullUp);
        }

        private void UpdateReticle()
        {
            if (reticle == null || _lockOn == null || _mainCam == null) return;
            if (_lockOn.CurrentTarget == null) { reticle.gameObject.SetActive(false); return; }

            reticle.gameObject.SetActive(true);

            // Project target position (with lead) to screen space
            Rigidbody targetRb = _lockOn.CurrentTarget.GetComponent<Rigidbody>();
            Vector3 leadPos    = _lockOn.CurrentTarget.position;
            if (targetRb != null && _physics != null)
            {
                float ttc = Vector3.Distance(transform.position, leadPos)
                          / Mathf.Max(_physics.SpeedMS, 1f);
                leadPos += targetRb.linearVelocity * ttc * reticleLeadScale;
            }

            Vector3 screenPos = _mainCam.WorldToScreenPoint(leadPos);
            if (screenPos.z > 0f)
                reticle.position = screenPos;
        }

        private void UpdateMinimap()
        {
            if (playerBlip == null || _physics == null) return;

            Vector3 worldPos = _physics.transform.position;
            Vector2 minimapPos = new Vector2(worldPos.x, worldPos.z) * minimapScale;
            playerBlip.anchoredPosition = minimapPos;
        }

        private void UpdateMissionTimer()
        {
            if (missionTimerText == null || GameManager.Instance == null) return;

            float t   = GameManager.Instance.MissionTimer;
            int   min = (int)(t / 60f);
            int   sec = (int)(t % 60f);
            missionTimerText.text = $"{min:D2}:{sec:D2}";
        }

        // ─── Event callbacks ─────────────────────────────────────────────────
        private void OnLockProgress(float progress)
        {
            if (lockWarningPanel != null)
                lockWarningPanel.SetActive(true);
        }

        private void OnLockAcquired()
        {
            StartCoroutine(FlashLockAcquired());
        }

        private void OnLockLost()
        {
            _lockWarningBlink = 0f;
            if (lockWarningPanel != null)
                lockWarningPanel.SetActive(false);
        }

        private void UpdateScore(int score)
        {
            if (scoreText != null) scoreText.text = $"{score:N0}";
        }

        private void OnStateChanged(GameStateContext ctx)
        {
            gameObject.SetActive(ctx.NewState == GameState.Flying);
        }

        // ─── Coroutines ───────────────────────────────────────────────────────
        private IEnumerator FlashLockAcquired()
        {
            if (lockWarningText == null) yield break;

            for (int i = 0; i < 4; i++)
            {
                lockWarningText.text = "LOCK ACQUIRED";
                lockWarningText.gameObject.SetActive(true);
                yield return new WaitForSeconds(0.1f);
                lockWarningText.gameObject.SetActive(false);
                yield return new WaitForSeconds(0.1f);
            }
        }

        // ─── Cleanup ─────────────────────────────────────────────────────────
        private void UnbindSystems()
        {
            if (_lockOn != null)
            {
                _lockOn.OnLockProgress  -= OnLockProgress;
                _lockOn.OnLockAcquired  -= _ => OnLockAcquired();
                _lockOn.OnLockLost      -= OnLockLost;
            }
        }
    }
}
