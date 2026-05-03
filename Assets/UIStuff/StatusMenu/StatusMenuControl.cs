using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class StatusMenuControl : MonoBehaviour
{
    public enum StatusType { Paused, YouWin, YouLose }

    public VisualElement ui;
    [SerializeField] private SettingsMenuControl settingsMenuControl;
    [SerializeField] private HowToPlayMenuControl howToPlayMenuControl;
    private Label statusLabel;
    private Button newGameButton;
    private Button restartLevelButton;
    private Button mainMenuButton;
    private Button settingsButton;
    private Button howToPlayButton;

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

        mainMenuButton = ui.Q<Button>("MainMenuButton");
        mainMenuButton.clicked += ReturnToMainMenu;

        settingsButton = ui.Q<Button>("SettingsButton");
        settingsButton.clicked += OpenSettings;

        howToPlayButton = ui.Q<Button>("HowToPlayButton");
        howToPlayButton.clicked += OpenHowToPlay;

        OnCombatOutcome.AddListener(OnCombatOutcomeHandler);
    }

    private void OnDestroy()
    {
        if (newGameButton != null) newGameButton.clicked -= NewGame;
        if (restartLevelButton != null) restartLevelButton.clicked -= RestartLevel;
        if (mainMenuButton != null) mainMenuButton.clicked -= ReturnToMainMenu;
        if (settingsButton != null) settingsButton.clicked -= OpenSettings;
        if (howToPlayButton != null) howToPlayButton.clicked -= OpenHowToPlay;
        OnCombatOutcome.RemoveListener(OnCombatOutcomeHandler);
    }

    private void OnCombatOutcomeHandler(bool playerWon)
    {
        if (playerWon && SceneManager.GetActiveScene().name == "Level3") return;
        StartCoroutine(ShowAfterDelay(playerWon ? StatusType.YouWin : StatusType.YouLose, 1f));
    }

    private IEnumerator ShowAfterDelay(StatusType status, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        Show(status);
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

            if (howToPlayMenuControl != null && howToPlayMenuControl.IsOpen)
            {
                howToPlayMenuControl.Close();
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
                SceneTransitionManager.FadeAndLoad(nextSceneIndex);
        }
        else
        {
            SceneTransitionManager.FadeAndLoad("CharacterCreationScene");
        }
    }

    public void RestartLevel()
    {
        Time.timeScale = 1f;
        SceneTransitionManager.FadeAndLoad(SceneManager.GetActiveScene().buildIndex);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        SceneTransitionManager.FadeAndLoad("MainMenuScene");
    }

    public void OpenSettings()
    {
        if (settingsMenuControl != null)
        {
            ui.style.display = DisplayStyle.None;
            settingsMenuControl.Open(onClose: () => Show(currentStatus));
        }
    }

    public void OpenHowToPlay()
    {
        if (howToPlayMenuControl != null)
        {
            ui.style.display = DisplayStyle.None;
            howToPlayMenuControl.Open(onClose: () => Show(currentStatus));
        }
    }
}
