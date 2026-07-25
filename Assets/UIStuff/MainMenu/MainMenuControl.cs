using System;
using Game.DungeonPersistence;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Presents production New Run and Continue operations and validates all seed/save input before
/// beginning the procedural gameplay scene transition.
/// </summary>
public class MainMenuControl : MonoBehaviour
{
    [SerializeField]
    private SettingsMenuControl settingsMenuControl;

    private VisualElement ui;
    private TextField seedField;
    private Button newRunButton;
    private Button continueButton;
    private Button optionsButton;
    private Button exitButton;
    private Button confirmOverwriteButton;
    private Button cancelOverwriteButton;
    private Label statusLabel;
    private VisualElement overwriteConfirmation;
    private DungeonRunMenuService menuService;
    private DungeonRunLaunchRequest pendingNewRun = DungeonRunLaunchRequest.None;
    private bool launchRequested;
    private Func<DungeonRunLaunchRequest, bool> launchDungeon = LaunchDungeon;

    private void Awake()
    {
        // Dungeon departure freezes simulation after its final checkpoint. Restore normal time
        // as soon as the destination menu activates while the fade continues on unscaled time.
        Time.timeScale = 1f;
        ui = GetComponent<UIDocument>().rootVisualElement;
        menuService ??= DungeonRunMenuService.CreateDefault();
    }

    private void OnEnable()
    {
        ui ??= GetComponent<UIDocument>().rootVisualElement;
        menuService ??= DungeonRunMenuService.CreateDefault();
        launchRequested = false;
        ui.SetEnabled(true);
        seedField = ui.Q<TextField>("SeedField");
        newRunButton = ui.Q<Button>("NewRunButton");
        continueButton = ui.Q<Button>("ContinueButton");
        optionsButton = ui.Q<Button>("OptionsButton");
        exitButton = ui.Q<Button>("ExitButton");
        confirmOverwriteButton = ui.Q<Button>("ConfirmOverwriteButton");
        cancelOverwriteButton = ui.Q<Button>("CancelOverwriteButton");
        statusLabel = ui.Q<Label>("MenuStatusLabel");
        overwriteConfirmation = ui.Q<VisualElement>("OverwriteConfirmationOverlay");

        newRunButton.clicked += StartNewRun;
        continueButton.clicked += ContinueRun;
        optionsButton.clicked += Options;
        exitButton.clicked += Exit;
        confirmOverwriteButton.clicked += ConfirmNewRun;
        cancelOverwriteButton.clicked += CancelNewRun;

        overwriteConfirmation.style.display = DisplayStyle.None;
        RefreshAutosaveStatus();
    }

    private void OnDisable()
    {
        if (newRunButton != null)
            newRunButton.clicked -= StartNewRun;
        if (continueButton != null)
            continueButton.clicked -= ContinueRun;
        if (optionsButton != null)
            optionsButton.clicked -= Options;
        if (exitButton != null)
            exitButton.clicked -= Exit;
        if (confirmOverwriteButton != null)
            confirmOverwriteButton.clicked -= ConfirmNewRun;
        if (cancelOverwriteButton != null)
            cancelOverwriteButton.clicked -= CancelNewRun;
    }

    /// <summary>Validates the optional seed and requests confirmation before replacing a save.</summary>
    public void StartNewRun()
    {
        if (
            !menuService.TryCreateNewRunRequest(
                seedField.value,
                out DungeonRunLaunchRequest request,
                out string error
            )
        )
        {
            DisplayStatus(error, isError: true);
            return;
        }

        DungeonRunMenuStatus status = menuService.InspectAutosave();
        if (status.HasAutosave)
        {
            pendingNewRun = request;
            overwriteConfirmation.style.display = DisplayStyle.Flex;
            DisplayStatus("Starting a new run will replace the existing autosave.", isError: false);
            return;
        }

        Launch(request);
    }

    /// <summary>Launches the current compatible autosave when Continue remains available.</summary>
    public void ContinueRun()
    {
        DungeonRunMenuStatus status = menuService.InspectAutosave();
        if (!status.CanContinue)
        {
            RefreshAutosaveStatus();
            return;
        }

        Launch(menuService.CreateContinueRequest());
    }

    /// <summary>Confirms replacement of the existing autosave and starts the prepared run.</summary>
    public void ConfirmNewRun()
    {
        if (!pendingNewRun.IsPending)
            return;

        DungeonRunLaunchRequest request = pendingNewRun;
        pendingNewRun = DungeonRunLaunchRequest.None;
        overwriteConfirmation.style.display = DisplayStyle.None;
        Launch(request);
    }

    /// <summary>Dismisses autosave replacement without changing the existing run.</summary>
    public void CancelNewRun()
    {
        pendingNewRun = DungeonRunLaunchRequest.None;
        overwriteConfirmation.style.display = DisplayStyle.None;
        RefreshAutosaveStatus();
    }

    /// <summary>Opens the existing settings panel.</summary>
    public void Options()
    {
        if (settingsMenuControl != null)
            settingsMenuControl.Open();
    }

    /// <summary>Requests application shutdown.</summary>
    public void Exit()
    {
        Application.Quit();
        Debug.Log("Clicked Exit button");
    }

    internal void ConfigureForTests(
        DungeonRunMenuService replacementService,
        Action<DungeonRunLaunchRequest> replacementLaunch
    )
    {
        if (replacementLaunch == null)
            throw new ArgumentNullException(nameof(replacementLaunch));

        ConfigureLaunchResultForTests(
            replacementService,
            request =>
            {
                replacementLaunch(request);
                return true;
            }
        );
    }

    internal void ConfigureLaunchResultForTests(
        DungeonRunMenuService replacementService,
        Func<DungeonRunLaunchRequest, bool> replacementLaunch
    )
    {
        menuService =
            replacementService ?? throw new ArgumentNullException(nameof(replacementService));
        launchDungeon =
            replacementLaunch ?? throw new ArgumentNullException(nameof(replacementLaunch));
        if (isActiveAndEnabled)
            RefreshAutosaveStatus();
    }

    private void RefreshAutosaveStatus()
    {
        DungeonRunMenuStatus status = menuService.InspectAutosave();
        continueButton.SetEnabled(status.CanContinue);
        DisplayStatus(status.Message, isError: status.HasAutosave && !status.CanContinue);
    }

    private void Launch(DungeonRunLaunchRequest request)
    {
        if (launchRequested)
            return;

        launchRequested = true;
        DisplayStatus(
            request.Mode == DungeonRunLaunchMode.NewRun
                ? $"Starting dungeon run with seed {request.NormalizedSeed}."
                : "Continuing saved dungeon run.",
            isError: false
        );
        ui.SetEnabled(false);

        try
        {
            if (launchDungeon(request))
                return;

            launchRequested = false;
            ui.SetEnabled(true);
            DisplayStatus(
                "Another scene transition is already in progress. Try again.",
                isError: true
            );
        }
        catch
        {
            launchRequested = false;
            ui.SetEnabled(true);
            throw;
        }
    }

    private void DisplayStatus(string message, bool isError)
    {
        statusLabel.text = message;
        statusLabel.EnableInClassList("menu-status--error", isError);
    }

    private static bool LaunchDungeon(DungeonRunLaunchRequest request) =>
        SceneTransitionManager.FadeAndLoadDungeon(request);
}
