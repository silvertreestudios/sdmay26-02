using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class StatusMenuControl : MonoBehaviour
{
    public enum StatusType { Paused, YouWin, YouLose }

    public VisualElement ui;
    [SerializeField] private SettingsMenuControl settingsMenuControl;
    private Label statusLabel;
    private Button newGameButton;
    private Button restartLevelButton;
    private Button quitGameButton;
    private Button settingsButton;

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

        settingsButton = ui.Q<Button>("SettingsButton");
        settingsButton.clicked += OpenSettings;

        OnCombatOutcome.AddListener(OnCombatOutcomeHandler);
    }

    private void OnDestroy()
    {
        if (newGameButton != null) newGameButton.clicked -= NewGame;
        if (restartLevelButton != null) restartLevelButton.clicked -= RestartLevel;
        if (quitGameButton != null) quitGameButton.clicked -= QuitGame;
        if (settingsButton != null) settingsButton.clicked -= OpenSettings;
        OnCombatOutcome.RemoveListener(OnCombatOutcomeHandler);
    }

    private void OnCombatOutcomeHandler(bool playerWon)
    {
        Show(playerWon ? StatusType.YouWin : StatusType.YouLose);
    }

    private bool isPaused = false;
    private StatusType currentStatus = StatusType.Paused;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (settingsMenuControl != null && settingsMenuControl.IsOpen)
            {
                settingsMenuControl.Close();
                return;
            }

            if (isPaused)
                Hide();
            else
                Show(StatusType.Paused);
        }
    }

    public void Show(StatusType status)
    {
        switch (status)
        {
            case StatusType.YouWin:
                statusLabel.text = "You Win";
                statusLabel.style.color = new UnityEngine.UIElements.StyleColor(UnityEngine.Color.green);
                newGameButton.text = "Next Level";
                break;
            case StatusType.YouLose:
                statusLabel.text = "You Lose";
                statusLabel.style.color = new UnityEngine.UIElements.StyleColor(UnityEngine.Color.red);
                newGameButton.text = "New Game";
                break;
            case StatusType.Paused:
                statusLabel.text = "Paused";
                newGameButton.text = "New Game";
                break;
        }

        currentStatus = status;
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
        if (currentStatus == StatusType.YouWin)
        {
            int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
                SceneManager.LoadScene(nextSceneIndex);
        }
        else
        {
            SceneManager.LoadScene("CharacterCreationScene");
        }
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

    public void OpenSettings()
    {
        if (settingsMenuControl != null)
        {
            ui.style.display = DisplayStyle.None;
            settingsMenuControl.Open(onClose: () => Show(currentStatus));
        }
    }
}
