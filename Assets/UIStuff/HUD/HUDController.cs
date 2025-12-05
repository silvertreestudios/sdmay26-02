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
        GameObject g = CombatManager.GetInstance().WhosTurn();
        // TODO: Check if is player
        g.GetComponent<ActionController>().TestStrike();
        Debug.Log("Clicked Strike button");
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
        GridCharacterController3D.Instance.cancel = true;
        Debug.Log("Clicked End Turn button");
    }
}
