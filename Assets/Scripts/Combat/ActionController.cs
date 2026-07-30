using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
using UnityEngine;

public abstract class ActionController : MonoBehaviour
{
    // Fields
    protected List<EntityAction> Actions = new();
    protected List<EntityAction> Reactions = new();
    private UnityCombatRulesBridge combatRules;
    private CreatureId rulesCreatureId;
    public bool IsTakingAction { get; set; } = false;

    /// <summary>Gets whether this controller currently has movement-only exploration authority.</summary>
    public bool IsInDungeonExploration { get; private set; }

    /// <summary>Gets whether combat initiative currently grants this controller turn authority.</summary>
    public bool HasTurnAuthority
    {
        get
        {
            if (!TryGetAttachedCombatRules(out _, out _))
                return false;
            return combatRules.HasTurnAuthority(rulesCreatureId);
        }
    }

    /// <summary>Gets authoritative remaining actions while attached to encounter rules.</summary>
    public uint ActionPoints =>
        TryGetAttachedCombatRules(out UnityCombatRulesBridge bridge, out CreatureId actor)
            ? checked((uint)bridge.GetActionsRemaining(actor))
            : 0;

    /// <summary>Gets whether the current encounter reaction has been spent.</summary>
    public bool Reacted =>
        TryGetAttachedCombatRules(out UnityCombatRulesBridge bridge, out CreatureId actor)
        && !bridge.GetActionEconomy(actor).ReactionAvailable;

    /// <summary>
    /// Gets the rules-owned number of prior attacks for this controller's current turn.
    /// </summary>
    public uint StrikePenalty =>
        TryGetAttachedCombatRules(out UnityCombatRulesBridge bridge, out CreatureId actor)
            ? checked((uint)bridge.GetMultipleAttackPenalty(actor).AttackCount)
            : 0;

    //Events
    public OnResetActionPoints ResetActionPointsEvent { get; protected set; } = new();
    public OnGetActions GetActionsEvent { get; protected set; } = new();
    public OnGetReactions GetReactionsEvent { get; protected set; } = new();

    [SerializeField]
    List<string> _actionNames = new List<string>(); // Temporary list of action names to add for testing purposes.  TODO remove

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public virtual void StartTurn()
    {
        RequireCombatRules();
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
        IsTakingAction = false;
    }

    /// <summary>Spends actions through the encounter store when combat rules are attached.</summary>
    /// <param name="amount">The action count paid by a Unity-hosted action, including a free action.</param>
    /// <exception cref="System.InvalidOperationException">
    /// The controller cannot afford the requested positive cost.
    /// </exception>
    public void SpendActions(uint amount)
    {
        if (amount == 0)
            return;
        (UnityCombatRulesBridge bridge, CreatureId actor) = RequireCombatRules();
        if (amount > checked((uint)bridge.GetActionsRemaining(actor)))
            throw new System.InvalidOperationException("The controller cannot afford this action.");
        bridge.SpendEncounterActions(actor, checked((int)amount));
    }

    internal void AttachCombatRules(UnityCombatRulesBridge bridge, CreatureId creatureId)
    {
        ValidateCombatRulesAttachment(bridge, creatureId);
        combatRules = bridge;
        rulesCreatureId = creatureId;
    }

    internal void ValidateCombatRulesAttachment(
        UnityCombatRulesBridge bridge,
        CreatureId creatureId
    )
    {
        if (bridge == null)
            throw new System.ArgumentNullException(nameof(bridge));
        if (creatureId.IsEmpty)
            throw new System.ArgumentException(
                "A rules creature ID is required.",
                nameof(creatureId)
            );
        EnsureAttachmentInvariant();
        if (combatRules != null)
            throw new System.InvalidOperationException(
                "The controller is already owned by a rules composition."
            );
        if (IsInDungeonExploration)
            throw new System.InvalidOperationException(
                "A controller cannot attach to combat rules while exploration is enabled."
            );
    }

