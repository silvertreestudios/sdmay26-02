using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Combat.Spells;
using Game.Creature;
using Game.Rules.Unity;
using Game.Strikes;
using UnityEngine;

/// <summary>
/// Owns the legacy action lifecycle for one combatant and exposes a mixed action-bar view while
/// individual actions migrate to the rules dispatcher.
/// </summary>
/// <remarks>
/// Legacy <see cref="EntityAction"/> instances remain on their existing invocation path.
/// Definition-backed entries are registered separately and replace a legacy entry only when both
/// registrations intentionally share the same <see cref="ActionBarEntryKey"/>.
/// </remarks>
public abstract class ActionController : MonoBehaviour
{
    // Fields
    protected List<EntityAction> Actions = new();
    protected List<EntityAction> Movements = new();
    protected List<EntityAction> Reactions = new();
    protected bool IsTurn = false;
    private readonly Dictionary<EntityAction, ActionBarEntryKey> legacyActionKeys = new();
    private readonly List<IDefinitionActionBarEntry> definitionActionEntries = new();
    private int nextGeneratedLegacyActionKey;

    public bool IsTakingAction { get; set; } = false;

    /// <summary>
    /// Gets whether this controller currently owns turn authority for presentation-triggered
    /// actions. Internal adapters must check this live value in addition to any cached UI identity.
    /// </summary>
    internal bool HasTurnAuthority => IsTurn;

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
        ActionPoints = newActionPoints.Value;
        StrikePenalty = 0;
        SpellEffectController.ExpireAtStartOfTurn(gameObject);
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
    /// Builds the deterministic action-bar view for this combatant from explicit legacy and rules
    /// definition entries.
    /// </summary>
    /// <returns>
    /// An immutable ordered view in which a definition replaces only a legacy entry with the same
    /// stable key. Matching display text alone never removes an entry.
    /// </returns>
    public IReadOnlyList<IActionBarEntry> GetActionBarEntries()
    {
        List<EntityAction> availableActions = GetActions();
        List<LegacyActionBarEntry> legacyEntries = new(availableActions.Count);
        foreach (EntityAction action in availableActions)
        {
            legacyEntries.Add(
                new LegacyActionBarEntry(RequireLegacyActionKey(action), action, this)
            );
        }

        return new ActionBarEntryCatalog(legacyEntries, definitionActionEntries).Entries;
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
        if (action == null)
        {
            Debug.LogWarning("No action provided to TakeAction!");
            return;
        }
        //Debug.Log("Attempting to take action: " + action);
        uint cost = action.ActionCost;
        if (!IsTurn || cost > ActionPoints)
            return;
        IsTakingAction = true;
        action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        int initiativeBonus = this.gameObject.GetComponent<CreatureComponent>().GetInitiative();
        uint roll = (uint)UnityEngine.Random.Range(1, 21);
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

    /// <summary>
    /// Adds a legacy action with a generated key that remains stable for this controller's
    /// lifetime.
    /// </summary>
    /// <param name="action">The non-null legacy action to register once.</param>
    /// <remarks>
    /// Code that is preparing an explicit rules-definition replacement should use
    /// <see cref="AddAction(ActionBarEntryKey, EntityAction)"/> so both entries can deliberately
    /// share a durable key.
    /// </remarks>
    public void AddAction(EntityAction action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        AddAction(CreateGeneratedLegacyKey(action), action);
    }

    /// <summary>Adds a legacy action under an explicit stable action-bar key.</summary>
    /// <param name="key">The non-empty key a future definition may intentionally replace.</param>
    /// <param name="action">The non-null legacy action to register once.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is uninitialized.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The action instance or key is already registered as a legacy action.
    /// </exception>
    public void AddAction(ActionBarEntryKey key, EntityAction action)
    {
        if (key.IsEmpty)
            throw new ArgumentException("A legacy action requires a stable key.", nameof(key));
        if (action == null)
            throw new ArgumentNullException(nameof(action));
        if (legacyActionKeys.ContainsKey(action) || Actions.Contains(action))
            throw new InvalidOperationException("The legacy action is already registered.");
        foreach (ActionBarEntryKey registeredKey in legacyActionKeys.Values)
        {
            if (registeredKey == key)
                throw new InvalidOperationException(
                    $"Legacy action key '{key}' is already registered."
                );
        }

        Actions.Add(action);
        legacyActionKeys.Add(action, key);
        _actionNames.Add(action.ToString()); // Add action name to the list for testing purposes
    }

    /// <summary>Removes one legacy action instance and its stable action-bar registration.</summary>
    /// <param name="action">The non-null legacy action to remove.</param>
    public void RemoveAction(EntityAction action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (Actions.Remove(action))
            legacyActionKeys.Remove(action);
    }

    /// <summary>Adds one heterogeneous rules-definition entry to this combatant's action bar.</summary>
    /// <param name="entry">The fully configured definition entry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Another definition entry already uses the same stable key.
    /// </exception>
    public void AddDefinitionAction(IDefinitionActionBarEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));
        foreach (IDefinitionActionBarEntry registeredEntry in definitionActionEntries)
        {
            if (registeredEntry.Key == entry.Key)
            {
                throw new InvalidOperationException(
                    $"Definition action key '{entry.Key}' is already registered."
                );
            }
        }

        definitionActionEntries.Add(entry);
    }

    /// <summary>Removes the definition entry registered under one stable action-bar key.</summary>
    /// <param name="key">The non-empty definition entry key.</param>
    /// <returns><see langword="true"/> when a matching entry was removed.</returns>
    public bool RemoveDefinitionAction(ActionBarEntryKey key)
    {
        if (key.IsEmpty)
            throw new ArgumentException("A definition action key is required.", nameof(key));

        for (int index = 0; index < definitionActionEntries.Count; index++)
        {
            if (definitionActionEntries[index].Key != key)
                continue;

            definitionActionEntries.RemoveAt(index);
            return true;
        }

        return false;
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

    private ActionBarEntryKey RequireLegacyActionKey(EntityAction action)
    {
        if (action == null)
            throw new InvalidOperationException(
                "The legacy action list contains no action instance."
            );
        if (legacyActionKeys.TryGetValue(action, out ActionBarEntryKey key))
            return key;

        key = CreateGeneratedLegacyKey(action);
        legacyActionKeys.Add(action, key);
        return key;
    }

    private ActionBarEntryKey CreateGeneratedLegacyKey(EntityAction action)
    {
        string typeName = action.GetType().FullName;
        if (string.IsNullOrWhiteSpace(typeName))
            typeName = action.GetType().Name;

        string ordinal = nextGeneratedLegacyActionKey.ToString(CultureInfo.InvariantCulture);
        nextGeneratedLegacyActionKey++;
        return new ActionBarEntryKey($"legacy/{typeName}/{ordinal}");
    }
}
