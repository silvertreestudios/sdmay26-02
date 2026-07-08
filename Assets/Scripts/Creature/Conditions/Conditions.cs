using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

/// <summary>
/// Tracks condition sources and exposes condition names to both rules snapshots and PF2e modifier providers.
/// </summary>
public class Conditions : MonoBehaviour, IConditionTarget, IPf2eModifierProvider
{
    protected Dictionary<string, List<ConditionSource>> AppliedConditions = new();

    /// <summary>
    /// Active condition names used by UI and condition modifier mapping; source details remain internal to this component.
    /// </summary>
    public IReadOnlyCollection<string> ActiveConditionNames => AppliedConditions.Keys;

    /// <summary>
    /// Adds a condition from a specific source, preserving multiple sources for the same condition.
    /// </summary>
    /// <param name="condition">The condition name to add.</param>
    /// <param name="source">The source responsible for applying the condition.</param>
    public void Add(string condition, ConditionSource source)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return;

        List<ConditionSource> sources;
        if(!AppliedConditions.TryGetValue(condition, out sources))
            AppliedConditions.Add(condition, new List<ConditionSource>() { source });
        else
            sources.Add(source);
    }

    /// <summary>
    /// Checks whether a condition is present from a specific source.
    /// </summary>
    /// <param name="condition">The condition name to check.</param>
    /// <param name="source">The source that must be present.</param>
    /// <returns>True when that source currently applies the condition.</returns>
    public bool Contains(string condition, ConditionSource source)
    {
        List<ConditionSource> sources;
        return AppliedConditions.TryGetValue(condition, out sources) && sources.Contains(source);
    }

    /// <summary>
    /// Checks whether a condition is present from any source.
    /// </summary>
    /// <param name="condition">The condition name to check.</param>
    /// <returns>True when the condition currently exists.</returns>
    public bool Contains(string condition)
    {
        return AppliedConditions.TryGetValue(condition, out _);
    }

    /// <summary>
    /// Returns a snapshot of active condition names for Unity-free rule evaluation.
    /// </summary>
    /// <returns>The active condition names without their source details.</returns>
    public IReadOnlyCollection<string> GetConditionNames()
    {
        return new List<string>(AppliedConditions.Keys);
    }

    /// <summary>
    /// Removes one source from a condition and clears the condition when no sources remain.
    /// </summary>
    /// <param name="condition">The condition name to remove.</param>
    /// <param name="source">The source being removed.</param>
    public void Remove(string condition, ConditionSource source)
    {
        List<ConditionSource> sources;
        if(AppliedConditions.TryGetValue(condition, out sources))
        {
            sources.Remove(source);
            if(sources.Count < 1)
                AppliedConditions.Remove(condition);
        }
    }

    /// <summary>
    /// Replaces one sourced condition with another while preserving source-aware condition ownership.
    /// </summary>
    /// <param name="oldCondition">The condition name to remove.</param>
    /// <param name="oldSource">The source to remove from the old condition.</param>
    /// <param name="newCondition">The condition name to add.</param>
    /// <param name="newSource">The source applying the new condition.</param>
    public void Change(string oldCondition, ConditionSource oldSource, string newCondition, ConditionSource newSource)
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
        { NormalizeConditionKey("off-guard"), new[] { new Pf2eModifier(-2, Pf2eModifierType.Circumstance, "Off-Guard", Pf2eStatistic.ArmorClass) } },
        { NormalizeConditionKey("flat-footed"), new[] { new Pf2eModifier(-2, Pf2eModifierType.Circumstance, "Off-Guard", Pf2eStatistic.ArmorClass) } }
    };

    /// <summary>
    /// Converts active condition names into de-duplicated modifiers for the requested statistic.
    /// </summary>
    /// <param name="activeConditions">Condition names currently applied to a creature.</param>
    /// <param name="statistic">The statistic currently being resolved.</param>
    /// <returns>Condition modifiers that apply to the requested statistic.</returns>
    public static IEnumerable<Pf2eModifier> GetModifiers(IEnumerable<string> activeConditions, Pf2eStatistic statistic)
    {
        if (activeConditions == null)
            yield break;

        HashSet<string> emittedSources = new();
        foreach (string activeCondition in activeConditions)
        {
            if (!ModifiersByCondition.TryGetValue(NormalizeConditionKey(activeCondition), out Pf2eModifier[] modifiers))
                continue;

            foreach (Pf2eModifier modifier in modifiers)
            {
                if (modifier.TargetStatistic != statistic || !emittedSources.Add(modifier.Source + modifier.TargetStatistic))
                    continue;

                yield return modifier;
            }
        }
    }

    private static string NormalizeConditionKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
    }
}
