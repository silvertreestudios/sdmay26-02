using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UniversalEvents;

/// <summary>
/// Presents combat state and a migration-safe action bar that keeps legacy actions explicit while
/// launching typed rules-definition workflows through their shared Unity boundary.
/// </summary>
public class HUDController : SingletonMonoBehaviour<HUDController>
{
    public VisualElement ui;
    public Button endTurnButton;
    private VisualElement buttonGrid;
    private VisualElement panel;
    private InputAction toggleAutoCameraAction;
    private bool autoCameraEnabled = true;
    private bool wasFollowing = false;
    public static bool IsPointerOverLog { get; private set; }
    public static bool IsPointerOverHUD { get; private set; }
    private int _hudHoverCount = 0;
    private Coroutine slideCoroutine;
    private Coroutine logSlideCoroutine;
    private Button logToggleButton;
    private Button pauseButton;
    private Button speed2xButton;
    private Button speed3xButton;
    private Button speedToggleButton;
    private VisualElement speedButtonsBox;
    private bool speedBarVisible = true;
    private ActionController currentTurnAC;
    private Dictionary<Button, uint> buttonCostMap = new();
    private readonly Dictionary<Button, DefinitionActionButtonBinding> definitionButtonEntries =
        new();
    private DefinitionExecutionSession activeDefinitionExecution =
        DefinitionExecutionSession.Inactive;
    private int actionBarTurnGeneration;

    private Button selectedActionButton;
    private Color selectedButtonBaseColor;
    private const float GlowSpeed = 3f;
    private VisualElement combatLogElement;
    private VisualElement combatLogWrapper;
    private VisualElement resizeHandle;
    public bool logVisible = true; // exposed for testing
    private bool isResizing = false;
    private float resizeStartY;
    private float resizeStartHeight;

    private const float LogMinHeight = 150f;
    private const float LogMaxHeight = 800f;
    private const string LogHeightKey = "CombatLogHeight";
    private const float LogHeightDefault = 550f;
    private const float SlideDuration = 0.4f;
    private const float LogHiddenPercent = 80f;
    private const float PanelHiddenPercent = 80f;
    private const int MaxActionMedallions = 3;
    private const string ActionMedallionClass = "action-medallion";
    private const string ActionMedallionFilledClass = "action-medallion--filled";
    private const string ActionMedallionEmptyClass = "action-medallion--empty";
    public const string DisabledHudButtonClass = "btn-hud-disabled";

    //####Player Queue Card Variables####
    private VisualElement cardHolder;

    [SerializeField]
    private VisualTreeAsset playerCardTemplate;
    private bool needToUpdateCards = true;

    //####Cancel Action Button####
    private Button cancelActionButton;
    private bool canCancelAction = true;

    private static List<GameObject> Players;
    private static bool IsActive = false;

    private CombatLogInterface combatLog;

    protected override void Awake()
    {
        base.Awake();
        //Debug.Log("Awake called");
        ui = GetComponent<UIDocument>().rootVisualElement;
        combatLog = CombatLog.GetInstance();
        combatLog.Log("Game Started. Combat Log Initialized.");
        Debug.Log("Listener");
        OnCombatStart.AddListener(() =>
        {
            EnableUi();
            Setup();
        });
        OnNextTurn.AddListener(OnTurnChanged);
        OnActionConfirm.AddListener(() => canCancelAction = false);
        OnActionComplete.AddListener(() => canCancelAction = true);
        //Copiloy made this so I could point it to another UXML file for a template
        //I suspect it sucks
        //vvvvvvvvvvvvvvvvvv
        /**
            * Load the UXML file as a VisualTreeAsset
            * Note: This only works in the Editor. For builds, you would need to use Resources.Load or Addressables.
            */
    }

