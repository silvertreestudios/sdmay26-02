using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A container for all the conditions that are applied to a target
/// </summary>
public class Conditions : MonoBehaviour, IConditionTarget
{
    protected Dictionary<string, List<ConditionSource>> AppliedConditions = new();
    public void Add(string condition, ConditionSource source)
    {
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
}
