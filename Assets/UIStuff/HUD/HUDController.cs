using NUnit.Framework.Internal;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Game.Creature;

public class HUDController : MonoBehaviour
{

    public VisualElement ui;
    public Button strikeButton;
    public Button moveButton;
    public Button endTurnButton;


    //####Player Queue Card Variables####   
    private VisualElement cardHolder; 
    private VisualTreeAsset playerCardTemplate;
    private bool needToUpdateCards = true;
    //These are just placeholder names for testing, I imagine we'd pull the roster at runtime from turnManager or something
    private TESTPlayerCard[] players;
    private int currentPlayerIndex = 0;

    //####Cancel Action Button####
    private Button cancelActionButton;
    
    //####Current Player Variables####
    private VisualElement currentPlayerCard;
    private ProgressBar currentPlayerHealthBar;

    //####Target Variables####
    private VisualElement targetCard;
    private ProgressBar targetHealthBar;
    

    private void Awake() {
        Debug.Log("Awake called");
        ui = GetComponent<UIDocument>().rootVisualElement;

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
        Debug.Log("OnEnable called");
        //####Button Setup####
        strikeButton = ui.Q<Button>("StrikeButton");
        strikeButton.clicked += Strike;

        moveButton = ui.Q<Button>("MoveButton");
        moveButton.clicked += Move;

        endTurnButton = ui.Q<Button>("EndTurnButton");
        endTurnButton.clicked += EndTurn;

        cancelActionButton = ui.Q<Button>("CancelActionButton");
        cancelActionButton.clicked += CancelAction;

        //####Player Queue Card Setup####
        currentPlayerCard = ui.Q<VisualElement>("CurrentPlayerInfo");
        currentPlayerHealthBar = currentPlayerCard.Q<ProgressBar>("HealthBar");

        //####Target Card Setup####
        targetCard = ui.Q<VisualElement>("TargetInfo");
        targetHealthBar = targetCard.Q<ProgressBar>("HealthBar");

        //####Test Player Cards Setup####
        players = new TESTPlayerCard[2];
        BuildTestCards();

        //####Player Queue Card Setup####
        cardHolder = ui.Q<VisualElement>("CardHolder");
        // fillPlayerCards(); // Fix: Let Update() handle the initial fill to avoid double execution
    }

    void Update()
    {
        // Debug.Log("Update called");
        // Debug.Log("HUD Update called");
        // Update current player card (placeholder logic)
        updateCurrentPlayerCard();

        // Update target card (placeholder logic)
        updateTargetCard(players[(currentPlayerIndex + 1) % players.Length]);

        // Update player queue cards if needed
        if (needToUpdateCards) {
            // cardHolder = ui.Q<VisualElement>("PlayerQueueCardHolder");
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
        HighlightCurrentPlayerCard(currentPlayerIndex);
    }



    // Card Logic attempt by Ryan
    private void fillPlayerCards() {
        Debug.Log("fillPlayerCards called");
        cardHolder.Clear(); // Fix: Clear existing cards before adding new ones
        for (int i = 0; i < players.Length; i++) {
            TemplateContainer cardInstance = playerCardTemplate.Instantiate();
            cardHolder.Add(cardInstance);
            cardInstance.Q<Label>("Card_Name").text = players[i].playerName;
            Debug.Log("Added card for " + players[i].playerName);
        }
    }

    private void HighlightCurrentPlayerCard(int playerIndex) {
        // Debug.Log("HighlightCurrentPlayerCard called");
        // Logic to highlight the current player's card
        // This is a placeholder implementation
        for (int i = 0; i < cardHolder.childCount; i++) {
            var card = cardHolder.ElementAt(i);
            if (i == playerIndex) {
                //another coPilot idea vvv , works for intial testing
                card.style.borderBottomColor = Color.yellow;
                card.style.borderBottomWidth = 4;
            } else {
                card.style.borderBottomWidth = 0;
            }
        }
    }
    // End of Card Logic

    public void Strike() {
        Debug.Log("Strike called");
        GameObject g = CombatManager.GetInstance().WhosTurn();
        // TODO: Check if is player
        g.GetComponent<ActionController>().TestStrike();
        Debug.Log("Clicked Strike button");
        //for testing, have strike do damage to next player
        // players[(currentPlayerIndex + 1) % players.Length].TakeDamage(10);
    }

    public void Move()
    {
        GameObject g = CombatManager.GetInstance().WhosTurn();
        // TODO: Check if is player
        g.GetComponent<ActionController>().TestStride();
        Debug.Log("Clicked Move button");
    }

    public void EndTurn()
    {
        GameObject g = CombatManager.GetInstance().WhosTurn();
        // TODO: Check if is player
        g.GetComponent<ActionController>().EndTurn();
        Debug.Log("Clicked End Turn button");
        //for testing, advance to next player
        NextPlayerTurn();
    }

    public void CancelAction() {
        Debug.Log("CancelAction called");
        Debug.Log("Clicked Cancel Action button");
        GridCharacterController3D.Instance.cancel = true;
    }

    public void focusOnPlayer(int playerIndex) {
        Debug.Log("focusOnPlayer called");
        Debug.Log("Focus on player: " + players[playerIndex]);
    }

    public void getQueuePoisition() {
        Debug.Log("getQueuePoisition called");
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
        CreatureComponent p1 = CombatManagerInterface.GetInstance().WhosTurn().GetComponent<CreatureComponent>();
        currentPlayerHealthBar.title = p1.name;
        currentPlayerHealthBar.value = p1.hp;
        currentPlayerHealthBar.highValue = p1.maxHp;
    }

    private void updateTargetCard(TESTPlayerCard targetPlayer) {
        // Debug.Log("updateTargetCard called");
        GameObject p1 = CombatManagerInterface.GetInstance().WhosTurn();
        CreatureComponent p2 = CombatManagerInterface.GetInstance().GetTarget(p1).GetComponent<CreatureComponent>();
        targetHealthBar.title = p2.name;
        targetHealthBar.value = p2.hp;
        targetHealthBar.highValue = p2.maxHp;
    }

    private void NextPlayerTurn() {
        Debug.Log("NextPlayerTurn called");
        currentPlayerIndex = (currentPlayerIndex + 1) % players.Length;
        needToUpdateCards = true;
    }

    private void PreviousPlayerTurn() {
        Debug.Log("PreviousPlayerTurn called");
        currentPlayerIndex = (currentPlayerIndex - 1 + players.Length) % players.Length;
        needToUpdateCards = true;
    }

    private void BuildTestCards() {
        Debug.Log("BuildTestCards called");
        for (int i = 0; i < players.Length; i++) {
            players[i] = new TESTPlayerCard("Player " + (i + 1), 100);
        }
        for (int i = 0; i < players.Length; i++) {
            Debug.Log("Created test card for " + players[i].playerName);
        }
    }
}

public class TESTPlayerCard {
    public string playerName;
    public int health;
    public int maxHealth;

    public TESTPlayerCard(string name, int maxHp) {
        Debug.Log("TESTPlayerCard constructor called");
        playerName = name;
        maxHealth = maxHp;
        health = maxHp;
    }

    public void TakeDamage(int damage) {
        Debug.Log("TakeDamage called");
        health -= damage;
        if (health < 0) health = 0;
    }

    public string getName() {
        Debug.Log("getName called");
        return playerName;
    }

    public int getMaxHealth() {
        // Debug.Log("getMaxHealth called");
        return maxHealth;
    }
    
    public int getHealth() {
        // Debug.Log("getHealth called");
        return health;
    }
}