    private void RegisterHUDHover(VisualElement el)
    {
        el.RegisterCallback<MouseEnterEvent>(_ =>
        {
            _hudHoverCount++;
            IsPointerOverHUD = true;
        });
        el.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            _hudHoverCount = Mathf.Max(0, --_hudHoverCount);
            IsPointerOverHUD = _hudHoverCount > 0;
        });
    }

    private void OnEnable()
    {
        //Debug.Log("OnEnable called");
        //####Button Setup####

        buttonGrid = ui.Q<VisualElement>("ButtonGrid");
        panel = ui.Q<VisualElement>("Panel");

        toggleAutoCameraAction = InputSystem.actions.FindAction("ToggleAutoCamera");
        if (toggleAutoCameraAction != null)
            toggleAutoCameraAction.performed += OnToggleAutoCamera;

        endTurnButton = new Button(EndTurn);
        endTurnButton.name = "EndTurnButton";
        endTurnButton.text = "End Turn";
        endTurnButton.AddToClassList("btn-general");

        cancelActionButton = new Button(CancelAction);
        cancelActionButton.name = "CancelActionButton";
        cancelActionButton.text = "Cancel";
        cancelActionButton.AddToClassList("btn-cancel");

        //####Player Queue Card Setup####
        cardHolder = ui.Q<VisualElement>("CardHolder");
        // fillPlayerCards(); // Fix: Let Update() handle the initial fill to avoid double execution

        //####Combat Log Toggle####
        combatLogElement = ui.Q<VisualElement>("CombatLog");
        combatLogElement.RegisterCallback<MouseEnterEvent>(_ => IsPointerOverLog = true);
        combatLogElement.RegisterCallback<MouseLeaveEvent>(_ => IsPointerOverLog = false);
        combatLogWrapper = ui.Q<VisualElement>("CombatLogWrapper");
        logToggleButton = ui.Q<Button>("LogToggleButton");
        if (logToggleButton != null)
            logToggleButton.clicked += ToggleLog;

        pauseButton = ui.Q<Button>("PauseButton");
        speed2xButton = ui.Q<Button>("Speed2xButton");
        speed3xButton = ui.Q<Button>("Speed3xButton");
        speedToggleButton = ui.Q<Button>("SpeedToggleButton");
        speedButtonsBox = ui.Q<VisualElement>("SpeedButtonsBox");
        if (pauseButton != null)
            pauseButton.clicked += OnPauseClicked;
        if (speed2xButton != null)
            speed2xButton.clicked += OnSpeed2xClicked;
        if (speed3xButton != null)
            speed3xButton.clicked += OnSpeed3xClicked;
        if (speedToggleButton != null)
            speedToggleButton.clicked += ToggleSpeedBar;

        resizeHandle = ui.Q<VisualElement>("ResizeHandle");
        if (resizeHandle != null)
        {
            resizeHandle.RegisterCallback<PointerDownEvent>(OnResizeStart);
            resizeHandle.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            resizeHandle.RegisterCallback<PointerUpEvent>(OnResizeEnd);
        }
        float savedHeight = PlayerPrefs.GetFloat(LogHeightKey, LogHeightDefault);
        combatLogElement.style.height = Mathf.Clamp(savedHeight, LogMinHeight, LogMaxHeight);
        combatLogElement.RegisterCallback<GeometryChangedEvent>(ClampLogHeightToScreen);

        logVisible = true;
        if (logToggleButton != null)
            logToggleButton.text = "▶";
        if (combatLogWrapper != null)
            combatLogWrapper.style.translate = new StyleTranslate(
                new Translate(new Length(0, LengthUnit.Pixel), new Length(0, LengthUnit.Pixel))
            );

        RegisterHUDHover(panel);
        RegisterHUDHover(combatLogWrapper);
        RegisterHUDHover(cardHolder);
        if (speedButtonsBox != null)
            RegisterHUDHover(speedButtonsBox);

        SettingsMenuControl.OnLogOpacityChanged += ApplyLogOpacity;
        ApplyLogOpacity(
            PlayerPrefs.GetFloat(
                SettingsMenuControl.LogOpacityKey,
                SettingsMenuControl.LogOpacityDefault
            )
        );
    }

    private void OnDisable()
    {
        RetirePendingDefinitionExecution();
        actionBarTurnGeneration++;
        currentTurnAC = null;
        SetSelectedButton(null);
        //Debug.Log("OnDisable called");
        OnNextTurn.RemoveListener(OnTurnChanged);
        if (toggleAutoCameraAction != null)
            toggleAutoCameraAction.performed -= OnToggleAutoCamera;
        if (logToggleButton != null)
            logToggleButton.clicked -= ToggleLog;
        if (pauseButton != null)
            pauseButton.clicked -= OnPauseClicked;
        if (speed2xButton != null)
            speed2xButton.clicked -= OnSpeed2xClicked;
        if (speed3xButton != null)
            speed3xButton.clicked -= OnSpeed3xClicked;
        if (speedToggleButton != null)
            speedToggleButton.clicked -= ToggleSpeedBar;
        if (resizeHandle != null)
        {
            resizeHandle.UnregisterCallback<PointerDownEvent>(OnResizeStart);
            resizeHandle.UnregisterCallback<PointerMoveEvent>(OnResizeMove);
            resizeHandle.UnregisterCallback<PointerUpEvent>(OnResizeEnd);
        }
        IsPointerOverLog = false;
        _hudHoverCount = 0;
        IsPointerOverHUD = false;
        SettingsMenuControl.OnLogOpacityChanged -= ApplyLogOpacity;
    }

    public void EnableUi()
    {
        this.enabled = true;
    }

    public static void Setup()
    {
        Players = CombatManagerInterface.GetInstance().GetCombatants();

        IsActive = true;
    }

    void Update()
    {
        if (!IsActive)
            return;
        List<GameObject> currentCombatants = CombatManagerInterface.GetInstance().GetCombatants();
        if (Players == null)
        {
            Players = currentCombatants;
            needToUpdateCards = true;
        }
        else if (HaveCombatantsChanged(currentCombatants))
        {
            Players.RemoveAll(p => !currentCombatants.Contains(p));
            needToUpdateCards = true;
        }

        if (needToUpdateCards)
        {
            fillPlayerCards();
            needToUpdateCards = false;
        }

        bool isFollowing = CameraManager.GetInstance().IsFollowing;
        if (autoCameraEnabled && wasFollowing && !isFollowing)
        {
            autoCameraEnabled = false;
            combatLog.Log("Auto camera disabled.");
        }
        wasFollowing = isFollowing;

        if (currentTurnAC != null)
        {
            bool definitionCanCancel = CanCancelActiveDefinitionExecution();
            cancelActionButton.style.display =
                (
                    currentTurnAC.IsTakingAction
                    && (
                        definitionCanCancel
                        || (
                            !(activeDefinitionExecution is ActiveDefinitionExecutionSession)
                            && canCancelAction
                        )
                    )
                )
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (!isActionRunning() && selectedActionButton != null)
                SetSelectedButton(null);
        }

        if (selectedActionButton != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * GlowSpeed) + 1f) / 2f;
            Color bright = new Color(
                Mathf.Min(selectedButtonBaseColor.r * 1.8f, 1f),
                Mathf.Min(selectedButtonBaseColor.g * 1.8f, 1f),
                Mathf.Min(selectedButtonBaseColor.b * 1.8f, 1f),
                1f
            );
            selectedActionButton.style.backgroundColor = new StyleColor(
                Color.Lerp(selectedButtonBaseColor, bright, t)
            );
        }

        UpdateHudButtonStates();

        // Highlight the current player's card

        updatePlayerQueueCards();
    }

    // Card Logic attempt by Ryan
    private void fillPlayerCards()
    {
        if (cardHolder == null || Players == null)
        {
            return;
        }
        cardHolder.Clear();
        for (int i = 0; i < Players.Count; i++)
        {
            CreatureComponent p = Players[i].GetComponent<CreatureComponent>();
            TemplateContainer cardInstance = playerCardTemplate.Instantiate();
            cardHolder.Add(cardInstance);

            Team team = Players[i].GetComponent<Team>();
            string teamName = team != null ? team.Name : "";
            Color cardColor = teamName switch
            {
                "Zombies" => new Color(120f / 255f, 50f / 255f, 160f / 255f, 1f), // purple
                "Goblins" => new Color(85f / 255f, 120f / 255f, 40f / 255f, 1f), // sickly green
                _ => Players[i].GetComponent<PlayerActionController>() != null
                    ? new Color(28 / 255f, 114 / 255f, 135 / 255f, 1f) // player green
                    : new Color(166f / 255f, 49f / 255f, 49f / 255f, 1f), // default red
            };
            cardInstance.Q<VisualElement>("Card").style.backgroundColor = new StyleColor(cardColor);

            var portraitImage = cardInstance.Q<Image>("PortraitImage");
            if (portraitImage != null)
            {
                portraitImage.scaleMode = ScaleMode.ScaleToFit;
            }
            // Get portrait snapshot and display it
            Portrait portraitScript = Players[i].GetComponent<Portrait>();
            if (portraitScript != null)
            {
                Texture2D portraitSnapshot = portraitScript.GetPortraitSnapshot();
                if (portraitSnapshot == null)
                {
                    portraitScript.RefreshSnapshot();
                    portraitSnapshot = portraitScript.GetPortraitSnapshot();
                }
                if (portraitSnapshot != null && portraitImage != null)
                {
                    portraitImage.image = portraitSnapshot;
                }
            }

            // Pan camera to player when card is clicked
            GameObject captured = Players[i];
            cardInstance
                .Q<VisualElement>("Card")
                .RegisterCallback<ClickEvent>(evt =>
                {
                    CameraManager.GetInstance().PanToTarget(captured);
                });
            UpdateActionPointMedallions(cardInstance, Players[i].GetComponent<ActionController>());
        }
    }

    private bool HaveCombatantsChanged(List<GameObject> currentCombatants)
    {
        if (Players == null || currentCombatants == null)
            return true;

        if (Players.Count != currentCombatants.Count)
            return true;

        foreach (GameObject p in Players)
        {
            if (!currentCombatants.Contains(p))
                return true;
        }

        return false;
    }

    private void OnToggleAutoCamera(InputAction.CallbackContext context)
    {
        autoCameraEnabled = !autoCameraEnabled;
        if (autoCameraEnabled)
        {
            GameObject current = CombatManager.GetInstance().WhosTurn();
            if (current != null)
                CameraManager.GetInstance().PanToTarget(current, followIndefinitely: true);
            combatLog.Log("Auto camera enabled.");
        }
        else
        {
            CameraManager.GetInstance().StopFollowing();
            combatLog.Log("Auto camera disabled.");
        }
    }

    private void OnTurnChanged(GameObject turnTaker)
    {
        RetirePendingDefinitionExecution();
        int turnGeneration = ++actionBarTurnGeneration;
        if (slideCoroutine != null)
            StopCoroutine(slideCoroutine);

        // Rows remain visible while sliding out, so remove their turn authority synchronously.
        currentTurnAC = null;
        UpdateHudButtonStates();
        if (turnTaker == null)
        {
            if (buttonGrid != null)
                ClearAllRows();
            return;
        }

        ActionController ac = turnTaker.GetComponent<ActionController>();
        if (ac == null)
        {
            if (buttonGrid != null)
                ClearAllRows();
            return;
        }

        slideCoroutine = StartCoroutine(TurnTransitionRoutine(turnTaker, ac, turnGeneration));
    }

    private IEnumerator TurnTransitionRoutine(
        GameObject turnTaker,
        ActionController ac,
        int turnGeneration
    )
    {
        bool isPlayer = turnTaker.GetComponent<PlayerActionController>() != null;

        // Slide out first
        yield return StartCoroutine(Slide(false));
        if (turnGeneration != actionBarTurnGeneration)
            yield break;

        // Swap buttons while panel is hidden
        ClearAllRows();
        currentTurnAC = isPlayer ? ac : null;
        if (isPlayer)
        {
            IReadOnlyList<IActionBarEntry> actionEntries = ac.GetActionBarEntries();
            string log = turnTaker.name + " available actions (" + actionEntries.Count + "): ";
            foreach (IActionBarEntry entry in actionEntries)
                log += "[" + entry.DisplayName + "] ";
            Debug.Log(log);
            BuildActionButtons(ac, turnGeneration, actionEntries);
            BuildMovementButtons(turnTaker, ac.GetMovements());
        }

        // Slide back in for player turns only
        if (isPlayer)
            yield return StartCoroutine(Slide(true));

        if (autoCameraEnabled)
            CameraManager.GetInstance().PanToTarget(turnTaker, followIndefinitely: true);
    }

    private IEnumerator Slide(bool visible)
    {
        if (panel == null)
            yield break;
        float startX = visible ? -PanelHiddenPercent : 0f;
        float endX = visible ? 0f : -PanelHiddenPercent;
        float elapsed = 0f;
        while (elapsed < SlideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / SlideDuration));
            panel.style.translate = new StyleTranslate(
                new Translate(
                    new Length(Mathf.Lerp(startX, endX, t), LengthUnit.Percent),
                    new Length(0, LengthUnit.Pixel)
                )
            );
            yield return null;
        }
        panel.style.translate = new StyleTranslate(
            new Translate(new Length(endX, LengthUnit.Percent), new Length(0, LengthUnit.Pixel))
        );
    }

    private void OnResizeStart(PointerDownEvent e)
    {
        isResizing = true;
        resizeStartY = e.position.y;
        resizeStartHeight = combatLogElement.resolvedStyle.height;
        resizeHandle.CapturePointer(e.pointerId);
        e.StopPropagation();
    }

    private void OnResizeMove(PointerMoveEvent e)
    {
        if (!isResizing)
            return;
        float delta = e.position.y - resizeStartY;
        combatLogElement.style.height = Mathf.Clamp(
            resizeStartHeight + delta,
            LogMinHeight,
            GetSafeMaxLogHeight()
        );
        e.StopPropagation();
    }

    private void OnResizeEnd(PointerUpEvent e)
    {
        if (!isResizing)
            return;
        isResizing = false;
        resizeHandle.ReleasePointer(e.pointerId);
        PlayerPrefs.SetFloat(LogHeightKey, combatLogElement.resolvedStyle.height);
        e.StopPropagation();
    }

    private float GetSafeMaxLogHeight()
    {
        if (cardHolder == null || combatLogElement == null)
            return LogMaxHeight;
        float available = cardHolder.worldBound.yMin - combatLogElement.worldBound.yMin - 30f;
        return Mathf.Min(LogMaxHeight, Mathf.Max(LogMinHeight, available));
    }

    private void ClampLogHeightToScreen(GeometryChangedEvent e)
    {
        combatLogElement.UnregisterCallback<GeometryChangedEvent>(ClampLogHeightToScreen);
        float safeMax = GetSafeMaxLogHeight();
        float current = combatLogElement.resolvedStyle.height;
        if (current > safeMax)
            combatLogElement.style.height = safeMax;
    }

    [ContextMenu("Reset Combat Log Height")]
    private void ResetLogHeight()
    {
        PlayerPrefs.DeleteKey(LogHeightKey);
        PlayerPrefs.Save();
        if (combatLogElement != null)
            combatLogElement.style.height = LogHeightDefault;
    }

    private void ApplyLogOpacity(float opacity)
    {
        var color = new StyleColor(new Color(86f / 255f, 92f / 255f, 68f / 255f, opacity));
        if (combatLogElement != null)
            combatLogElement.style.backgroundColor = color;
    }

    private void ToggleSpeedBar()
    {
        speedBarVisible = !speedBarVisible;
        if (speedButtonsBox != null)
            speedButtonsBox.style.display = speedBarVisible ? DisplayStyle.Flex : DisplayStyle.None;
        if (speedToggleButton != null)
            speedToggleButton.text = speedBarVisible ? "▲" : "▼";
    }

    private void OnPauseClicked() => ToggleSpeed(0f);

    private void OnSpeed2xClicked() => ToggleSpeed(2f);

    private void OnSpeed3xClicked() => ToggleSpeed(3f);

    private void ToggleSpeed(float speed)
    {
        Time.timeScale = (Time.timeScale == speed) ? 1f : speed;
        UpdateSpeedButtons();
    }

    private void UpdateSpeedButtons()
    {
        SetSpeedActive(pauseButton, Time.timeScale == 0f);
        SetSpeedActive(speed2xButton, Time.timeScale == 2f);
        SetSpeedActive(speed3xButton, Time.timeScale == 3f);
    }

    private void SetSpeedActive(Button btn, bool active)
    {
        if (btn == null)
            return;
        if (active)
            btn.AddToClassList("btn-speed--active");
        else
            btn.RemoveFromClassList("btn-speed--active");
    }

    private void ToggleLog()
    {
        if (logSlideCoroutine != null)
            StopCoroutine(logSlideCoroutine);
        logVisible = !logVisible;
        logToggleButton.text = logVisible ? "▶" : "◀";
        logSlideCoroutine = StartCoroutine(LogSlide(logVisible));
    }

    private IEnumerator LogSlide(bool visible)
    {
        if (combatLogWrapper == null)
            yield break;
        float startX = visible ? LogHiddenPercent : 0f;
        float endX = visible ? 0f : LogHiddenPercent;
        float elapsed = 0f;
        while (elapsed < SlideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / SlideDuration));
            combatLogWrapper.style.translate = new StyleTranslate(
                new Translate(
                    new Length(Mathf.Lerp(startX, endX, t), LengthUnit.Percent),
                    new Length(0, LengthUnit.Pixel)
                )
            );
            yield return null;
        }
        combatLogWrapper.style.translate = new StyleTranslate(
            new Translate(new Length(endX, LengthUnit.Percent), new Length(0, LengthUnit.Pixel))
        );
    }

    private static readonly Color ActionButtonColor = new Color(
        180 / 255f,
        80 / 255f,
        20 / 255f,
        1f
    );
    private static readonly Color MovementButtonColor = new Color(
        30 / 255f,
        60 / 255f,
        140 / 255f,
        1f
    );

    private void SetSelectedButton(Button btn, Color baseColor = default)
    {
        if (selectedActionButton != null)
            selectedActionButton.style.backgroundColor = StyleKeyword.Null;
        selectedActionButton = btn;
        selectedButtonBaseColor = baseColor;
    }

    private void ClearAllRows()
    {
        selectedActionButton = null;
        buttonCostMap.Clear();
        definitionButtonEntries.Clear();
        buttonGrid.Query<VisualElement>(className: "btn-row").ForEach(r => r.RemoveFromHierarchy());
    }

    private Button AddButtonToGrid(string label, string colorClass, System.Action onClick = null)
    {
        var rows = buttonGrid.Query<VisualElement>(className: "btn-row").ToList();
        VisualElement row = null;
        if (rows.Count > 0 && rows[rows.Count - 1].childCount < 2)
            row = rows[rows.Count - 1];

        if (row == null)
        {
            row = new VisualElement();
            row.AddToClassList("btn-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignSelf = Align.Stretch;
            buttonGrid.Add(row);
        }

        Button btn = onClick != null ? new Button(onClick) : new Button();
        btn.name = label.Replace(" ", "") + "Button";
        btn.text = label;
        btn.AddToClassList(colorClass);
        if (label.Length > 10)
            btn.AddToClassList("btn-small-text");
        btn.style.width = new StyleLength(new Length(50, LengthUnit.Percent));
        btn.style.flexGrow = 0;
        row.Add(btn);
        return btn;
    }

    private void AddGeneralButtons()
    {
        endTurnButton.style.width = new StyleLength(new Length(50, LengthUnit.Percent));
        cancelActionButton.style.width = new StyleLength(new Length(50, LengthUnit.Percent));

        var rows = buttonGrid.Query<VisualElement>(className: "btn-row").ToList();
        VisualElement row = null;
        if (rows.Count > 0 && rows[rows.Count - 1].childCount < 2)
            row = rows[rows.Count - 1];

        if (row == null)
        {
            row = new VisualElement();
            row.AddToClassList("btn-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignSelf = Align.Stretch;
            buttonGrid.Add(row);
        }

        row.Add(endTurnButton);

        // Cancel goes in next slot
        rows = buttonGrid.Query<VisualElement>(className: "btn-row").ToList();
        row = rows[rows.Count - 1].childCount < 2 ? rows[rows.Count - 1] : null;
        if (row == null)
        {
            row = new VisualElement();
            row.AddToClassList("btn-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignSelf = Align.Stretch;
            buttonGrid.Add(row);
        }
        row.Add(cancelActionButton);
    }

    private void UpdateHudButtonStates()
    {
        bool actionRunning = isActionRunning();

        foreach (var (btn, cost) in buttonCostMap)
        {
            btn.style.display = DisplayStyle.Flex;
            SetHudButtonEnabled(
                btn,
                currentTurnAC != null && !actionRunning && cost <= currentTurnAC.ActionPoints
            );
        }

        foreach (var (btn, binding) in definitionButtonEntries)
        {
            btn.style.display = DisplayStyle.Flex;
            if (!binding.BelongsTo(currentTurnAC, actionBarTurnGeneration))
            {
                btn.tooltip = string.Empty;
                SetHudButtonEnabled(btn, false);
                continue;
            }

            ActionAvailability availability = binding.Entry.GetAvailability();
            if (availability is AvailableActionAvailability)
            {
                btn.tooltip = string.Empty;
                SetHudButtonEnabled(btn, currentTurnAC != null && !actionRunning);
            }
            else if (availability is UnavailableActionAvailability unavailable)
            {
                btn.tooltip = unavailable.Reason;
                SetHudButtonEnabled(btn, false);
            }
            else
            {
                throw new System.InvalidOperationException(
                    $"Action-bar entry '{binding.Entry.Key}' returned an unknown availability case."
                );
            }
        }

        if (endTurnButton != null)
            SetHudButtonEnabled(endTurnButton, currentTurnAC != null && !actionRunning);

        if (cancelActionButton != null)
        {
            bool definitionCanCancel = CanCancelActiveDefinitionExecution();
            SetHudButtonEnabled(
                cancelActionButton,
                currentTurnAC != null
                    && currentTurnAC.IsTakingAction
                    && (
                        definitionCanCancel
                        || (
                            !(activeDefinitionExecution is ActiveDefinitionExecutionSession)
                            && canCancelAction
                        )
                    )
            );
        }
    }

    private void SetHudButtonEnabled(Button btn, bool enabled)
    {
        if (btn == null)
            return;

        btn.SetEnabled(enabled);
        btn.EnableInClassList(DisabledHudButtonClass, !enabled);

        if (!enabled)
            btn.style.backgroundColor = StyleKeyword.Null;
    }

    private void BuildActionButtons(
        ActionController owner,
        int turnGeneration,
        IReadOnlyList<IActionBarEntry> entries
    )
    {
        ClearAllRows();
        foreach (IActionBarEntry entry in entries)
        {
            Button btn = AddButtonToGrid(entry.DisplayName, "btn-action");
            if (entry is LegacyActionBarEntry legacyEntry)
            {
                buttonCostMap[btn] = legacyEntry.LegacyAction.ActionCost;
                btn.clicked += () =>
                {
                    UniversalEvents.OnCancel.Invoke();
                    SetSelectedButton(btn, ActionButtonColor);
                    legacyEntry.Invoke();
                };
            }
            else if (entry is IDefinitionActionBarEntry definitionEntry)
            {
                DefinitionActionButtonBinding binding = new DefinitionActionButtonBinding(
                    owner,
                    turnGeneration,
                    definitionEntry
                );
                definitionButtonEntries.Add(btn, binding);
                btn.clicked += () => ExecuteDefinitionAction(btn, binding);
            }
            else
            {
                throw new System.InvalidOperationException(
                    $"Action-bar entry '{entry.Key}' has no supported execution contract."
                );
            }
        }
    }

    private async void ExecuteDefinitionAction(Button btn, DefinitionActionButtonBinding binding)
    {
        if (!binding.BelongsTo(currentTurnAC, actionBarTurnGeneration) || isActionRunning())
            return;

        ActionController executingController = binding.Owner;
        UniversalEvents.OnCancel.Invoke();
        SetSelectedButton(btn, ActionButtonColor);
        ActionBarExecutionControl execution = new ActionBarExecutionControl();
        ActiveDefinitionExecutionSession executionSession = new ActiveDefinitionExecutionSession(
            executingController,
            execution
        );
        activeDefinitionExecution = executionSession;
        executingController.IsTakingAction = true;
        UpdateHudButtonStates();

        try
        {
            ActionBarExecutionOutcome outcome = await binding.Entry.Execute(execution);
            ReportDefinitionOutcome(binding.Entry, outcome);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (ReferenceEquals(activeDefinitionExecution, executionSession))
            {
                executingController.IsTakingAction = false;
                activeDefinitionExecution = DefinitionExecutionSession.Inactive;
                if (binding.BelongsTo(currentTurnAC, actionBarTurnGeneration))
                {
                    SetSelectedButton(null);
                    UpdateHudButtonStates();
                }
            }
            execution.Dispose();
        }
    }

    private void ReportDefinitionOutcome(
        IDefinitionActionBarEntry entry,
        ActionBarExecutionOutcome outcome
    )
    {
        if (outcome is DispatchedActionBarExecutionOutcome)
            return;
        if (outcome is CancelledActionBarExecutionOutcome)
        {
            combatLog.Log($"- {entry.DisplayName} selection was canceled.");
            return;
        }
        if (outcome is UnavailableActionBarExecutionOutcome unavailable)
        {
            combatLog.Log($"- {entry.DisplayName}: {unavailable.Reason}");
            return;
        }
        if (outcome is InvalidActionBarExecutionOutcome invalid)
        {
            combatLog.Log($"- {entry.DisplayName}: {invalid.Reason}");
            return;
        }

        throw new System.InvalidOperationException(
            $"Action-bar entry '{entry.Key}' returned an unknown execution outcome."
        );
    }

    private void BuildMovementButtons(GameObject turnTaker, List<EntityAction> movements)
    {
        foreach (EntityAction movement in movements)
        {
            EntityAction captured = movement;
            Button btn = AddButtonToGrid(captured.ActionName, "btn-movement");
            buttonCostMap[btn] = captured.ActionCost;
            btn.clicked += () =>
            {
                UniversalEvents.OnCancel.Invoke();
                SetSelectedButton(btn, MovementButtonColor);
                turnTaker.GetComponent<ActionController>().TakeAction(captured);
            };
        }
        AddGeneralButtons();
    }

    public void EndTurn()
    {
        GameObject g = CombatManager.GetInstance().WhosTurn();
        PlayerActionController pac = g.GetComponent<PlayerActionController>();
        if (pac == null)
            return;
        UniversalEvents.OnCancel.Invoke();
        pac.EndTurn();
    }

    // Commented out for now - ToggleTempHpBar
    // public void ToggleTempHpBar(CreatureComponent cc, ProgressBar bar) {
    //     if(cc != null && cc.tempHp > 0)
    //         bar.style.visibility = Visibility.Visible;
    //     else
    //         bar.style.visibility = Visibility.Hidden;
    // }

    public void CancelAction()
    {
        Debug.Log("here I am");
        if (activeDefinitionExecution is ActiveDefinitionExecutionSession definitionExecution)
        {
            if (definitionExecution.Control.TryCancel())
            {
                UniversalEvents.OnCancel.Invoke();
                GameObject cancellingTurnTaker = CombatManager.GetInstance().WhosTurn();
                combatLog.Log("- " + cancellingTurnTaker.name + " canceled their action.");
            }
            UpdateHudButtonStates();
            return;
        }

        UniversalEvents.OnCancel.Invoke();
        if (currentTurnAC == null)
            return;

        currentTurnAC.IsTakingAction = false;
        canCancelAction = true;
        SetSelectedButton(null);
        UpdateHudButtonStates();
        GameObject currentTurnTaker = CombatManager.GetInstance().WhosTurn();
        combatLog.Log("- " + currentTurnTaker.name + " canceled their action.");
    }

    public void focusOnPlayer(int playerIndex)
    {
        //Debug.Log("focusOnPlayer called");
        //Debug.Log("Focus on player: " + Players[playerIndex]);
    }

    public void getQueuePoisition()
    {
        //Debug.Log("getQueuePoisition called");
        // Need current player index from turn manager
    }

    public bool isActionRunning()
    {
        return activeDefinitionExecution is ActiveDefinitionExecutionSession
            || (currentTurnAC != null && currentTurnAC.IsTakingAction);
    }

    private bool CanCancelActiveDefinitionExecution() =>
        activeDefinitionExecution is ActiveDefinitionExecutionSession execution
        && execution.Control.CanCancel;

    private bool RetirePendingDefinitionExecution()
    {
        if (
            !(activeDefinitionExecution is ActiveDefinitionExecutionSession execution)
            || !execution.Control.TryRetireSelection()
        )
        {
            return false;
        }

        activeDefinitionExecution = DefinitionExecutionSession.Inactive;
        execution.Owner.IsTakingAction = false;
        canCancelAction = true;
        SetSelectedButton(null);
        UniversalEvents.OnCancel.Invoke();
        return true;
    }

    private abstract class DefinitionExecutionSession
    {
        public static DefinitionExecutionSession Inactive { get; } =
            new InactiveDefinitionExecutionSession();

        private sealed class InactiveDefinitionExecutionSession : DefinitionExecutionSession { }
    }

    private sealed class ActiveDefinitionExecutionSession : DefinitionExecutionSession
    {
        public ActiveDefinitionExecutionSession(
            ActionController owner,
            ActionBarExecutionControl control
        )
        {
            Owner = owner;
            Control = control;
        }

        public ActionController Owner { get; }
        public ActionBarExecutionControl Control { get; }
    }

    private sealed class DefinitionActionButtonBinding
    {
        public DefinitionActionButtonBinding(
            ActionController owner,
            int turnGeneration,
            IDefinitionActionBarEntry entry
        )
        {
            Owner = owner;
            TurnGeneration = turnGeneration;
            Entry = entry;
        }

        public ActionController Owner { get; }
        public int TurnGeneration { get; }
        public IDefinitionActionBarEntry Entry { get; }

        public bool BelongsTo(ActionController owner, int turnGeneration) =>
            Owner == owner && TurnGeneration == turnGeneration;
    }

    private void updatePlayerQueueCards()
    {
        if (cardHolder == null || Players == null)
            return;
        CombatManagerInterface cm = CombatManager.GetInstance();
        if (cm == null)
        {
            Debug.LogWarning("CombatManager is null");
            return;
        }
        GameObject turnGO = cm.WhosTurn();
        if (turnGO == null)
        {
            Debug.LogWarning("WhosTurn returned null");
            return;
        }
        CreatureComponent currentTurn = turnGO.GetComponent<CreatureComponent>();
        if (currentTurn == null)
        {
            Debug.LogWarning($"No CreatureComponent on {turnGO.name}");
            return;
        }

        for (int i = 0; i < cardHolder.childCount; i++)
        {
            try
            {
                var card = cardHolder.ElementAt(i);
                CreatureComponent p = Players[i].GetComponent<CreatureComponent>();
                var hbGreen = card.Q<VisualElement>("HealthBarGreen");
                var hbBlue = card.Q<VisualElement>("HealthBarBlue");
                var hbEmpty = card.Q<VisualElement>("HealthBarEmpty");
                var hbLabel = card.Q<Label>("HealthBarLabel");
                int tempHp = p.tempHp;
                int emptyAmount = Mathf.Max(0, p.maxHp - p.hp);
                hbGreen.style.flexGrow = p.hp;
                hbBlue.style.flexGrow = tempHp;
                hbEmpty.style.flexGrow = emptyAmount;
                hbLabel.text = (p.hp + tempHp) + "/" + (p.maxHp + tempHp);

                VisualElement cardVE = card.Q<VisualElement>("Card");
                if (p == currentTurn)
                {
                    // card.style.opacity = 1f;
                    cardVE.RemoveFromClassList("card-inactive");
                }
                else
                {
                    // card.style.opacity = 0.5f;
                    cardVE.AddToClassList("card-inactive");
                }
                UpdateActionPointMedallions(card, p.GetComponent<ActionController>());
                if (p.hp <= 0)
                {
                    continue;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error updating card: {e.Message}\n{e.StackTrace}");
            }
        }
        // Debug.Log("Finished updating player queue cards");
    }

    private void UpdateActionPointMedallions(VisualElement card, ActionController actionController)
    {
        List<VisualElement> medallions = card.Query<VisualElement>(className: ActionMedallionClass)
            .ToList();
        int actionPoints =
            actionController != null
                ? Mathf.Clamp((int)actionController.ActionPoints, 0, MaxActionMedallions)
                : 0;

        for (int i = 0; i < medallions.Count; i++)
        {
            bool filled = i < actionPoints;
            medallions[i].EnableInClassList(ActionMedallionFilledClass, filled);
            medallions[i].EnableInClassList(ActionMedallionEmptyClass, !filled);
        }
    }
}
