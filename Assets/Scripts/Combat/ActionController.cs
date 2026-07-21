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
    private UnityEncounterRulesBridge encounterRules;
    private CreatureId encounterCreatureId;

    [SerializeField]
    private uint actionPoints;
    private bool reacted;
    private uint strikePenalty;
    public bool IsTakingAction { get; set; } = false;

    /// <summary>Gets whether this controller currently has movement-only exploration authority.</summary>
    public bool IsInDungeonExploration { get; private set; }

    /// <summary>Gets whether combat initiative currently grants this controller turn authority.</summary>
    public bool HasTurnAuthority =>
        encounterRules == null
            ? IsTurn
            : encounterRules.CurrentTurn.HasValue
                && encounterRules.CurrentTurn.Value.Actor == encounterCreatureId;

    public uint ActionPoints
    {
        get =>
            HasAuthoritativeActionState
                ? (uint)encounterRules.GetActionEconomy(encounterCreatureId).ActionsRemaining
                : actionPoints;
        set
        {
            RequirePreEncounterMutation();
            actionPoints = value;
        }
    }
    public bool Reacted
    {
        get =>
            HasAuthoritativeActionState
                ? !encounterRules.GetActionEconomy(encounterCreatureId).ReactionAvailable
                : reacted;
        set
        {
            RequirePreEncounterMutation();
            reacted = value;
        }
    }
    public uint StrikePenalty
    {
        get =>
            HasAuthoritativeActionState
                ? (uint)encounterRules.GetMultipleAttackPenalty(encounterCreatureId).AttackCount
                : strikePenalty;
        set
        {
            RequirePreEncounterMutation();
            strikePenalty = value;
        }
    }

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
        if (!HasAuthoritativeActionState)
        {
            Ref<uint> contribution = new(3);
            ResetActionPointsEvent.Invoke(contribution);
            actionPoints = contribution.Value;
            reacted = false;
            strikePenalty = 0;
        }
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
        IsTurn = false;
        IsTakingAction = false;
        if (!HasAuthoritativeActionState)
        {
            actionPoints = 0;
            reacted = false;
            strikePenalty = 0;
        }
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
        else if (!HasTurnAuthority || action.ActionCost > ActionPoints)
        {
            return;
        }

        IsTakingAction = true;
        action.Invoke(this.gameObject);
    }

    /// <summary>Gets the captured modifier used by the rules runtime's injected d20 roll.</summary>
    public int GetInitiativeModifier()
    {
        return gameObject.GetComponent<CreatureComponent>().GetInitiative();
    }

    internal void AttachEncounterRules(UnityEncounterRulesBridge bridge, CreatureId creatureId)
    {
        encounterRules = bridge ?? throw new System.ArgumentNullException(nameof(bridge));
        encounterCreatureId = creatureId.IsEmpty
            ? throw new System.ArgumentException("A creature ID is required.", nameof(creatureId))
            : creatureId;
    }

    internal uint CalculateTurnStartActions()
    {
        Ref<uint> contribution = new(3);
        ResetActionPointsEvent.Invoke(contribution);
        return contribution.Value;
    }

    public void SpendActions(uint amount)
    {
        if (amount == 0)
            return;

        if (!HasAuthoritativeActionState)
        {
            actionPoints -= amount;
            return;
        }
        encounterRules.SpendActions(encounterCreatureId, checked((int)amount));
    }

    public void IncrementMultipleAttackPenalty()
    {
        if (!HasAuthoritativeActionState)
        {
            strikePenalty++;
            return;
        }
        encounterRules.IncrementMap(encounterCreatureId);
    }

    private void RequirePreEncounterMutation()
    {
        if (HasAuthoritativeActionState)
            throw new System.InvalidOperationException(
                "Attached encounter action state is reducer-owned. Use a narrow rules operation."
            );
    }

    private bool HasAuthoritativeActionState =>
        encounterRules != null
        && encounterRules.Snapshot.ActionEconomy.Contains(encounterCreatureId);

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
