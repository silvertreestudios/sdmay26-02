using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

public abstract class ActionController : MonoBehaviour
{
    // Fields
    protected List<EntityAction> Actions = new();
    protected List<EntityAction> Movements = new();
    protected List<EntityAction> Reactions = new();
    private bool hasStandaloneTurnAuthority;
    private UnityEncounterRulesBridge encounterRules;
    private CreatureId encounterCreatureId;
    private bool isTakingAction;
    private long actionReservationGeneration;

    [SerializeField]
    private uint actionPoints;
    private bool reacted;
    private uint strikePenalty;
    private readonly List<UnityAction<Ref<uint>>> managedActionResetListeners = new();
    private readonly List<UnityAction<List<EntityAction>>> managedReactionListeners = new();

    /// <summary>Gets or sets whether an action lifecycle currently reserves this controller.</summary>
    /// <remarks>
    /// Each false-to-true transition creates an exact reservation generation. Encounter host
    /// completion uses the matching settlement notification so a lethal action can finish all of
    /// its post-damage work before Unity publishes the committed outcome.
    /// </remarks>
    public bool IsTakingAction
    {
        get => isTakingAction;
        set
        {
            if (isTakingAction == value)
                return;
            if (value)
            {
                actionReservationGeneration = checked(actionReservationGeneration + 1);
                isTakingAction = true;
                return;
            }

            long settledGeneration = actionReservationGeneration;
            isTakingAction = false;
            ActionReservationSettled.Invoke(this, settledGeneration);
        }
    }

    // This is deliberately internal: only the encounter host may defer committed completion on an
    // exact Unity action reservation. Rules state remains reducer-owned.
    internal event Action<ActionController, long> ActionReservationSettled = delegate { };

    /// <summary>Gets whether this controller currently has movement-only exploration authority.</summary>
    public bool IsInDungeonExploration { get; private set; }

    /// <summary>Gets whether combat initiative currently grants this controller turn authority.</summary>
    /// <remarks>
    /// Before encounter attachment, direct controller fixtures may establish standalone authority
    /// through <see cref="StartTurn"/>. Attached controllers always project the reducer-owned turn.
    /// </remarks>
    public bool HasTurnAuthority =>
        encounterRules == null
            ? hasStandaloneTurnAuthority
            : encounterRules.CurrentTurn.HasValue
                && encounterRules.CurrentTurn.Value.Actor == encounterCreatureId;

    /// <summary>
    /// Gets reducer-owned actions while attached, or the serialized setup value before attachment.
    /// </summary>
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

    /// <summary>
    /// Gets the inverse of authoritative reaction availability while attached, or setup state before attachment.
    /// </summary>
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

