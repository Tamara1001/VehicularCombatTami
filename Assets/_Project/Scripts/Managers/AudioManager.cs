using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Controlador central de audio del proyecto.
/// 
/// Reglas de Arquitectura:
/// - Singleton con DontDestroyOnLoad. Los duplicados se autodestruyen en el Awake.
/// - Se suscribe a GameManager.OnStateChanged para reaccionar a los estados globales.
/// - Utiliza diccionarios de optimización para buscar los fragmentos de audio en tiempo O(1).
/// - Las transiciones de música de fondo (BGM) utilizan corrutinas para crossfade (desvanecimiento suave).
/// </summary>
public class AudioManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------
    public static AudioManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Constantes — Parámetros del AudioMixer y Claves de PlayerPrefs
    // -------------------------------------------------------------------------
    private const string MIXER_PARAM_MUSIC = "MusicVolume";
    private const string MIXER_PARAM_SFX = "SFXVolume";

    public const string PREF_KEY_MUSIC = "Volume_Music";
    public const string PREF_KEY_SFX = "Volume_SFX";

    private const float DEFAULT_VOLUME = 1f;

    // -------------------------------------------------------------------------
    // Estructuras de Mapeo Serializables
    // -------------------------------------------------------------------------
    [Serializable]
    public struct SFXEntry
    {
        [Tooltip("ID único en cadena de texto para reproducir este efecto (Ej: 'sfx_laser', 'sfx_coin').")]
        public string id;
        [Tooltip("El archivo de audio (AudioClip) correspondiente.")]
        public AudioClip clip;
    }

    [Serializable]
    public struct BGMEntry
    {
        [Tooltip("ID único en cadena de texto para esta música de fondo (Ej: 'bgm_pantano', 'bgm_boss').")]
        public string id;
        [Tooltip("El archivo de música correspondiente.")]
        public AudioClip clip;
    }

    // -------------------------------------------------------------------------
    // Inspector — Canales del Audio Mixer
    // -------------------------------------------------------------------------
    [Header("Audio Mixer Groups")]
    [SerializeField] private AudioMixerGroup _musicMixerGroup;
    [SerializeField] private AudioMixerGroup _sfxMixerGroup;

    // -------------------------------------------------------------------------
    // Inspector — Librerías de Clips
    // -------------------------------------------------------------------------
    [Header("Librerías de Sonido")]
    [SerializeField] private List<SFXEntry> _sfxLibrary = new List<SFXEntry>();
    [SerializeField] private List<BGMEntry> _bgmLibrary = new List<BGMEntry>();

    [Tooltip("Música que suena automáticamente en el Menú Principal.")]
    [SerializeField] private AudioClip _mainMenuMusic;

    [Header("Configuración de Transiciones")]
    [SerializeField][Range(0f, 1f)] private float _musicTargetVolume = 1f;

    // -------------------------------------------------------------------------
    // Runtime — Audio Sources e Internos
    // -------------------------------------------------------------------------
    private AudioSource _musicSource;
    private AudioSource _sfxSource;
    private Coroutine _fadeCoroutine;

    // Diccionarios de búsqueda rápida O(1)
    private Dictionary<string, AudioClip> _sfxDict;
    private Dictionary<string, AudioClip> _bgmDict;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------
    private void Awake()
    {
        // Resguardo del Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Creación dinámica de los AudioSources para mantener limpia la jerarquía
        _musicSource = CreateAudioSource("MusicSource", _musicMixerGroup, loop: true);
        _sfxSource = CreateAudioSource("SFXSource", _sfxMixerGroup, loop: false);

        _musicSource.volume = 0f;

        // Construcción de diccionarios indexados
        BuildLookupDictionaries();
    }

    private void Start()
    {
        // Cargamos los volúmenes un frame tarde para asegurar que el Mixer esté inicializado
        LoadSavedVolumePreferences();

        if (_mainMenuMusic != null)
        {
            PlayMusicWithCrossfade(_mainMenuMusic);
        }
    }

    private void OnEnable()
    {
        GameManager.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        GameManager.OnStateChanged -= HandleStateChanged;
    }

    // -------------------------------------------------------------------------
    // Manejador de Eventos del FSM
    // -------------------------------------------------------------------------
    private void HandleStateChanged(GameManager.GameState newState)
    {
        switch (newState)
        {
            case GameManager.GameState.MainMenu:
                PlayMusicWithCrossfade(_mainMenuMusic);
                break;

            case GameManager.GameState.Playing:
                // Aquí podés dejar que el WaveManager o el cargador de niveles decida qué BGM poner,
                // o podés arrancar una música por defecto si lo deseas.
                break;

            case GameManager.GameState.Pause:
                // Opcional: Podés bajar el volumen de la música con un filtro o dejar que siga normal
                break;

            case GameManager.GameState.GameOver:
                // Opcional: Detener música o pasar a un track de derrota
                break;
        }
    }

    // -------------------------------------------------------------------------
    // API Pública de Reproducción
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reproduce una música de fondo usando su ID registrado con un crossfade suave.
    /// </summary>
    public void PlayBGM(string bgmId, float fadeDuration = 1f)
    {
        if (!_bgmDict.TryGetValue(bgmId, out AudioClip clip))
        {
            Debug.LogWarning($"[AudioManager] PlayBGM: No hay música registrada con el ID '{bgmId}'.");
            return;
        }

        PlayMusicWithCrossfade(clip, fadeDuration);
    }

    /// <summary>
    /// Desvanece la música actual hasta detenerla por completo.
    /// </summary>
    public void StopBGM(float fadeDuration = 1f)
    {
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeOutAndStop(fadeDuration));
    }

    /// <summary>
    /// Reproduce un efecto de sonido instantáneo (OneShot) por su ID. Permite solapamiento.
    /// </summary>
    public void PlaySFX(string sfxId)
    {
        if (_sfxDict.TryGetValue(sfxId, out AudioClip clip))
        {
            _sfxSource.PlayOneShot(clip);
        }
        else
        {
            Debug.LogWarning($"[AudioManager] PlaySFX: No hay efecto registrado con el ID '{sfxId}'.");
        }
    }

    // -------------------------------------------------------------------------
    // API Pública de Opciones (Mapeo de Sliders de UI)
    // -------------------------------------------------------------------------
    public void SetMusicVolume(float linearValue)
    {
        ApplyVolume(MIXER_PARAM_MUSIC, PREF_KEY_MUSIC, linearValue);
    }

    public void SetSFXVolume(float linearValue)
    {
        ApplyVolume(MIXER_PARAM_SFX, PREF_KEY_SFX, linearValue);
    }

    // -------------------------------------------------------------------------
    // Lógica Privada de Transiciones e Interpolación
    // -------------------------------------------------------------------------
    private void PlayMusicWithCrossfade(AudioClip clip, float fadeDuration = 1f)
    {
        if (_musicSource.clip == clip && _musicSource.isPlaying)
            return;

        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        if (clip == null)
        {
            _fadeCoroutine = StartCoroutine(FadeOutAndStop(fadeDuration));
        }
        else if (!_musicSource.isPlaying)
        {
            _fadeCoroutine = StartCoroutine(FadeIn(clip, fadeDuration));
        }
        else
        {
            _fadeCoroutine = StartCoroutine(Crossfade(clip, fadeDuration));
        }
    }

    private IEnumerator Crossfade(AudioClip nextClip, float fadeDuration)
    {
        yield return StartCoroutine(FadeVolume(_musicSource.volume, 0f, fadeDuration));

        _musicSource.clip = nextClip;
        _musicSource.Play();

        yield return StartCoroutine(FadeVolume(0f, _musicTargetVolume, fadeDuration));
        _fadeCoroutine = null;
    }

    private IEnumerator FadeIn(AudioClip clip, float fadeDuration)
    {
        _musicSource.clip = clip;
        _musicSource.volume = 0f;
        _musicSource.Play();

        yield return StartCoroutine(FadeVolume(0f, _musicTargetVolume, fadeDuration));
        _fadeCoroutine = null;
    }

    private IEnumerator FadeOutAndStop(float fadeDuration)
    {
        yield return StartCoroutine(FadeVolume(_musicSource.volume, 0f, fadeDuration));

        _musicSource.Stop();
        _musicSource.clip = null;
        _fadeCoroutine = null;
    }

    private IEnumerator FadeVolume(float fromVolume, float toVolume, float duration)
    {
        if (duration <= 0f)
        {
            _musicSource.volume = toVolume;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Usamos unscaledDeltaTime para que los desvanecimientos funcionen correctamente en las pantallas de pausa
            elapsed += Time.unscaledDeltaTime;
            _musicSource.volume = Mathf.Lerp(fromVolume, toVolume, elapsed / duration);
            yield return null;
        }
        _musicSource.volume = toVolume;
    }

    // -------------------------------------------------------------------------
    // Helpers y Configuración Inicial
    // -------------------------------------------------------------------------
    private AudioSource CreateAudioSource(string sourceName, AudioMixerGroup mixerGroup, bool loop)
    {
        GameObject child = new GameObject(sourceName);
        child.transform.SetParent(transform);

        AudioSource source = child.AddComponent<AudioSource>();
        source.outputAudioMixerGroup = mixerGroup;
        source.loop = loop;
        source.playOnAwake = false;

        return source;
    }

    private void BuildLookupDictionaries()
    {
        _sfxDict = new Dictionary<string, AudioClip>(_sfxLibrary.Count);
        foreach (SFXEntry entry in _sfxLibrary)
        {
            if (!string.IsNullOrEmpty(entry.id)) _sfxDict[entry.id] = entry.clip;
        }

        _bgmDict = new Dictionary<string, AudioClip>(_bgmLibrary.Count);
        foreach (BGMEntry entry in _bgmLibrary)
        {
            if (!string.IsNullOrEmpty(entry.id)) _bgmDict[entry.id] = entry.clip;
        }
    }

    private void ApplyVolume(string mixerParam, string prefsKey, float linearValue)
    {
        linearValue = Mathf.Clamp(linearValue, 0.0001f, 1f);
        float dB = Mathf.Log10(linearValue) * 20f;

        if (_musicMixerGroup != null && _musicMixerGroup.audioMixer != null)
        {
            _musicMixerGroup.audioMixer.SetFloat(mixerParam, dB);
        }

        PlayerPrefs.SetFloat(prefsKey, linearValue);
    }

    private void LoadSavedVolumePreferences()
    {
        float music = PlayerPrefs.GetFloat(PREF_KEY_MUSIC, DEFAULT_VOLUME);
        float sfx = PlayerPrefs.GetFloat(PREF_KEY_SFX, DEFAULT_VOLUME);

        ApplyVolume(MIXER_PARAM_MUSIC, PREF_KEY_MUSIC, music);
        ApplyVolume(MIXER_PARAM_SFX, PREF_KEY_SFX, sfx);
    }
}