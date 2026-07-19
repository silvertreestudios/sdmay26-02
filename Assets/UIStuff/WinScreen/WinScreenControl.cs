using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class WinScreenControl : MonoBehaviour
{
    private VisualElement ui;
    private Button mainMenuButton;

    private void Awake()
    {
        ui = GetComponent<UIDocument>().rootVisualElement;
        ui.style.display = DisplayStyle.None;
    }

    private void Start()
    {
        OnCombatOutcome.AddListener(OnCombatOutcomeHandler);
    }

    private void OnDestroy()
    {
        if (mainMenuButton != null)
            mainMenuButton.clicked -= GoToMainMenu;
        OnCombatOutcome.RemoveListener(OnCombatOutcomeHandler);
    }

    private void OnCombatOutcomeHandler(bool playerWon)
    {
        if (!playerWon)
            return;
        if (SceneManager.GetActiveScene().name != "Level3")
            return;

        TextAsset jsonAsset = Resources.Load<TextAsset>("storyboard");
        if (jsonAsset != null)
        {
            StoryBoardData data = JsonUtility.FromJson<StoryBoardData>(jsonAsset.text);
            ui.Q<Label>("WinText").text = data.END?.Message ?? "";
        }

        mainMenuButton = ui.Q<Button>("MainMenuButton");
        mainMenuButton.clicked += GoToMainMenu;

        HUDController hud = FindFirstObjectByType<HUDController>();
        if (hud != null)
            hud.ui.style.display = DisplayStyle.None;

        Time.timeScale = 0f;
        ui.style.display = DisplayStyle.Flex;
    }

    private void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneTransitionManager.FadeAndLoad("MainMenuScene");
    }

    [Serializable]
    private class StoryEntry
    {
        public string Title;
        public string Message;
    }

    [Serializable]
    private class StoryBoardData
    {
        public StoryEntry END;
    }
}
