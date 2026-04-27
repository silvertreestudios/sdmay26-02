using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;


public class MainMenuControl : MonoBehaviour
{

    public VisualElement ui;
    [SerializeField] private SettingsMenuControl settingsMenuControl;
    public Button newGameButton;
    public Button loadGameButton;
    public Button optionsButton;
    public Button exitButton;

    private void Awake() {
        ui = GetComponent<UIDocument>().rootVisualElement;
    }

    private void OnEnable() {
        newGameButton = ui.Q<Button>("CharacterCreationButton");
        newGameButton.clicked += CharacterCreation;

        loadGameButton = ui.Q<Button>("PlayGameButton");
        loadGameButton.clicked += PlayGame;

        optionsButton = ui.Q<Button>("OptionsButton");
        optionsButton.clicked += Options;

        exitButton = ui.Q<Button>("ExitButton");
        exitButton.clicked += Exit;
    }

    public void CharacterCreation() {
        SceneTransitionManager.FadeAndLoad("CharacterCreationScene");
    }

    public void PlayGame() {
        SceneTransitionManager.FadeAndLoad("Level1");
    }

    public void Options() {
        if (settingsMenuControl != null)
            settingsMenuControl.Open();
    }

    public void Exit() {
        Application.Quit();
        Debug.Log("Clicked Exit button");
    }
}
