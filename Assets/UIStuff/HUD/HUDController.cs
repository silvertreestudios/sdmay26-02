using NUnit.Framework.Internal;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Game.Creature;
using System.Collections.Generic;

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

    public static void Setup()
    {
        Players = CombatManagerInterface.GetInstance().GetCombatants();
        IsActive = true;
    }

    void Update()
    {
        if (!IsActive)
            return;
        // Debug.Log("Update called");
        // Debug.Log("HUD Update called");
        // Update current player card (placeholder logic)
        //updateCurrentPlayerCard();

        // Update target card (placeholder logic)
        //updateTargetCard();

        // Update player queue cards if needed
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
        HighlightCurrentPlayerCard();
        updatePlayerQueueCards();
    }



    // Card Logic attempt by Ryan
    private void fillPlayerCards() {
        cardHolder.Clear(); // Fix: Clear existing cards before adding new ones
        for (int i = 0; i < Players.Count; i++) {
            CreatureComponent p = Players[i].GetComponent<CreatureComponent>();
            TemplateContainer cardInstance = playerCardTemplate.Instantiate();
            cardHolder.Add(cardInstance);
            cardInstance.Q<Label>("Card_Name").text = p.name;
            Debug.Log("Added card for " + Players[i].name);
        }
    }

    private void HighlightCurrentPlayerCard() {
        // Debug.Log("HighlightCurrentPlayerCard called");
        // Logic to highlight the current player's card
        // This is a placeholder implementation
        int playerIndex = CombatManagerInterface.GetInstance().WhosTurn() == Players[0] ? 1 : 0;
        if(playerIndex != lastPlayerIndex) {
            needToMoveCards = true; // Set flag to move cards in the next update
        }
        for (int i = 0; i < cardHolder.childCount; i++) {
            var card = cardHolder.ElementAt(i);
            if (i == playerIndex) {
                // Highlight and scale up the active card
                lastPlayerIndex = playerIndex; // Update last player index
                card.style.scale = new Vector3(1.2f, 1.2f, 1); // Scale up
                if(needToMoveCards){card.BringToFront();} // Ensure the active card is on top}
                needToMoveCards = false;
            } else {
                // Remove highlight and scale down
                
                card.style.scale = new Vector3(0.9f, 0.9f, 1); // Scale down
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
        FSM_API.EndTurn();
        Debug.Log("Clicked End Turn button");

    }

    public void CancelAction() {
        Debug.Log("CancelAction called");
        Debug.Log("Clicked Cancel Action button");
        FSM_API.CancelCurrentAction();
    }

    public void focusOnPlayer(int playerIndex) {
        Debug.Log("focusOnPlayer called");
        Debug.Log("Focus on player: " + Players[playerIndex]);
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
        Debug.Log("updatePlayerQueueCards called");
        // Logic to update player queue cards
        // This is a placeholder implementation
        for (int i = 0; i < cardHolder.childCount; i++) {
            var card = cardHolder.ElementAt(i);
            CreatureComponent p = Players[i].GetComponent<CreatureComponent>();
            var healthBar = card.Q<ProgressBar>("HealthBar");
            healthBar.title = p.name + ": " + p.hp + "/" + p.maxHp;
            healthBar.value = p.hp;
            healthBar.highValue = p.maxHp;
        }
    }
}