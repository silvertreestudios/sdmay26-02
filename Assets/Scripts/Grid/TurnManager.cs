using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple turn manager for alternating player turns
/// singleton pattern for global access
/// </summary>
public class TurnManager : SingletonMonoBehaviour<TurnManager>
{
    // trigger when new player turn begins/ends
    public event Action<string> OnTurnStarted;
    public event Action<string> OnTurnEnded;

    // store characters in a circular order (i.e player -> enemy -> player -> enemy ...)
    private LinkedList<string> turnOrder = new LinkedList<string>();
    private LinkedListNode<string> currentTurnNode;

    // current state
    private string currentCharacter;
    // input lock state (locked during movement/animation)
    private bool isInputLocked = false;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    /// <summary>
    /// initialize the turn order with character names
    /// </summary>
    public void InitializeTurnOrder(params string[] characterNames)
    {
        turnOrder.Clear();

        // add each character to the turn order
        foreach (string name in characterNames)
        {
            turnOrder.AddLast(name);
        }

        // if there's at least one character, set the first as the current turn
        if (turnOrder.Count > 0)
        {
            currentTurnNode = turnOrder.First;
            currentCharacter = currentTurnNode.Value;

            if (showDebugInfo)
                Debug.Log($"[TurnManager] {currentCharacter}'s turn started");

            OnTurnStarted?.Invoke(currentCharacter);
        }
    }

    /// <summary>
    /// returns the current active character
    /// </summary>
    public string GetCurrentCharacter() => currentCharacter;

    /// <summary>
    /// checks if it's a specific character's turn and input is allowed
    /// </summary>
    public bool IsCharacterTurn(string characterName)
        => currentCharacter == characterName && !isInputLocked;

    /// <summary>
    /// lock input (called when movement starts)
    /// </summary>
    public void LockInput() => isInputLocked = true;

    /// <summary>
    /// end current turn and advance to next character
    /// </summary>
    public void EndTurn()
    {
        string previousCharacter = currentCharacter;

        OnTurnEnded?.Invoke(previousCharacter);

        // move to next node in circular list
        currentTurnNode = currentTurnNode.Next ?? turnOrder.First;
        currentCharacter = currentTurnNode.Value;

        // unlock input for new character
        isInputLocked = false;

        if (showDebugInfo)
            Debug.Log($"[TurnManager] Turn ended. Switched from {previousCharacter} to {currentCharacter}");

        OnTurnStarted?.Invoke(currentCharacter);
    }

    private void OnGUI()
    {
        if (!showDebugInfo) return;

        // animate panel with smooth vertical bobbing
        float t = Time.time;
        float pulse = Mathf.Sin(t * 2f) * 0.5f + 0.5f; // oscillates between 0–1

        // save old GUI color
        Color textColor = GUI.color;

        // background rectangle with alpha transparency
        float width = 260f;
        float height = 110f;
        float yOffset = 10f + Mathf.Sin(t * 2f) * 3f; // floating animation
        GUI.Box(new Rect(15, yOffset, width, height), GUIContent.none);


        // draw info text with slight transparency
        GUIStyle infoStyle = new GUIStyle(GUI.skin.label)
        {
            // 
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = textColor }
        };
        float contentY = yOffset + 35f;
        GUI.Label(new Rect(30, contentY, width - 20, 20), $"Active Character: <b>{currentCharacter} </b>", infoStyle);
        GUI.Label(new Rect(30, contentY + 22, width - 20, 20), $"Input Locked:  {isInputLocked}", infoStyle);
    }
}