    /// <summary>
    /// Gets reducer-owned turn attack count while attached, or the setup value before attachment.
    /// </summary>
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
        if (!HasAuthoritativeActionState)
        {
            hasStandaloneTurnAuthority = true;
            Ref<uint> contribution = new(3);
            ResetActionPointsEvent.Invoke(contribution);
            actionPoints = contribution.Value;
            reacted = false;
            strikePenalty = 0;
        }
        if (!HasAuthoritativeActionState)
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
        ResetEncounterTurnState(preserveActionReservation: false);
    }

    // TurnEnded can commit while the action that caused it still has post-damage work. The host
    // clears reducer-projected turn resources immediately but leaves that exact Unity reservation
    // for its owning action lifecycle to release.
    internal void ResetEncounterTurnState(bool preserveActionReservation)
    {
        if (!preserveActionReservation)
            IsTakingAction = false;
        if (!HasAuthoritativeActionState)
        {
            hasStandaloneTurnAuthority = false;
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

    /// <summary>Requests completion of this controller's exact authoritative turn.</summary>
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
        hasStandaloneTurnAuthority = false;
    }

    internal bool TryGetCurrentActionReservation(out long generation)
    {
        generation = actionReservationGeneration;
        return isTakingAction;
    }

    // Startup hooks may add actions and managed rule listeners before their awaited work fails.
    // Preserve the pre-attachment state so CombatManager can retry without duplicated passives.
    internal ActionControllerEncounterState CaptureEncounterStartupState() =>
        new ActionControllerEncounterState(
            Actions.ToArray(),
            Movements.ToArray(),
            Reactions.ToArray(),
            _actionNames.ToArray(),
            hasStandaloneTurnAuthority,
            encounterRules,
            encounterCreatureId,
            actionPoints,
            reacted,
            strikePenalty,
            IsTakingAction,
            IsInDungeonExploration,
            enabled,
            managedActionResetListeners.Count,
            managedReactionListeners.Count
        );

    internal void RestoreEncounterStartupState(ActionControllerEncounterState state)
    {
        // Combat-start rule hooks only append through these managed seams. Removing back to the
        // captured counts preserves unrelated listeners that predated the transaction.
        while (managedActionResetListeners.Count > state.ManagedActionResetListenerCount)
        {
            int last = managedActionResetListeners.Count - 1;
            ResetActionPointsEvent.RemoveListener(managedActionResetListeners[last]);
            managedActionResetListeners.RemoveAt(last);
        }
        while (managedReactionListeners.Count > state.ManagedReactionListenerCount)
        {
            int last = managedReactionListeners.Count - 1;
            GetReactionsEvent.RemoveListener(managedReactionListeners[last]);
            managedReactionListeners.RemoveAt(last);
        }

        Actions.Clear();
        Actions.AddRange(state.Actions);
        Movements.Clear();
        Movements.AddRange(state.Movements);
        Reactions.Clear();
        Reactions.AddRange(state.Reactions);
        _actionNames.Clear();
        _actionNames.AddRange(state.ActionNames);
        hasStandaloneTurnAuthority = state.HasStandaloneTurnAuthority;
        encounterRules = state.EncounterRules;
        encounterCreatureId = state.EncounterCreatureId;
        actionPoints = state.ActionPoints;
        reacted = state.Reacted;
        strikePenalty = state.StrikePenalty;
        IsTakingAction = state.IsTakingAction;
        IsInDungeonExploration = state.IsInDungeonExploration;
        enabled = state.Enabled;
    }

    internal void AddRuleActionResetListener(UnityAction<Ref<uint>> listener)
    {
        ResetActionPointsEvent.AddListener(listener);
        managedActionResetListeners.Add(listener);
    }

    internal void AddRuleReactionListener(UnityAction<List<EntityAction>> listener)
    {
        GetReactionsEvent.AddListener(listener);
        managedReactionListeners.Add(listener);
    }

    internal uint CalculateTurnStartActions()
    {
        Ref<uint> contribution = new(3);
        ResetActionPointsEvent.Invoke(contribution);
        return contribution.Value;
    }

    /// <summary>Spends actions through the shared store, or setup state before attachment.</summary>
    /// <param name="amount">The non-negative action count to spend.</param>
    public async ValueTask SpendActionsAsync(uint amount)
    {
        if (amount == 0)
            return;

        if (!HasAuthoritativeActionState)
        {
            actionPoints -= amount;
            return;
        }
        await encounterRules.SpendActionsAsync(encounterCreatureId, checked((int)amount));
    }

    /// <summary>Increments turn-scoped MAP through the shared encounter store.</summary>
    public async ValueTask IncrementMultipleAttackPenaltyAsync()
    {
        if (!HasAuthoritativeActionState)
        {
            strikePenalty++;
            return;
        }
        await encounterRules.IncrementMapAsync(encounterCreatureId);
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

internal sealed class ActionControllerEncounterState
{
    internal ActionControllerEncounterState(
        EntityAction[] actions,
        EntityAction[] movements,
        EntityAction[] reactions,
        string[] actionNames,
        bool hasStandaloneTurnAuthority,
        UnityEncounterRulesBridge encounterRules,
        CreatureId encounterCreatureId,
        uint actionPoints,
        bool reacted,
        uint strikePenalty,
        bool isTakingAction,
        bool isInDungeonExploration,
        bool enabled,
        int managedActionResetListenerCount,
        int managedReactionListenerCount
    )
    {
        Actions = actions;
        Movements = movements;
        Reactions = reactions;
        ActionNames = actionNames;
        HasStandaloneTurnAuthority = hasStandaloneTurnAuthority;
        EncounterRules = encounterRules;
        EncounterCreatureId = encounterCreatureId;
        ActionPoints = actionPoints;
        Reacted = reacted;
        StrikePenalty = strikePenalty;
        IsTakingAction = isTakingAction;
        IsInDungeonExploration = isInDungeonExploration;
        Enabled = enabled;
        ManagedActionResetListenerCount = managedActionResetListenerCount;
        ManagedReactionListenerCount = managedReactionListenerCount;
    }

    internal EntityAction[] Actions { get; }
    internal EntityAction[] Movements { get; }
    internal EntityAction[] Reactions { get; }
    internal string[] ActionNames { get; }
    internal bool HasStandaloneTurnAuthority { get; }
    internal UnityEncounterRulesBridge EncounterRules { get; }
    internal CreatureId EncounterCreatureId { get; }
    internal uint ActionPoints { get; }
    internal bool Reacted { get; }
    internal uint StrikePenalty { get; }
    internal bool IsTakingAction { get; }
    internal bool IsInDungeonExploration { get; }
    internal bool Enabled { get; }
    internal int ManagedActionResetListenerCount { get; }
    internal int ManagedReactionListenerCount { get; }
}
