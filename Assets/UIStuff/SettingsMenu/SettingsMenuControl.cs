using System;
using UnityEngine;
using UnityEngine.UIElements;

public class SettingsMenuControl : MonoBehaviour
{
    public static event Action<float> OnLogOpacityChanged;
    public const string LogOpacityKey = "CombatLogOpacity";
    public const float LogOpacityDefault = 0.35f;

    public VisualElement ui;
    private Button backButton;
    private Slider logOpacitySlider;
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
    }

    private void OnDestroy()
    {
        if (backButton != null) backButton.clicked -= Close;
        if (logOpacitySlider != null) logOpacitySlider.UnregisterValueChangedCallback(OnOpacitySliderChanged);
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