    /// <summary>Releases encounter action ownership when it still belongs to the given bridge.</summary>
    /// <remarks>
    /// Exact bridge identity prevents delayed cleanup from an older encounter from detaching a
    /// controller that a newer encounter already owns.
    /// </remarks>
    internal void DetachCombatRules(UnityCombatRulesBridge bridge)
    {
        if (!ReferenceEquals(combatRules, bridge))
            return;

        combatRules = null;
        rulesCreatureId = default;
    }

    internal bool TryGetCombatRules(out UnityCombatRulesBridge bridge, out CreatureId creatureId)
    {
        if (IsInDungeonExploration)
        {
            EnsureAttachmentInvariant();
            bridge = null;
            creatureId = default;
            return false;
        }
        return TryGetAttachedCombatRules(out bridge, out creatureId);
    }

    /// <summary>Calculates the Unity Slowed contribution without owning turn state.</summary>
    internal uint CalculateTurnStartActions()
    {
        Ref<uint> contribution = new(3);
        ResetActionPointsEvent.Invoke(contribution);
        return contribution.Value;
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
        EnsureAttachmentInvariant();
        if (enabled && combatRules != null)
            throw new System.InvalidOperationException(
                "Exploration cannot be enabled while combat rules are attached."
            );
        IsInDungeonExploration = enabled;
    }

    private bool TryGetAttachedCombatRules(
        out UnityCombatRulesBridge bridge,
        out CreatureId creatureId
    )
    {
        EnsureAttachmentInvariant();
        bridge = combatRules;
        creatureId = rulesCreatureId;
        return bridge != null;
    }

    private (UnityCombatRulesBridge Bridge, CreatureId Actor) RequireCombatRules()
    {
        if (!TryGetCombatRules(out UnityCombatRulesBridge bridge, out CreatureId creatureId))
            throw new System.InvalidOperationException(
                "A committed combat turn requires a complete rules attachment."
            );
        return (bridge, creatureId);
    }

    private void EnsureAttachmentInvariant()
    {
        if ((combatRules == null) != rulesCreatureId.IsEmpty)
            throw new System.InvalidOperationException(
                "Combat rules attachment is structurally incomplete."
            );
    }

    public abstract void EndTurn();

    /// <summary>Returns a copied list of all action-bar entries the controller can perform.</summary>
    /// <returns></returns>
    public List<EntityAction> GetActions()
    {
        List<EntityAction> available = new(Actions);
        GetActionsEvent.Invoke(available);
        return available;
    }

    /// <summary>Returns the action-bar entries that exploration authority may offer.</summary>
    /// <returns>A copied movement-only view of the shared action list.</returns>
    public List<EntityAction> GetExplorationActions() =>
        GetActions().FindAll(action => action.IsExplorationAction);

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

    /// <summary>Checks whether an action can begin under this controller's current authority.</summary>
    /// <param name="action">The movement or combat action to authorize and invoke.</param>
    /// <returns>Whether the action may begin now.</returns>
    public bool CanTakeAction(EntityAction action)
    {
        if (action == null || IsTakingAction)
            return false;
        if (IsInDungeonExploration)
            return action.IsExplorationAction && action.IsAvailable(this);
        return HasTurnAuthority && action.IsAvailable(this);
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
        if (!CanTakeAction(action))
            return;

        IsTakingAction = true;
        action.Invoke(this.gameObject);
    }

    /// <summary>
    /// Starts a typed-selection action with a resolver supplied by an AI, replay, or test.
    /// </summary>
    /// <param name="action">The registered action to authorize.</param>
    /// <param name="resolver">The resolver that supplies the action's typed selection.</param>
    public void TakeAction(EntityAction action, ISelectionResolver resolver)
    {
        if (action == null)
        {
            Debug.LogWarning("No action provided to TakeAction!");
            return;
        }
        if (resolver == null)
            throw new System.ArgumentNullException(nameof(resolver));
        if (action is not ISelectionDrivenEntityAction selectionDriven)
        {
            throw new System.ArgumentException(
                "The supplied action does not accept a selection resolver.",
                nameof(action)
            );
        }
        if (!CanTakeAction(action))
            return;

        IsTakingAction = true;
        selectionDriven.Invoke(gameObject, resolver);
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
