using System.Collections.Generic;
using Game.Combat.Spells;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
using NUnit.Framework;
using UnityEngine;

public abstract class ActionController : MonoBehaviour
{
    // Fields
    protected List<EntityAction> Actions = new();
    protected List<EntityAction> Movements = new();
    protected List<EntityAction> Reactions = new();
    protected bool IsTurn = false;
    private UnityCombatRulesBridge combatRules;
    private CreatureId rulesCreatureId;
    public bool IsTakingAction { get; set; } = false;

    /// <summary>Gets whether this controller currently has movement-only exploration authority.</summary>
    public bool IsInDungeonExploration { get; private set; }

    /// <summary>Gets whether combat initiative currently grants this controller turn authority.</summary>
    public bool HasTurnAuthority => IsTurn;

    [field: SerializeField]
    public uint ActionPoints { get; set; }
    public bool Reacted { get; set; }
    public uint StrikePenalty { get; set; } = 0;

    //Events
    public OnResetActionPoints ResetActionPointsEvent { get; protected set; } = new();
    public OnGetActions GetActionsEvent { get; protected set; } = new();
    public OnGetMovements GetMovementsEvent { get; protected set; } = new();
    public OnGetReactions GetReactionsEvent { get; protected set; } = new();

    [SerializeField]
    List<string> _actionNames = new List<string>(); // Temporary list of action names to add for testing purposes.  TODO remove

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public virtual void StartTurn()
    {
        IsTurn = true;
        Ref<uint> newActionPoints = new(3);
        ResetActionPointsEvent.Invoke(newActionPoints);
        if (combatRules == null)
        {
            ActionPoints = newActionPoints.Value;
        }
        else
        {
            combatRules.BeginTurn(rulesCreatureId, checked((int)newActionPoints.Value));
            SyncActionPointsFromRules();
        }
        StrikePenalty = 0;
        SpellEffectController.ExpireAtStartOfTurn(gameObject);
    }

    /// <summary>
    /// Clears encounter-scoped turn authority and action economy without changing creature,
    /// equipment, position, condition, or effect state.
    /// </summary>
    /// <remarks>
    /// Dungeon retreat and encounter restoration use this boundary so a fresh initiative round
    /// cannot retain actions, reactions, or multiple-attack penalty from the prior round.
    /// </remarks>
    public virtual void ResetEncounterTurnState()
    {
        if (combatRules != null)
            combatRules.EndTurn(rulesCreatureId);
        IsTurn = false;
        IsTakingAction = false;
        ActionPoints = 0;
        Reacted = false;
        StrikePenalty = 0;
    }

    /// <summary>Spends actions through the encounter store when combat rules are attached.</summary>
    /// <param name="amount">The action count paid by a legacy action, including a free action.</param>
    /// <exception cref="System.InvalidOperationException">
    /// The controller cannot afford the requested positive cost.
    /// </exception>
    public void SpendActions(uint amount)
    {
        if (amount == 0)
            return;
        if (amount > ActionPoints)
            throw new System.InvalidOperationException("The controller cannot afford this action.");
        if (combatRules == null)
        {
            ActionPoints -= amount;
            return;
        }
        combatRules.SpendLegacyActions(rulesCreatureId, checked((int)amount));
        SyncActionPointsFromRules();
    }

    /// <summary>Clears authoritative turn state before the scheduler advances.</summary>
    /// <returns>Whether this controller owned an idle turn that could be completed.</returns>
    protected bool TryCompleteTurn()
    {
        if (!IsTurn || IsTakingAction)
            return false;
        if (combatRules != null)
            combatRules.EndTurn(rulesCreatureId);
        ActionPoints = 0;
        IsTurn = false;
        return true;
    }

    internal void AttachCombatRules(UnityCombatRulesBridge bridge, CreatureId creatureId)
    {
        combatRules = bridge ?? throw new System.ArgumentNullException(nameof(bridge));
        if (creatureId.IsEmpty)
            throw new System.ArgumentException(
                "A rules creature ID is required.",
                nameof(creatureId)
            );
        rulesCreatureId = creatureId;
        SyncActionPointsFromRules();
    }

    internal void SyncActionPointsFromRules()
    {
        if (combatRules != null)
            ActionPoints = checked((uint)combatRules.GetActionsRemaining(rulesCreatureId));
    }

    /// <summary>Enables or disables movement-only authority between dungeon encounters.</summary>
    /// <param name="enabled">
    /// Whether the controller may repeatedly invoke its movement actions without initiative or
    /// action-point expenditure.
    /// </param>
    /// <remarks>
    /// Encounter composition disables this mode before initiative grants normal turn authority.
    /// This flag is deliberately independent of combat turn state and action counters; entering or
    /// leaving exploration must not manufacture actions, advance turns, or erase combat state.
    /// Non-movement actions remain unavailable while exploration is enabled.
    /// </remarks>
    public void SetDungeonExploration(bool enabled)
    {
        IsInDungeonExploration = enabled;
    }

    public abstract void EndTurn();

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

    /// <summary>Starts an action when this controller has authority in its current mode.</summary>
    /// <param name="action">The movement or combat action to authorize and invoke.</param>
    /// <remarks>
    /// Concurrent actions are rejected. During dungeon exploration, only registered movement
    /// actions are allowed and action points are ignored; outside exploration, the controller must
    /// own the combat turn and afford the action cost. The invoked action remains responsible for
    /// clearing <see cref="IsTakingAction"/> when its synchronous or coroutine work completes.
    /// </remarks>
    public void TakeAction(EntityAction action)
    {
        if (action == null)
        {
            Debug.LogWarning("No action provided to TakeAction!");
            return;
        }
        //Debug.Log("Attempting to take action: " + action);
        if (IsTakingAction)
            return;

        if (IsInDungeonExploration)
        {
            if (!GetMovements().Contains(action))
                return;
        }
        else if (!IsTurn || action.ActionCost > ActionPoints)
        {
            return;
        }

        IsTakingAction = true;
        action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        int initiativeBonus = this.gameObject.GetComponent<CreatureComponent>().GetInitiative();
        uint roll = (uint)Random.Range(1, 20);
        Debug.Log(
            this.gameObject.name
                + " rolled initiative: "
                + roll
                + " +"
                + initiativeBonus
                + " = "
                + (roll + initiativeBonus)
        );
        roll += (uint)initiativeBonus;
        return roll;
    }

    public void AddAction(EntityAction action)
    {
        Actions.Add(action);
        _actionNames.Add(action.ToString()); // Add action name to the list for testing purposes
    }

    public void RemoveAction(EntityAction action)
    {
        Actions.Remove(action);
    }

    public string GetActionNames() // Temporary method for testing purposes to display available actions in log
    {
        string names = "";
        for (int i = 0; i < Actions.Count; i++)
        {
            names += i + ": " + Actions[i] + "   ";
        }
        return names;
    }
}
