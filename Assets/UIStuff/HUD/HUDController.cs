using NUnit.Framework.Internal;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Game.Creature;
using Game.Strikes;
using System.Collections.Generic;

public class HUDController : SingletonMonoBehaviour<HUDController>
{

    public VisualElement ui;
    public Button endTurnButton;
    public Button nextLevelButton;
    private VisualElement buttonGrid;
    private Toggle autoCameraToggle;
    private bool autoCameraEnabled = true;
    private bool wasFollowing = false;


    //####Player Queue Card Variables####   
    private VisualElement cardHolder; 
    [SerializeField]
    private VisualTreeAsset playerCardTemplate;
    private bool needToUpdateCards = true;
    private bool needToMoveCards = false;
    private int lastPlayerIndex = -1; // Track the last player index to detect changes

    //####Cancel Action Button####
    private Button cancelActionButton;
    
    //####Current Player Variables####
    private VisualElement currentPlayerCard;
    private ProgressBar currentPlayerHealthBar;
    private ProgressBar currentPlayerThpBar;

    //####Target Variables####
    private VisualElement targetCard;
    private ProgressBar targetHealthBar;

    private static List<GameObject> Players;
    private static bool IsActive = false;

    private CombatLogInterface combatLog;
    

    protected override void Awake() {
        base.Awake();
        //Debug.Log("Awake called");
        ui = GetComponent<UIDocument>().rootVisualElement;
        combatLog = CombatLog.GetInstance();
        combatLog.Log("Game Started. Combat Log Initialized.");
        Debug.Log("Listener");
        OnCombatStart.AddListener(() => { EnableUi(); Setup(); });
        OnNextTurn.AddListener(OnTurnChanged);
        //Copiloy made this so I could point it to another UXML file for a template
        //I suspect it sucks
        //vvvvvvvvvvvvvvvvvv
        /**
            * Load the UXML file as a VisualTreeAsset
            * Note: This only works in the Editor. For builds, you would need to use Resources.Load or Addressables.
            */
    }

    private void OnEnable() {
        //Debug.Log("OnEnable called");
        //####Button Setup####
        OnNextLevelRequest.AddListener(ToggleNextLevelButton);

        buttonGrid = ui.Q<VisualElement>("ButtonGrid");

        autoCameraToggle = ui.Q<Toggle>("AutoCameraToggle");
        autoCameraToggle.SetValueWithoutNotify(true);
        autoCameraToggle.RegisterValueChangedCallback(evt =>
        {
            autoCameraEnabled = evt.newValue;
            if (autoCameraEnabled)
            {
                GameObject current = CombatManager.GetInstance().WhosTurn();
                if (current != null)
                    CameraManager.GetInstance().PanToTarget(current, followIndefinitely: true);
            }
        });

        endTurnButton = new Button(EndTurn);
        endTurnButton.name = "EndTurnButton";
        endTurnButton.text = "End Turn";
        endTurnButton.AddToClassList("btn-general");

        cancelActionButton = new Button(CancelAction);
        cancelActionButton.name = "CancelActionButton";
        cancelActionButton.text = "Cancel";
        cancelActionButton.AddToClassList("btn-general");

        nextLevelButton = ui.Q<Button>("NextLevelButton");
        nextLevelButton.clicked += NextLevel;

        // //####Player Queue Card Setup####
        // currentPlayerCard = ui.Q<VisualElement>("CurrentPlayerInfo");
        // currentPlayerHealthBar = currentPlayerCard.Q<ProgressBar>("HealthBar");

        // //####Target Card Setup####
        // targetCard = ui.Q<VisualElement>("TargetInfo");
        // targetHealthBar = targetCard.Q<ProgressBar>("HealthBar");



        //####Player Queue Card Setup####
        cardHolder = ui.Q<VisualElement>("CardHolder");
        // fillPlayerCards(); // Fix: Let Update() handle the initial fill to avoid double execution



    }

    private void OnDisable() {
        //Debug.Log("OnDisable called");
        OnNextLevelRequest.RemoveListener(ToggleNextLevelButton);
        OnNextTurn.RemoveListener(OnTurnChanged);
        nextLevelButton.clicked -= NextLevel;
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
        if (Players == null) {
            Players = currentCombatants;
            needToUpdateCards = true;
        } else if (HaveCombatantsChanged(currentCombatants)) {
            Players.RemoveAll(p => !currentCombatants.Contains(p));
            needToUpdateCards = true;
        }

        if (needToUpdateCards) {
            fillPlayerCards();
            needToUpdateCards = false;
        }

        bool isFollowing = CameraManager.GetInstance().IsFollowing;
        if (autoCameraEnabled && wasFollowing && !isFollowing)
        {
            autoCameraEnabled = false;
            autoCameraToggle.SetValueWithoutNotify(false);
        }
        wasFollowing = isFollowing;

        bool actionRunning = isActionRunning();
        foreach (var child in buttonGrid.Children())
            child.SetEnabled(!actionRunning);

        // Highlight the current player's card
        
        updatePlayerQueueCards();
    }



