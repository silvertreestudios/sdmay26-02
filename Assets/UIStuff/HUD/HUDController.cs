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
    public Button strikeButton;
    public Button moveButton;
    public Button endTurnButton;
    public Button strikeWeaponButtton; // for testing, will need to be generated based on equipped weapons in the future
    public Button nextLevelButton;


    //####Player Queue Card Variables####   
    private VisualElement cardHolder; 
    private VisualTreeAsset playerCardTemplate;
    private bool needToUpdateCards = true;
    private bool needToMoveCards = false;
    private int lastPlayerIndex = -1; // Track the last player index to detect changes

    //####Cancel Action Button####
    private Button cancelActionButton;
    
    //####Current Player Variables####
    private VisualElement currentPlayerCard;
    private ProgressBar currentPlayerHealthBar;

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
        //Copiloy made this so I could point it to another UXML file for a template
        //I suspect it sucks
        //vvvvvvvvvvvvvvvvvv
        /**
            * Load the UXML file as a VisualTreeAsset
            * Note: This only works in the Editor. For builds, you would need to use Resources.Load or Addressables.
            */
#if UNITY_EDITOR
        playerCardTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UIStuff/HUD/Player_Card_Template.uxml");
        #endif
    }

    private void OnEnable() {
        //Debug.Log("OnEnable called");
        //####Button Setup####
        strikeButton = ui.Q<Button>("StrikeButton");
        strikeButton.clicked += Strike;

        strikeWeaponButtton = ui.Q<Button>("StrikeWeaponButton");
        strikeWeaponButtton.clicked += StrikeWeapon;

        moveButton = ui.Q<Button>("MoveButton");
        moveButton.clicked += Move;

        endTurnButton = ui.Q<Button>("EndTurnButton");
        endTurnButton.clicked += EndTurn;

        cancelActionButton = ui.Q<Button>("CancelActionButton");
        cancelActionButton.clicked += CancelAction;

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
        if (Players == null || HaveCombatantsChanged(currentCombatants)) {
            // Quich reshuffle
            GameObject a = currentCombatants[currentCombatants.Count - 1];
            currentCombatants.Remove(a);
            currentCombatants.Insert(0, a);
            Players = currentCombatants;
            needToUpdateCards = true;
        }

        if (needToUpdateCards) {
            fillPlayerCards();
            needToUpdateCards = false;
        }

        if (isActionRunning()){
            strikeButton.SetEnabled(false);
            moveButton.SetEnabled(false);
            endTurnButton.SetEnabled(false);
        } else {
            strikeButton.SetEnabled(true);
            moveButton.SetEnabled(true);
            endTurnButton.SetEnabled(true);
        }

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

            
        }
    }

    private bool HaveCombatantsChanged(List<GameObject> currentCombatants)
    {
        if (Players == null || currentCombatants == null)
            return true;

        if (Players.Count != currentCombatants.Count)
            return true;

        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i] != currentCombatants[i])
                return true;
        }

        return false;
    }


    public void Strike() {
        //Debug.Log("Strike called");
        GameObject g = CombatManager.GetInstance().WhosTurn();
        // TODO: Check if is player
        g.GetComponent<PlayerActionController>().TestStrike();
        //Debug.Log("Clicked Strike button");
        //for testing, have strike do damage to next player
        // players[(currentPlayerIndex + 1) % players.Length].TakeDamage(10);
    }

    // Testing function for StrikeWeapon action
    public void StrikeWeapon() {
        //Debug.Log("Strike Weapon called");
        GameObject g = CombatManager.GetInstance().WhosTurn();
        List<EntityAction> acs = g.GetComponent<ActionController>().GetActions();
        StrikeWeapon strikeWeaponAction = null;
        // Debug.Log("Available actions for current player: "+ acs.Count);
        foreach (var a in acs)
        {
            Debug.Log("Checking action: " + a);
            if (a is StrikeWeapon)
            {
                // Debug.Log("Found StrikeWeapon action: " + a);
                strikeWeaponAction = (StrikeWeapon)a;
                // break;
            }
        }
        if (strikeWeaponAction == null)
        {
            // Debug.LogWarning("No StrikeWeapon action found for current player!");
            return;
        }
        g.GetComponent<ActionController>().TakeAction(strikeWeaponAction);
        //Debug.Log("Clicked Strike Weapon button");
    }

    public void SetStrikeWeaponText(string weaponName)
    {
        if (strikeWeaponButtton != null && !string.IsNullOrEmpty(weaponName))
        {
            //ui.Q<Button>("StrikeWeaponButton").text = weaponName;
            strikeWeaponButtton.text = weaponName;
        }
        else
        {
            Debug.Log("Here");
            GameObject g = CombatManager.GetInstance().WhosTurn();
            List<EntityAction> acs = g.GetComponent<ActionController>().GetActions();
            StrikeWeapon strikeWeaponAction = null;
            //Debug.Log("Available actions for current player: "+ acs.Count);
            foreach (var a in acs)
            {
                //Debug.Log("Checking action: " + a);
                if (a is StrikeWeapon)
                {
                    //Debug.Log("Found StrikeWeapon action: " + a);
                    strikeWeaponAction = (StrikeWeapon)a;
                    strikeWeaponButtton.text = strikeWeaponAction.GetWeaponName();
                    return;
                }
            }
            strikeWeaponButtton.text = "N/A";
        }
        
    }

    public void Move()
    {
        GameObject g = CombatManager.GetInstance().WhosTurn();
        combatLog.Log("- " + g.name + " is moving.");
        g.GetComponent<PlayerActionController>().TestStride();
        //Debug.Log("Clicked Move button");
    }

    public void EndTurn()
    {
        GameObject g = CombatManager.GetInstance().WhosTurn();
        // TODO: Check if is player
        GridAPI.GetInstance().CancelCurrentAction();
        g.GetComponent<PlayerActionController>().EndTurn();
        combatLog.Log("- " + g.name + " ended their turn.");
    }
    
    public void NextLevel() {
        //put logic here to load next level, for now just log it
        combatLog.Log("- Proceeding to next level...");
    }

    //used to toggle next level button visibility when player wins combat, can be called from combat manager
    public void ToggleNextLevelButton(bool show) {
        if (nextLevelButton != null) {
            nextLevelButton.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
        }
    }

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
                
                if (p == currentTurn) {
                    //card.style.scale = new StyleScale(new Scale(new Vector3(1.5f,1.5f,1))); // Scale up the current player's card
                    card.style.opacity = 1f; // Full opacity for current player card
                    //card.style.borderBottomColor = Color.clear;
                    //card.style.borderBottomWidth = 50;
                } else {
                    //card.style.borderBottomColor = Color.clear;
                    //card.style.borderBottomWidth = 0;
                    //card.style.scale = new StyleScale(new Scale(new Vector3(1f, 1f, 1))); // Normal scale for non-current player cards
                    card.style.opacity = 0.3f; // Dim non-current player cards
                }
                if (p.hp <= 0) {
                    needToMoveCards = true; // Flag to move cards if a player is defeated
                    return; // Exit early to avoid updating cards that may be removed
                }
                //card.Q<Label>("AP").text = "AP: " + p.GetComponent<ActionController>().ActionPoints; // Update action points display
                
                
            } catch (System.Exception e) {
                Debug.LogError($"Error updating card: {e.Message}\n{e.StackTrace}");
            }
        }
        // Debug.Log("Finished updating player queue cards");
    }
}
