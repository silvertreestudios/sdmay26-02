using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class StatusMenuControl : MonoBehaviour
{
    public enum StatusType { Paused, YouWin, YouLose }

    public VisualElement ui;
    private Label statusLabel;
    private Button newGameButton;
    private Button restartLevelButton;
    private Button quitGameButton;

    private void Awake()
    {
        ui = GetComponent<UIDocument>().rootVisualElement;
        ui.style.display = DisplayStyle.None;
    }

    private void Start()
    {
        statusLabel = ui.Q<Label>("StatusLabel");

        newGameButton = ui.Q<Button>("NewGameButton");
        newGameButton.clicked += NewGame;

        restartLevelButton = ui.Q<Button>("RestartLevelButton");
        restartLevelButton.clicked += RestartLevel;

        quitGameButton = ui.Q<Button>("QuitGameButton");
        quitGameButton.clicked += QuitGame;
    }

    private void OnDestroy()
    {
        if (newGameButton != null) newGameButton.clicked -= NewGame;
        if (restartLevelButton != null) restartLevelButton.clicked -= RestartLevel;
        if (quitGameButton != null) quitGameButton.clicked -= QuitGame;
    }

    private bool isPaused = false;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
            {
                Hide();
            }
            else
            {
                Show(StatusType.Paused);
            }
        }
    }

    public void Show(StatusType status)
    {
        switch (status)
        {
            case StatusType.YouWin:
                statusLabel.text = "You Win";
                break;
            case StatusType.YouLose:
                statusLabel.text = "You Lose";
                break;
            case StatusType.Paused:
                statusLabel.text = "Paused";
                break;
        }

        isPaused = status == StatusType.Paused;
        if (isPaused) Time.timeScale = 0f;
        ui.style.display = DisplayStyle.Flex;
    }

    public void Hide()
    {
        isPaused = false;
        Time.timeScale = 1f;
        ui.style.display = DisplayStyle.None;
    }

    public void NewGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("CharacterCreationScene");
    }

    public void RestartLevel()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
        Application.Quit();
        Debug.Log("Clicked Quit Game button");
    }
}
