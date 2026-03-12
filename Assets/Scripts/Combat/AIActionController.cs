using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;
using System;
using Game.Strikes;

//TODO abstract AIActionConroller and make a subclass for mindless
public abstract class AIActionController : ActionController
{
    protected GridCharacterController3D Controller => GridCharacterController3D.GetInstance();
    public GameObject bestTarget { get; protected set; }
    public List<Vector3Int> bestPath { get; protected set; }
    public Vector3Int selectedTile { get; protected set; }
    protected void Awake()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);

        Stride strideAction = new Stride(1); // Cost of 1 action point
        Movements.Add(strideAction);
    }

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
        base.StartTurn();

    }

    // Changed from private to public so actions can call it
    [ContextMenu("End Turn")]
    public override void EndTurn()
    {
        if (IsTurn && !IsTakingAction)
        {
            IsTurn = false;
            Debug.Log("Turn End: " + this.gameObject.name);
            // Clean up turn state
            // I.E. UI, etc
            CombatManagerInterface.GetInstance().NextTurn();
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

}