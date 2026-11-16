using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Per-aircraft engine audio controller.
    /// Drives two AudioSources (idle loop and full-power loop) cross-faded
    /// by throttle.  Pitch rises with throttle and linearly maps to airspeed.
    /// Afterburner adds a third "roar" layer.
    /// Wind noise is blended in separately based on airspeed.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudio : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Clips")]
        [SerializeField] private AudioClip engineIdleClip;
        [SerializeField] private AudioClip engineFullClip;
        [SerializeField] private AudioClip afterburnerClip;
        [SerializeField] private AudioClip windNoiseClip;

        [Header("Pitch Range")]
        [SerializeField] private float idlePitch   = 0.6f;
        [SerializeField] private float maxPitch     = 1.6f;
        [SerializeField] private float abPitch      = 2.0f;

        [Header("Volume")]
        [SerializeField] private float baseVolume        = 0.8f;
        [SerializeField] private float windMaxVolume     = 0.4f;
        [SerializeField] private float windStartSpeedMs  = 80f;
        [SerializeField] private float windMaxSpeedMs    = 300f;

        [Header("Smoothing")]
        [SerializeField] private float pitchSmoothTime   = 0.15f;
        [SerializeField] private float volumeSmoothTime  = 0.1f;

        // ─── Private ──────────────────────────────────────────────────────────
        private AudioSource _idleSource;
        private AudioSource _fullSource;
        private AudioSource _afterburnerSource;
        private AudioSource _windSource;

        private FlightPhysics    _physics;
        private FlightController _controller;
        private AircraftStats    _stats;

        private float _currentPitch;
        private float _pitchVelocity;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _physics    = GetComponent<FlightPhysics>();
            _controller = GetComponent<FlightController>();
            _stats      = GetComponent<AircraftStats>();

            _idleSource        = GetComponent<AudioSource>();
            _fullSource        = CreateSource(engineFullClip,   baseVolume, true);
            _afterburnerSource = CreateSource(afterburnerClip,  0f,         true);
            _windSource        = CreateSource(windNoiseClip,    0f,         true);

            _idleSource.clip   = engineIdleClip;
            _idleSource.loop   = true;
            _idleSource.volume = baseVolume;
            _idleSource.spatialBlend = 1f;

            _idleSource.Play();
            _fullSource.Play();
            if (afterburnerClip != null) _afterburnerSource.Play();
            if (windNoiseClip   != null) _windSource.Play();

            _currentPitch = idlePitch;
        }

        private void Update()
        {
            if (_physics    == null) return;

            float throttle = _controller != null ? _controller.Throttle : _physics.ThrottleInput;
            bool  ab       = _controller != null && _controller.AfterburnerActive;

            UpdateEnginePitchVolume(throttle, ab);
            UpdateWindNoise();
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void UpdateEnginePitchVolume(float throttle, bool afterburner)
        {
            // Target pitch: idle + (max-idle)*throttle, boosted further for AB
            float targetPitch = afterburner
                ? abPitch
                : Mathf.Lerp(idlePitch, maxPitch, throttle);

            _currentPitch = Mathf.SmoothDamp(
                _currentPitch, targetPitch,
                ref _pitchVelocity, pitchSmoothTime
            );

            float masterVol = AudioManager.Instance != null
                ? AudioManager.Instance.MasterVolume * AudioManager.Instance.SFXVolume
                : 1f;

            // Cross-fade idle/full based on throttle
            float idleVol = Mathf.Lerp(baseVolume, 0f,         throttle) * masterVol;
            float fullVol = Mathf.Lerp(0f,         baseVolume, throttle) * masterVol;
            float abVol   = afterburner ? baseVolume * masterVol : 0f;

            if (_idleSource        != null) { _idleSource.pitch        = _currentPitch; _idleSource.volume        = idleVol; }
            if (_fullSource        != null) { _fullSource.pitch        = _currentPitch; _fullSource.volume        = fullVol; }
            if (_afterburnerSource != null) { _afterburnerSource.pitch = _currentPitch; _afterburnerSource.volume = abVol;   }
        }

        private void UpdateWindNoise()
        {
            if (_windSource == null) return;

            float speed    = _physics.SpeedMS;
            float normWind = Mathf.InverseLerp(windStartSpeedMs, windMaxSpeedMs, speed);
            float masterVol = AudioManager.Instance != null
                ? AudioManager.Instance.MasterVolume * AudioManager.Instance.SFXVolume
                : 1f;

            _windSource.volume = Mathf.Lerp(_windSource.volume,
                normWind * windMaxVolume * masterVol,
                Time.deltaTime / volumeSmoothTime);
        }

        private AudioSource CreateSource(AudioClip clip, float vol, bool loop)
        {
            GameObject go = new GameObject(clip != null ? clip.name : "AudioSource");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            AudioSource src = go.AddComponent<AudioSource>();
            src.clip         = clip;
            src.volume       = vol;
            src.loop         = loop;
            src.playOnAwake  = false;
            src.spatialBlend = 1f;
            return src;
        }
    }
}
