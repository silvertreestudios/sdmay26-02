using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;

public class ActionController : MonoBehaviour
{
    // Fields
    protected List<EntityAction> Actions = new();
    protected List<EntityAction> Movements = new();
    protected List<EntityAction> Reactions = new();
    protected bool IsTurn = false;
    public bool IsTakingAction { get; set; } = false;
    [field: SerializeField]
    public uint ActionPoints { get; set; }
    public bool Reacted { get; set; }

    //Events
    public OnResetActionPoints ResetActionPointsEvent { get; protected set; } = new();
    public OnGetActions GetActionsEvent { get; protected set; } = new();
    public OnGetMovements GetMovementsEvent { get; protected set; } = new();
    public OnGetReactions GetReactionsEvent { get; protected set; } = new();

    protected void Awake()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);
        
        Stride strideAction = new Stride(1); // Cost of 1 action point
        Movements.Add(strideAction);

        List<Dice> dices = new() { new Dice(1, 3, "Bludgeoning") };

        Unarmed unarmed = new Unarmed(1, dices, new());
        Actions.Add(unarmed);
    }

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public void StartTurn()
    {
        IsTurn = true;
        Ref<uint> newActionPoints = new(3);
        ResetActionPointsEvent.Invoke(newActionPoints);
        ActionPoints = newActionPoints.Value;
        
        // Provide Options
        // Prompt user or AI for action
        Debug.Log("Turn: " + this.gameObject.name);
    }

    // Changed from private to public so actions can call it
    [ContextMenu("End Turn")]
    public void EndTurn()
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

    /// <summary>
    /// Returns a copied list of all actions the controller can perform, excluding movements
    /// </summary>
    /// <returns></returns>
    public List<EntityAction> GetActions()
    {
        List<EntityAction> available = new(Actions);
        GetActionsEvent.Invoke(available);
        return available;
    }

    /// <summary>
    /// Returns a copied list of all movements the controller can perform
    /// </summary>
    /// <returns></returns>
    public List<EntityAction> GetMovements()
    {
        List<EntityAction> available = new(Movements);
        GetMovementsEvent.Invoke(available);
        return available;
    }

    /// <summary>
    /// Returns a copied list of all reactions the controller can perform
    /// </summary>
    /// <returns></returns>
    public List<EntityAction> GetReactions()
    {
        List<EntityAction> available = new(Reactions);
        GetReactionsEvent.Invoke(available);
        return available;
    }

    /// <summary>
    /// Performs a given action for this controller
    /// </summary>
    /// <param name="action"></param>
    public void TakeAction(EntityAction action)
    {
        uint cost = action.ActionCost;
        if (!IsTurn || cost > ActionPoints)
            return;
        IsTakingAction = true;
        action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        return (uint)Random.Range(1, 20);
    }
}