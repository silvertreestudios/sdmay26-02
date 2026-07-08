using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores active conditions by source so rules can query condition names without coupling to condition UI or effects.
/// </summary>
public class Conditions : MonoBehaviour, IConditionTarget
{
    protected Dictionary<string, List<ConditionSource>> AppliedConditions = new();

    /// <summary>
    /// Adds a condition from a specific source, preserving multiple sources for the same condition.
    /// </summary>
    /// <param name="condition">The condition name to add.</param>
    /// <param name="source">The source responsible for applying the condition.</param>
    public void Add(string condition, ConditionSource source)
    {
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
}