    // Card Logic attempt by Ryan
    private void fillPlayerCards() {
        cardHolder.Clear(); // Fix: Clear existing cards before adding new ones
        if (Players == null) {
            return;
        }
        for (int i = 0; i < Players.Count; i++) {
            CreatureComponent p = Players[i].GetComponent<CreatureComponent>();
            TemplateContainer cardInstance = playerCardTemplate.Instantiate();
            cardHolder.Add(cardInstance);

            var portraitImage = cardInstance.Q<Image>("PortraitImage");
            // Get portrait snapshot and display it
            Portrait portraitScript = Players[i].GetComponent<Portrait>();
            if (portraitScript != null) {
                Texture2D portraitSnapshot = portraitScript.GetPortraitSnapshot();
                if (portraitSnapshot != null && portraitImage != null) {
                    portraitImage.image = portraitSnapshot;
                }
            }

            cardInstance.Q<Label>("DESC").text = p.description;

            // Pan camera to player when card is clicked
            GameObject captured = Players[i];
            cardInstance.Q<VisualElement>("Card").RegisterCallback<ClickEvent>(evt =>
            {
                CameraManager.GetInstance().PanToTarget(captured);
            });
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


    private void OnTurnChanged(GameObject turnTaker)
    {
        ActionController ac = turnTaker.GetComponent<ActionController>();
        if (ac == null) return;

        // Only generate action buttons for player-controlled creatures
        bool isPlayer = turnTaker.GetComponent<PlayerActionController>() != null;

        if (isPlayer)
        {
            List<EntityAction> actions = ac.GetActions();
            string log = turnTaker.name + " available actions (" + actions.Count + "): ";
            foreach (EntityAction a in actions)
                log += "[" + a.ActionName + "] ";
            Debug.Log(log);
            BuildActionButtons(turnTaker, actions);
            BuildMovementButtons(turnTaker, ac.GetMovements());
        }
        else
        {
            ClearAllRows();
        }

        if (autoCameraEnabled)
            CameraManager.GetInstance().PanToTarget(turnTaker, followIndefinitely: true);
    }

    private void ClearAllRows()
    {
        buttonGrid.Query<VisualElement>(className: "btn-row").ForEach(r => r.RemoveFromHierarchy());
    }

    private void AddButtonToGrid(string label, string colorClass, System.Action onClick = null)
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
        btn.text = label;
        btn.AddToClassList(colorClass);
        if (label.Length > 10)
            btn.AddToClassList("btn-small-text");
        btn.style.width = new StyleLength(new Length(50, LengthUnit.Percent));
        btn.style.flexGrow = 0;
        row.Add(btn);
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

    private void BuildActionButtons(GameObject turnTaker, List<EntityAction> actions)
    {
        ClearAllRows();
        foreach (EntityAction action in actions)
        {
            EntityAction captured = action;
            AddButtonToGrid(captured.ActionName, "btn-action",
                () => turnTaker.GetComponent<ActionController>().TakeAction(captured));
        }
    }

    private void BuildMovementButtons(GameObject turnTaker, List<EntityAction> movements)
    {
        foreach (EntityAction movement in movements)
        {
            EntityAction captured = movement;
            AddButtonToGrid(captured.ActionName, "btn-movement",
                () => turnTaker.GetComponent<ActionController>().TakeAction(captured));
        }
        // DEBUG: test buttons to verify grid layout
        for (int i = 1; i <= 5; i++)
            AddButtonToGrid("Test - " + i, "btn-action");

        AddGeneralButtons();
    }

    public void EndTurn()
    {
        GameObject g = CombatManager.GetInstance().WhosTurn();
        PlayerActionController pac = g.GetComponent<PlayerActionController>();
        if (pac == null) return;
        GridAPI.GetInstance().CancelCurrentAction();
        pac.EndTurn();
        // combatLog.Log("- " + g.name + " ended their turn.");
    }
    
    public void NextLevel() {
        //put logic here to load next level, for now just log it
        
        ToggleNextLevelButton(false);
        combatLog.Log("- Proceeding to next level...");
        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
            SceneManager.LoadScene(nextSceneIndex);
    }

    //used to toggle next level button visibility when player wins combat, can be called from combat manager
    public void ToggleNextLevelButton(bool show) {
        if (nextLevelButton != null) {
            nextLevelButton.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
        }
    }

    // Commented out for now - ToggleTempHpBar
    // public void ToggleTempHpBar(CreatureComponent cc, ProgressBar bar) {
    //     if(cc != null && cc.tempHp > 0)
    //         bar.style.visibility = Visibility.Visible;
    //     else
    //         bar.style.visibility = Visibility.Hidden;
    // }

    public void CancelAction() {
        //Debug.Log("CancelAction called");
        //Debug.Log("Clicked Cancel Action button");
        GridAPI.GetInstance().CancelCurrentAction();
        GameObject g = CombatManager.GetInstance().WhosTurn();
        combatLog.Log("- " + g.name + " canceled their action.");
    }

    public void focusOnPlayer(int playerIndex) {
        //Debug.Log("focusOnPlayer called");
        //Debug.Log("Focus on player: " + Players[playerIndex]);
    }

    public void getQueuePoisition() {
        //Debug.Log("getQueuePoisition called");
        // Need current player index from turn manager
    }

    public bool isActionRunning(){
        // Debug.Log("isActionRunning called");
        // Disable buttons if action is running
        // Placeholder logic, always return false for now
        return false;
    }



    private void updateCurrentPlayerCard() {
        // Debug.Log("updateCurrentPlayerCard called");
        CreatureComponent p = Players[1].GetComponent<CreatureComponent>();
        currentPlayerHealthBar.title = p.name + ": " + p.hp + "/" + p.maxHp;
        currentPlayerHealthBar.value = p.hp;
        currentPlayerHealthBar.highValue = p.maxHp;

        // Update THP bar
        currentPlayerThpBar.title = "THP: " + p.tempHp + "/" + p.tempHp;
        currentPlayerThpBar.value = p.tempHp;
        currentPlayerThpBar.highValue = p.tempHp;
    }

    private void updateTargetCard() {
        // Debug.Log("updateTargetCard called");
        CreatureComponent p = Players[0].GetComponent<CreatureComponent>();
        targetHealthBar.title = p.name + ": " + p.hp + "/" + p.maxHp;
        targetHealthBar.value = p.hp;
        targetHealthBar.highValue = p.maxHp;
    }

    private void updatePlayerQueueCards() {
        for (int i = 0; i < cardHolder.childCount; i++) {
            try {
                // Safe check before calling WhosTurn
                CombatManagerInterface cm = CombatManager.GetInstance();
                if (cm == null) {
                    Debug.LogWarning("CombatManager is null");
                    continue;
                }
                
                GameObject turnGO = cm.WhosTurn();
                if (turnGO == null) {
                    Debug.LogWarning("WhosTurn returned null");
                    continue;
                }
                
                CreatureComponent currentTurn = turnGO.GetComponent<CreatureComponent>();
                if (currentTurn == null) {
                    Debug.LogWarning($"No CreatureComponent on {turnGO.name}");
                    continue;
                }
                
                var card = cardHolder.ElementAt(i);
                CreatureComponent p = Players[i].GetComponent<CreatureComponent>();
                var healthBar = card.Q<ProgressBar>("HealthBar");
                //Debug.Log($"Setting health bar for {p.name}: {p.hp}/{p.maxHp}");
                healthBar.title = p.name + ": " + p.hp + "/" + p.maxHp;
                healthBar.value = p.hp;
                healthBar.highValue = p.maxHp;

                // Commented out for now - TempHp bar
                // var thpBar = card.Q<ProgressBar>("TempHpBar");
                // thpBar.title = "Temp HP: " + p.tempHp + "/" + p.tempHp;
                // thpBar.value = p.tempHp;
                // thpBar.highValue = p.tempHp;
                // ToggleTempHpBar(p, thpBar);

                VisualElement cardVE = card.Q<VisualElement>("Card");
                if (p == currentTurn) {
                    // card.style.opacity = 1f;
                    cardVE.RemoveFromClassList("card-inactive");
                } else {
                    // card.style.opacity = 0.5f;
                    cardVE.AddToClassList("card-inactive");
                }
                if (p.hp <= 0) {
                    needToMoveCards = true; // Flag to move cards if a player is defeated
                    return; // Exit early to avoid updating cards that may be removed
                }
                card.Q<Label>("DESC").text = "AP: " + p.GetComponent<ActionController>().ActionPoints;
                
                
            } catch (System.Exception e) {
                Debug.LogError($"Error updating card: {e.Message}\n{e.StackTrace}");
            }
        }
        // Debug.Log("Finished updating player queue cards");
    }
}
