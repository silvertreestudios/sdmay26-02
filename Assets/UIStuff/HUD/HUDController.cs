using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class HUDController : MonoBehaviour
{

    public VisualElement ui;
    public Button strikeButton;
    public Button moveButton;
    public Button endTurnButton;

    private void Awake() {
        ui = GetComponent<UIDocument>().rootVisualElement;
    }

    private void OnEnable() {
        strikeButton = ui.Q<Button>("StrikeButton");
        strikeButton.clicked += Strike;

        moveButton = ui.Q<Button>("MoveButton");
        moveButton.clicked += Move;

        endTurnButton = ui.Q<Button>("EndTurnButton");
        endTurnButton.clicked += EndTurn;
    }

    public void Strike() {
        Debug.Log("Clicked Strike button");
    }

    public void Move() {
        Debug.Log("Clicked Move button");
    }

    public void EndTurn() {
        Debug.Log("Clicked End Turn button");
    }
}
