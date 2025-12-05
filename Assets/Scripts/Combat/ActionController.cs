using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;

public class ActionController : MonoBehaviour
{
    [SerializeField]
    protected List<EntityAction> Actions = new List<EntityAction>();
    [SerializeField]
    protected List<EntityAction> Movements = new List<EntityAction>();
    private bool IsTurn = false;
    private uint ActionPoints;

    protected void Start()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);
        
        Stride strideAction = new Stride(1); // Cost of 1 action point
        Movements.Add(strideAction);

        List<Dice> dices = new() { new Dice(1, 3, "Bludgening") };

        Unarmed unarmed = new Unarmed(1, dices, new());
        Actions.Add(unarmed);
    }

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public void StartTurn()
    {
        IsTurn = true;
        ActionPoints = 3;
        
        // Provide Options
        // Prompt user or AI for action
        Debug.Log("Turn: " + this.gameObject.name);
    }

    // Changed from private to public so actions can call it
    [ContextMenu("End Turn")]
    public void EndTurn()
    {
        if (IsTurn)
        {
            IsTurn = false;
            Debug.Log("Turn End: " + this.gameObject.name);
            // Clean up turn state
            // I.E. UI, etc
            CombatManagerInterface.GetInstance().NextTurn();
        }
        else
        {
            Debug.LogWarning("Cannot end turn - it's not this character's turn!");
        }
    }

    /// <summary>
    /// Temporary function to invoke the Stride movement action for testing.
    /// Can be triggered from the Unity editor during runtime via right-click menu.
    /// </summary>
    [ContextMenu("Test Invoke Stride")]
    public void TestStride()
    {
        if (!IsTurn)
        {
            Debug.LogWarning("Cannot use Stride - it's not this character's turn!");
            return;
        }

        if (Movements.Count > 0)
        {
            Debug.Log("Invoking Stride action...");
            TakeAction(Movements[0]);
        }
        else
        {
            Debug.LogWarning("Stride action is not initialized!");
        }
    }

    /// <summary>
    /// Temporary function to invoke the strike action for testing.
    /// Can be triggered from the Unity editor during runtime via right-click menu.
    /// </summary>
    [ContextMenu("Test Strike")]
    public void TestStrike()
    {
        if (Actions.Count > 0)
        {
            Debug.Log("Invoking Strike action...");
            TakeAction(Actions[0]);
        }
    }

    public void TakeAction(EntityAction action)
    {
        uint cost = action.Cost();
        if (!IsTurn || cost > ActionPoints)
            return;
        ActionPoints -= action.Cost();
        action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        return (uint)Random.Range(1, 20);
    }
}
