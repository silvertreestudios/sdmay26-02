using System.Collections.Generic;
using Game.Creature;
using Game.Strikes;
using NUnit.Framework;
using UnityEngine;

public class PlayerActionController : ActionController
{
    protected void Awake()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);

        AddAction(new RulesStrideAction());
    }

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
        base.StartTurn();

        // Provide Options
        // Prompt user or AI for action
        //Debug.Log("Turn: " + this.gameObject.name);
    }

    // Changed from private to public so actions can call it
    [ContextMenu("End Turn")]
    public override void EndTurn()
    {
        if (TryCompleteTurn())
        {
            Debug.Log("Turn End: " + this.gameObject.name);
            CombatLog.GetInstance().Log("- " + this.gameObject.name + " ended their turn.");
            // Clean up turn state
            // I.E. UI, etc
            CombatManagerInterface.GetInstance().NextTurn();
            OnGameplayStateCommitted.Invoke();
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

        EntityAction stride = Actions.Find(action => action is RulesStrideAction);
        if (stride != null)
        {
            TakeAction(stride);
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
        EntityAction strike = Actions.Find(action => action is RulesStrikeAction);
        if (strike != null)
        {
            //Debug.Log("Invoking Strike action...");
            TakeAction(strike);
        }
    }
}
