using System.Collections.Generic;
using System.Linq;
using Game.DungeonPersistence.Actors;
using Game.Rules;
using UnityEngine;

/// <summary>
/// Tracks condition sources and exposes condition names to both rules snapshots and PF2e modifier providers.
/// </summary>
public class Conditions : MonoBehaviour, IConditionTarget, IPf2eModifierProvider
{
    private readonly Dictionary<string, List<ConditionPersistenceApplication>> appliedConditions =
        new();

    /// <summary>
    /// Active condition names used by UI and condition modifier mapping; source details remain internal to this component.
    /// </summary>
    public IReadOnlyCollection<string> ActiveConditionNames => appliedConditions.Keys;

    /// <summary>
    /// Gets whether this fresh component can receive a complete persistent replacement without
    /// orphaning reverse links held by existing condition sources.
    /// </summary>
    internal bool CanRestorePersistentState => appliedConditions.Count == 0;

    /// <summary>
    /// Adds a condition from a specific source, preserving multiple sources for the same condition.
    /// </summary>
    /// <param name="condition">The condition name to add.</param>
    /// <param name="source">The source responsible for applying the condition.</param>
    public void Add(string condition, ConditionSource source)
    {
        AddPersistent(condition, 0, source);
    }

    /// <summary>Adds one source-aware valued condition application.</summary>
    /// <param name="condition">The condition name or definition ID.</param>
    /// <param name="value">The non-negative valued-condition amount.</param>
    /// <param name="source">The applying source, or null for an intrinsic condition.</param>
    /// <param name="applicationId">
    /// Stable restored application identity, or an empty string for a new live application.
    /// </param>
    public void AddPersistent(
        string condition,
        int value,
        ConditionSource source,
        string applicationId = ""
    )
    {
        if (string.IsNullOrWhiteSpace(condition))
            return;
        if (value < 0)
            throw new System.ArgumentOutOfRangeException(nameof(value));

        List<ConditionPersistenceApplication> applications;
        ConditionPersistenceApplication application = new(condition, value, source, applicationId);
        if (!appliedConditions.TryGetValue(application.ConditionId, out applications))
            appliedConditions.Add(
                application.ConditionId,
                new List<ConditionPersistenceApplication> { application }
            );
        else
            applications.Add(application);
    }

    /// <summary>
    /// Checks whether a condition is present from a specific source.
    /// </summary>
    /// <param name="condition">The condition name to check.</param>
    /// <param name="source">The source that must be present.</param>
    /// <returns>True when that source currently applies the condition.</returns>
    public bool Contains(string condition, ConditionSource source)
    {
        string conditionId = NormalizeConditionId(condition);
        List<ConditionPersistenceApplication> applications;
        return conditionId.Length > 0
            && appliedConditions.TryGetValue(conditionId, out applications)
            && applications.Any(application => application.Source == source);
    }

    /// <summary>
    /// Checks whether a condition is present from any source.
    /// </summary>
    /// <param name="condition">The condition name to check.</param>
    /// <returns>True when the condition currently exists.</returns>
    public bool Contains(string condition)
    {
        string conditionId = NormalizeConditionId(condition);
        return conditionId.Length > 0 && appliedConditions.TryGetValue(conditionId, out _);
    }

    /// <summary>
    /// Returns a snapshot of active condition names for Unity-free rule evaluation.
    /// </summary>
    /// <returns>The active condition names without their source details.</returns>
    public IReadOnlyCollection<string> GetConditionNames()
    {
        return new List<string>(appliedConditions.Keys);
    }

    /// <summary>Captures every condition application, including duplicate shared sources.</summary>
    /// <returns>
    /// A copied sequence ordered by condition ID while preserving same-condition application order.
    /// </returns>
    internal IReadOnlyList<ConditionPersistenceApplication> CapturePersistentState()
    {
        List<ConditionPersistenceApplication> captured = new();
        foreach (
            KeyValuePair<
                string,
                List<ConditionPersistenceApplication>
            > pair in appliedConditions.OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
        )
        {
            captured.AddRange(pair.Value);
        }
        return captured.AsReadOnly();
    }

