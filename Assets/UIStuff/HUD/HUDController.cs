using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class HUDController : MonoBehaviour
{

    public VisualElement ui;
    public Button strikeButton;
    public Button moveButton;
    public Button endTurnButton;

    // Card Variables attempt by Ryan
    private VisualElement cardHolder;
    private VisualTreeAsset playerCardTemplate;
    private bool needToUpdateCards = true;
    //These are just placeholder names for testing, I imagine we'd pull the roster at runtime from turnManager or something
    private string[] players = { "Player 1", "Player 2", "Player 3", "Player 4" };
    private int currentPlayerIndex = 0;
    private ProgressBar healthBar;
    private float elapsedTime = 0f;
    // End of Card Variables

    private void Awake() {
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
        strikeButton = ui.Q<Button>("StrikeButton");
        strikeButton.clicked += Strike;

        moveButton = ui.Q<Button>("MoveButton");
        moveButton.clicked += Move;

        endTurnButton = ui.Q<Button>("EndTurnButton");
        endTurnButton.clicked += EndTurn;

        // Card Logic attempt by Ryan
        cardHolder = ui.Q<VisualElement>("CardHolder");


        healthBar = ui.Q<ProgressBar>("HealthBar");
        healthBar.lowValue = 0f;
        healthBar.highValue = 1f;
        healthBar.value = 0f;
        // End of Card Logic 
    }

    void Update()
    {
        // Card Logic attempt by Ryan
        if (needToUpdateCards) {
            fillPlayerCards();
            needToUpdateCards = false;
        }
        HighlightCurrentPlayerCard(currentPlayerIndex);

        Debug.Log(healthBar.value);

        
        // End of Card Logic

    }

    private float zeroToOneTimer(float duration) {
        while (elapsedTime < duration) {
            Debug.Log("In Timer Loop");
            Debug.Log("Delta Time: " + Time.deltaTime);
            elapsedTime = elapsedTime + Time.deltaTime;
        }
        // if (elapsedTime >= duration) {elapsedTime = 0f;}
        float result = elapsedTime / duration;
        Debug.Log("Elapsed Time: " + elapsedTime);
        Debug.Log("Duration: " + duration);
        Debug.Log("Timer value: " + result);
        return result;
    }

    // Card Logic attempt by Ryan
    private void fillPlayerCards() {
        for (int i = 0; i < players.Length; i++) {
            TemplateContainer cardInstance = playerCardTemplate.Instantiate();
            cardHolder.Add(cardInstance);
            cardInstance.Q<Label>("Card_Name").text = players[i];
            Debug.Log("Added card for " + players[i]);
        }
    }

    private void HighlightCurrentPlayerCard(int playerIndex) {
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
        Debug.Log("Clicked Strike button");
        healthBar.value += 0.1f;
    }

    public void Move() {
        Debug.Log("Clicked Move button");
        healthBar.value -= 0.1f;
    }

    public void EndTurn() {
        Debug.Log("Clicked End Turn button");
        // Card Logic attempt by Ryan
        Debug.Log("Ending turn for " + players[currentPlayerIndex]);
        currentPlayerIndex = (currentPlayerIndex + 1) % players.Length;
        // End of Card Logic
    }
}
