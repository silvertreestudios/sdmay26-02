using System;
using UnityEngine;
using UnityEngine.UIElements;

public class HowToPlayMenuControl : MonoBehaviour
{
    public VisualElement ui;
    private Button backButton;
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
    }

    private void OnDestroy()
    {
        if (backButton != null)
            backButton.clicked -= Close;
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