    /// <summary>
    /// Restores a complete source-aware condition set onto a fresh component and re-establishes
    /// shared-source removal links.
    /// </summary>
    /// <param name="applications">The fully validated applications to restore.</param>
    /// <exception cref="System.InvalidOperationException">The component already has conditions.</exception>
    internal void RestorePersistentState(IEnumerable<ConditionPersistenceApplication> applications)
    {
        if (!CanRestorePersistentState)
            throw new System.InvalidOperationException(
                "Persistent conditions can only be restored onto a fresh component."
            );
        if (applications == null)
            throw new System.ArgumentNullException(nameof(applications));
        ConditionPersistenceApplication[] copied = applications.ToArray();
        if (copied.Any(application => application == null))
            throw new System.ArgumentException(
                "Persistent conditions cannot contain null.",
                nameof(applications)
            );

        foreach (ConditionPersistenceApplication application in copied)
        {
            AddPersistent(
                application.ConditionId,
                application.Value,
                application.Source,
                application.ApplicationId
            );
            application.Source?.TrackRestoredApplication(application.ConditionId, this);
        }
    }

    /// <summary>
    /// Removes one source from a condition and clears the condition when no sources remain.
    /// </summary>
    /// <param name="condition">The condition name to remove.</param>
    /// <param name="source">The source being removed.</param>
    public void Remove(string condition, ConditionSource source)
    {
        string conditionId = NormalizeConditionId(condition);
        if (conditionId.Length == 0)
            return;
        List<ConditionPersistenceApplication> applications;
        if (appliedConditions.TryGetValue(conditionId, out applications))
        {
            int index = applications.FindIndex(application => application.Source == source);
            if (index >= 0)
                applications.RemoveAt(index);
            if (applications.Count < 1)
                appliedConditions.Remove(conditionId);
        }
    }

    private static string NormalizeConditionId(string condition) =>
        string.IsNullOrWhiteSpace(condition) ? string.Empty : condition.Trim();

    /// <summary>
    /// Replaces one sourced condition with another while preserving source-aware condition ownership.
    /// </summary>
    /// <param name="oldCondition">The condition name to remove.</param>
    /// <param name="oldSource">The source to remove from the old condition.</param>
    /// <param name="newCondition">The condition name to add.</param>
    /// <param name="newSource">The source applying the new condition.</param>
    public void Change(
        string oldCondition,
        ConditionSource oldSource,
        string newCondition,
        ConditionSource newSource
    )
    {
        Remove(oldCondition, oldSource);
        Add(newCondition, newSource);
    }

    /// <summary>
    /// Provides rule-derived modifiers from active conditions without requiring CreatureComponent to know condition details.
    /// </summary>
    /// <param name="statistic">The statistic currently being resolved.</param>
    /// <returns>Condition modifiers for the requested statistic.</returns>
    public IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic)
    {
        return ConditionModifierRules.GetModifiers(ActiveConditionNames, statistic);
    }
}

/// <summary>
/// Maps active condition names to PF2e modifiers while keeping condition-specific math outside CreatureComponent.
/// Add new condition modifiers here only when the condition itself directly changes a supported statistic.
/// </summary>
public static class ConditionModifierRules
{
    private static readonly Dictionary<string, Pf2eModifier[]> ModifiersByCondition = new()
    {
        // Off-Guard/Flat-Footed: circumstance penalty to AC. Source: https://2e.aonprd.com/Conditions.aspx?ID=58
        {
            NormalizeConditionKey("off-guard"),
            new[]
            {
                new Pf2eModifier(
                    -2,
                    Pf2eModifierType.Circumstance,
                    "Off-Guard",
                    Pf2eStatistic.ArmorClass
                ),
            }
        },
        {
            NormalizeConditionKey("flat-footed"),
            new[]
            {
                new Pf2eModifier(
                    -2,
                    Pf2eModifierType.Circumstance,
                    "Off-Guard",
                    Pf2eStatistic.ArmorClass
                ),
            }
        },
    };

    /// <summary>
    /// Converts active condition names into de-duplicated modifiers for the requested statistic.
    /// </summary>
    /// <param name="activeConditions">Condition names currently applied to a creature.</param>
    /// <param name="statistic">The statistic currently being resolved.</param>
    /// <returns>Condition modifiers that apply to the requested statistic.</returns>
    public static IEnumerable<Pf2eModifier> GetModifiers(
        IEnumerable<string> activeConditions,
        Pf2eStatistic statistic
    )
    {
        if (activeConditions == null)
            yield break;

        HashSet<string> emittedSources = new();
        foreach (string activeCondition in activeConditions)
        {
            if (
                !ModifiersByCondition.TryGetValue(
                    NormalizeConditionKey(activeCondition),
                    out Pf2eModifier[] modifiers
                )
            )
                continue;

            foreach (Pf2eModifier modifier in modifiers)
            {
                if (
                    modifier.TargetStatistic != statistic
                    || !emittedSources.Add(modifier.Source + modifier.TargetStatistic)
                )
                    continue;

                yield return modifier;
            }
        }
    }

    private static string NormalizeConditionKey(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
    }
}
