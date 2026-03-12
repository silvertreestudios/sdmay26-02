using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;


public class MainMenuControl : MonoBehaviour
{

    public VisualElement ui;
    public Button newGameButton;
    public Button loadGameButton;
    public Button optionsButton;
    public Button exitButton;

    private void Awake() {
        ui = GetComponent<UIDocument>().rootVisualElement;
    }

    private void OnEnable() {
        newGameButton = ui.Q<Button>("NewGameButton");
        newGameButton.clicked += NewGame;

        loadGameButton = ui.Q<Button>("LoadGameButton");
        loadGameButton.clicked += LoadGame;

        optionsButton = ui.Q<Button>("OptionsButton");
        optionsButton.clicked += Options;

        exitButton = ui.Q<Button>("ExitButton");
        exitButton.clicked += Exit;
    }

    public void NewGame() {
        SceneManager.LoadScene("CharacterCreationScene");
    }

    public void LoadGame() {
        SceneManager.LoadScene("Level1");
    }

    public void Options() {
        Debug.Log("Clicked Options button");
    }

    public void Exit() {
        Application.Quit();
        Debug.Log("Clicked Exit button");
    }
}
