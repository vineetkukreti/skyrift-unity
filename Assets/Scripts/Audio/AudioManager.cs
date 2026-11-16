using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Central audio singleton.  Manages separate AudioSource tracks for
    /// music layers (calm/intense) and a pooled SFX source.
    ///
    /// Music layers are crossfaded based on the combat threat level reported
    /// by any system via <see cref="SetThreatLevel"/>.
    ///
    /// Volume buses (master / music / SFX) are controlled separately and
    /// persisted in PlayerPrefs.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────
        public static AudioManager Instance { get; private set; }

        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Music Layers")]
        [SerializeField] private AudioClip musicCalmLayer;
        [SerializeField] private AudioClip musicIntenseLayer;
        [SerializeField] private float     musicCrossfadeSpeed = 1.5f;

        [Header("SFX Clips")]
        [SerializeField] private AudioClip gunfireClip;
        [SerializeField] private AudioClip missileLaunchClip;
        [SerializeField] private AudioClip explosionClip;
        [SerializeField] private AudioClip lockToneClip;
        [SerializeField] private AudioClip lockAcquiredClip;
        [SerializeField] private AudioClip stallWarningClip;
        [SerializeField] private AudioClip uiClickClip;

        [Header("SFX Pool")]
        [SerializeField] private int sfxPoolSize = 8;

        [Header("Initial Volumes")]
        [SerializeField, Range(0f, 1f)] private float defaultMasterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float defaultMusicVolume  = 0.6f;
        [SerializeField, Range(0f, 1f)] private float defaultSFXVolume    = 1f;

        // ─── Volume properties ────────────────────────────────────────────────
        public float MasterVolume { get; private set; }
        public float MusicVolume  { get; private set; }
        public float SFXVolume    { get; private set; }

        // ─── Private ──────────────────────────────────────────────────────────
        private AudioSource _calmSource;
        private AudioSource _intenseSource;
        private AudioSource _lockToneSource;
        private AudioSource[] _sfxPool;
        private int            _sfxPoolIndex;

        private float _threatLevel;       // 0=calm, 1=intense (set by combat systems)
        private float _currentIntense;    // current crossfade blend

        private const string VolMasterKey = "VOL_MASTER";
        private const string VolMusicKey  = "VOL_MUSIC";
        private const string VolSFXKey    = "VOL_SFX";

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

            MasterVolume = PlayerPrefs.GetFloat(VolMasterKey, defaultMasterVolume);
            MusicVolume  = PlayerPrefs.GetFloat(VolMusicKey,  defaultMusicVolume);
            SFXVolume    = PlayerPrefs.GetFloat(VolSFXKey,    defaultSFXVolume);

            InitMusicSources();
            InitSFXPool();
        }

        private void Update()
        {
            CrossfadeMusic();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        // ── Volume buses ──────────────────────────────────────────────────────

        /// <summary>Sets master volume 0..1 and saves to PlayerPrefs.</summary>
        public void SetMasterVolume(float volume)
        {
            MasterVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(VolMasterKey, MasterVolume);
            ApplyVolumes();
        }

        /// <summary>Sets music bus volume 0..1.</summary>
        public void SetMusicVolume(float volume)
        {
            MusicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(VolMusicKey, MusicVolume);
            ApplyVolumes();
        }

        /// <summary>Sets SFX bus volume 0..1.</summary>
        public void SetSFXVolume(float volume)
        {
            SFXVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(VolSFXKey, SFXVolume);
        }

        // ── Music control ─────────────────────────────────────────────────────

        /// <summary>
        /// Sets the threat level 0..1 which drives the calm/intense music crossfade.
        /// Call this from combat systems whenever the threat level changes.
        /// </summary>
        public void SetThreatLevel(float level)
        {
            _threatLevel = Mathf.Clamp01(level);
        }

        // ── SFX playback ──────────────────────────────────────────────────────

        /// <summary>Plays a gunfire burst sound at the given world position.</summary>
        public void PlayGunfire(Vector3 position)  => PlaySFX(gunfireClip, position);

        /// <summary>Plays a missile launch sound.</summary>
        public void PlayMissileLaunch(Vector3 pos) => PlaySFX(missileLaunchClip, pos);

        /// <summary>Plays an explosion at the given position.</summary>
        public void PlayExplosion(Vector3 position) => PlaySFX(explosionClip, position);

        /// <summary>Starts or stops the missile lock tone.</summary>
        public void SetLockTone(bool active)
        {
            if (_lockToneSource == null || lockToneClip == null) return;
            if (active && !_lockToneSource.isPlaying)
                _lockToneSource.Play();
            else if (!active && _lockToneSource.isPlaying)
                _lockToneSource.Stop();
        }

        /// <summary>Plays the lock-acquired confirmation chime.</summary>
        public void PlayLockAcquired()   => PlaySFX(lockAcquiredClip,  Vector3.zero);

        /// <summary>Plays the stall warning tone (one-shot).</summary>
        public void PlayStallWarning()   => PlaySFX(stallWarningClip,  Vector3.zero);

        /// <summary>Plays the UI button click sound.</summary>
        public void PlayUIClick()        => PlaySFX(uiClickClip,        Vector3.zero);

        /// <summary>Plays any arbitrary AudioClip from the SFX pool.</summary>
        public void PlaySFX(AudioClip clip, Vector3 worldPosition, float pitchVariance = 0.05f)
        {
            if (clip == null) return;

            AudioSource src = _sfxPool[_sfxPoolIndex % sfxPoolSize];
            _sfxPoolIndex++;

            src.transform.position = worldPosition;
            src.clip   = clip;
            src.volume = SFXVolume * MasterVolume;
            src.pitch  = 1f + UnityEngine.Random.Range(-pitchVariance, pitchVariance);
            src.Play();
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void InitMusicSources()
        {
            _calmSource   = CreateAudioSource("Music_Calm",   musicCalmLayer,    true);
            _intenseSource = CreateAudioSource("Music_Intense", musicIntenseLayer, true);
            _lockToneSource = CreateAudioSource("LockTone",   lockToneClip,       true);
            _lockToneSource.loop = true;
            _lockToneSource.volume = 0f;   // start silent; SetLockTone enables it

            _calmSource.volume    = MusicVolume * MasterVolume;
            _intenseSource.volume = 0f;

            _calmSource.Play();
            _intenseSource.Play();
        }

        private void InitSFXPool()
        {
            _sfxPool = new AudioSource[sfxPoolSize];
            for (int i = 0; i < sfxPoolSize; i++)
            {
                GameObject go = new GameObject($"SFX_Pool_{i}");
                go.transform.SetParent(transform);
                _sfxPool[i] = go.AddComponent<AudioSource>();
                _sfxPool[i].playOnAwake = false;
                _sfxPool[i].spatialBlend = 1f;   // 3D
            }
        }

        private AudioSource CreateAudioSource(string objName, AudioClip clip, bool loop)
        {
            GameObject go = new GameObject(objName);
            go.transform.SetParent(transform);
            AudioSource src = go.AddComponent<AudioSource>();
            src.clip         = clip;
            src.loop         = loop;
            src.playOnAwake  = false;
            src.spatialBlend = 0f;   // 2D for music
            return src;
        }

        private void CrossfadeMusic()
        {
            _currentIntense = Mathf.MoveTowards(
                _currentIntense,
                _threatLevel,
                musicCrossfadeSpeed * Time.deltaTime
            );

            float musicBus = MusicVolume * MasterVolume;
            if (_calmSource    != null) _calmSource.volume    = (1f - _currentIntense) * musicBus;
            if (_intenseSource != null) _intenseSource.volume = _currentIntense         * musicBus;
        }

        private void ApplyVolumes()
        {
            float musicBus = MusicVolume * MasterVolume;
            if (_calmSource    != null) _calmSource.volume    = (1f - _currentIntense) * musicBus;
            if (_intenseSource != null) _intenseSource.volume = _currentIntense         * musicBus;
        }
    }
}
