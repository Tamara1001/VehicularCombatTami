using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the volume sliders inside the Options panel.
///
/// Responsibilities:
/// - On Start(), reads the saved PlayerPrefs values to set each slider's
///   visual position so it matches the last session's settings.
/// - Registers onValueChanged listeners that forward the new slider value
///   to AudioManager.Instance.SetXxxVolume(), which handles the logarithmic
///   conversion and PlayerPrefs persistence.
///
/// Setup:
/// 1. Attach this script to the Options Panel root GameObject (or a child).
/// 2. Drag the three UI Sliders into the Inspector fields below.
/// 3. Each Slider MUST have Min Value = 0.0001 and Max Value = 1.
///    (A Min Value of exactly 0 would produce -Infinity dB via Log10.)
///
/// Architecture rules (context.md):
/// - No direct references to the AudioMixer — all volume logic is centralised
///   in AudioManager, keeping this script purely a UI binding.
/// - No coroutines, no DOTween, no Time.timeScale changes.
/// - Uses AudioManager's public PREF_KEY constants to read PlayerPrefs,
///   avoiding duplicated magic strings.
/// </summary>
public class UI_OptionsMenu : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector Fields — drag the matching Slider components here
    // -------------------------------------------------------------------------

    [Header("Volume Sliders")]
    [Tooltip(
        "Reference to the Music volume Slider component.\n" +
        "Min Value must be 0.0001, Max Value must be 1.")]
    [SerializeField] private Slider _musicSlider;

    [Tooltip(
        "Reference to the SFX volume Slider component.\n" +
        "Min Value must be 0.0001, Max Value must be 1.")]
    [SerializeField] private Slider _sfxSlider;


    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises slider positions from saved preferences and wires up
    /// onValueChanged listeners.
    ///
    /// Important: listeners are added AFTER setting .value so the initial
    /// assignment does not trigger a redundant SetXxxVolume() call — the
    /// AudioManager already applied these values during its own Awake().
    /// </summary>
    private void Start()
    {
        // --- Read saved preferences (default to 1 = full volume) -------------
        float savedMusic = PlayerPrefs.GetFloat(AudioManager.PREF_KEY_MUSIC, 1f);
        float savedSFX   = PlayerPrefs.GetFloat(AudioManager.PREF_KEY_SFX,   1f);

        // --- Set slider visuals to match the saved values --------------------
        // Listeners are not connected yet, so this won't fire SetXxxVolume().
        if (_musicSlider != null)
            _musicSlider.value = savedMusic;

        if (_sfxSlider != null)
            _sfxSlider.value = savedSFX;

        // --- Register listeners AFTER initial value assignment ----------------
        // Each listener simply forwards the float to the AudioManager singleton.
        if (_musicSlider != null)
            _musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);

        if (_sfxSlider != null)
            _sfxSlider.onValueChanged.AddListener(OnSFXSliderChanged);

    }

    /// <summary>
    /// Removes all listeners added by this script when the GameObject is
    /// destroyed, preventing ghost callbacks if the AudioManager outlives
    /// this UI element (which it will, thanks to DontDestroyOnLoad).
    /// </summary>
    private void OnDestroy()
    {
        if (_musicSlider != null)
            _musicSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);

        if (_sfxSlider != null)
            _sfxSlider.onValueChanged.RemoveListener(OnSFXSliderChanged);

    }

    // -------------------------------------------------------------------------
    // Slider Callbacks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called every time the Music slider value changes.
    /// Forwards the linear value [0.0001, 1] to the AudioManager.
    /// </summary>
    /// <param name="value">New slider value.</param>
    private void OnMusicSliderChanged(float value)
    {
        AudioManager.Instance.SetMusicVolume(value);
    }

    /// <summary>
    /// Called every time the SFX slider value changes.
    /// Forwards the linear value [0.0001, 1] to the AudioManager.
    /// </summary>
    /// <param name="value">New slider value.</param>
    private void OnSFXSliderChanged(float value)
    {
        AudioManager.Instance.SetSFXVolume(value);
    }

    /// <summary>
    /// Called every time the Voice slider value changes.
    /// Forwards the linear value [0.0001, 1] to the AudioManager.
    /// </summary>
    /// <param name="value">New slider value.</param>
}
