using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;

public class SettingsMenuControl : MonoBehaviour
{
    public AudioMixer audioMixer;

    public static event Action<float> OnLogOpacityChanged;
    public const string LogOpacityKey = "CombatLogOpacity";
    public const float LogOpacityDefault = 0.35f;

    public const string MasterVolumeKey = "MasterVolume";
    public const string MusicVolumeKey = "MusicVolume";
    public const string SFXVolumeKey = "SFXVolume";
    public const string FullscreenKey = "Fullscreen";

    public VisualElement ui;
    private Button backButton;
    private Slider logOpacitySlider;
    private Slider masterVolumeSlider;
    private Slider musicVolumeSlider;
    private Slider sfxVolumeSlider;
    private Toggle fullScreenToggle;
    private Action onCloseCallback;

    private void Awake()
    {
        ui = GetComponent<UIDocument>().rootVisualElement;
        ui.style.display = DisplayStyle.None;
    }

    private void Start()
    {
        backButton = ui.Q<Button>("BackButton");
        backButton.clicked += Close;

        logOpacitySlider = ui.Q<Slider>("LogOpacitySlider");
        if (logOpacitySlider != null)
        {
            logOpacitySlider.value = PlayerPrefs.GetFloat(LogOpacityKey, LogOpacityDefault);
            logOpacitySlider.RegisterValueChangedCallback(OnOpacitySliderChanged);
        }

        masterVolumeSlider = ui.Q<Slider>("MasterVolumeSlider");
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = PlayerPrefs.GetFloat(MasterVolumeKey, 0.5f);
            SetVolume("MasterVolume", masterVolumeSlider.value);
            masterVolumeSlider.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetFloat(MasterVolumeKey, evt.newValue);
                SetVolume("MasterVolume", evt.newValue);
            });
        }

        musicVolumeSlider = ui.Q<Slider>("MusicVolumeSlider");
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = PlayerPrefs.GetFloat(MusicVolumeKey, 0.5f);
            SetVolume("MusicVolume", musicVolumeSlider.value);
            musicVolumeSlider.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetFloat(MusicVolumeKey, evt.newValue);
                SetVolume("MusicVolume", evt.newValue);
            });
        }

        sfxVolumeSlider = ui.Q<Slider>("SFXVolumeSlider");
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = PlayerPrefs.GetFloat(SFXVolumeKey, 0.5f);
            SetVolume("SFXVolume", sfxVolumeSlider.value);
            sfxVolumeSlider.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetFloat(SFXVolumeKey, evt.newValue);
                SetVolume("SFXVolume", evt.newValue);
            });
        }

        fullScreenToggle = ui.Q<Toggle>();
        if (fullScreenToggle != null)
        {
            bool isFullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
            fullScreenToggle.value = isFullscreen;
            Screen.fullScreen = isFullscreen;
            fullScreenToggle.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetInt(FullscreenKey, evt.newValue ? 1 : 0);
                Screen.fullScreen = evt.newValue;
            });
        }
    }

    private void SetVolume(string parameterName, float sliderValue)
    {
        if (audioMixer != null)
        {
            // Convert linear slider (0-1) to logarithmic dB (-80 to 0)
            float dbValue = sliderValue > 0.0001f ? Mathf.Log10(sliderValue) * 20f : -80f;
            audioMixer.SetFloat(parameterName, dbValue);
        }
    }

    private void OnDestroy()
    {
        if (backButton != null)
            backButton.clicked -= Close;
        if (logOpacitySlider != null)
            logOpacitySlider.UnregisterValueChangedCallback(OnOpacitySliderChanged);
    }

    private void OnOpacitySliderChanged(ChangeEvent<float> evt)
    {
        PlayerPrefs.SetFloat(LogOpacityKey, evt.newValue);
        OnLogOpacityChanged?.Invoke(evt.newValue);
    }

    public bool IsOpen => ui.style.display == DisplayStyle.Flex;

    public void Open(Action onClose = null)
    {
        onCloseCallback = onClose;
        ui.style.display = DisplayStyle.Flex;
    }

    public void Close()
    {
        ui.style.display = DisplayStyle.None;
        onCloseCallback?.Invoke();
        onCloseCallback = null;
    }
}
