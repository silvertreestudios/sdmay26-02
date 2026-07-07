using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

/// <summary>
/// A container for all the conditions that are applied to a target
/// </summary>
public class Conditions : MonoBehaviour, IConditionTarget, IPf2eModifierProvider
{
    protected Dictionary<string, List<ConditionSource>> AppliedConditions = new();

    public IReadOnlyCollection<string> ActiveConditionNames => AppliedConditions.Keys;

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

    public bool Contains(string condition, ConditionSource source)
    {
        List<ConditionSource> sources;
        return AppliedConditions.TryGetValue(condition, out sources) && sources.Contains(source);
    }

    public bool Contains(string condition)
    {
        return AppliedConditions.TryGetValue(condition, out _);
    }

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

    public void Change(string oldCondition, ConditionSource oldSource, string newCondition, ConditionSource newSource)
    {
        Remove(oldCondition, oldSource);
        Add(newCondition, newSource);
    }

    public IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic)
    {
        return ConditionModifierRules.GetModifiers(ActiveConditionNames, statistic);
    }
}

public static class ConditionModifierRules
{
    private static readonly Dictionary<string, Pf2eModifier[]> ModifiersByCondition = new()
    {
        // Off-Guard/Flat-Footed: circumstance penalty to AC. Source: https://2e.aonprd.com/Conditions.aspx?ID=58
        { NormalizeConditionKey("off-guard"), new[] { new Pf2eModifier(-2, Pf2eModifierType.Circumstance, "Off-Guard", Pf2eStatistic.ArmorClass) } },
        { NormalizeConditionKey("flat-footed"), new[] { new Pf2eModifier(-2, Pf2eModifierType.Circumstance, "Off-Guard", Pf2eStatistic.ArmorClass) } }
    };

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
