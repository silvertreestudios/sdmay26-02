using System.Collections.Generic;
using Game.Rules.Runtime;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A container and delegator for conditions.
/// </summary>
public class ConditionSource
{
    private readonly RuleSource replaySource;
    private readonly bool hasReplaySource;

    /// <summary>
    /// The names of the source
    /// </summary>
    List<string> sourceQualifiers = new();
    List<(string, List<IConditionTarget>)> conditions = new();

    /// <summary>Creates an ordinary condition source with no passive replay identity.</summary>
    public ConditionSource() { }

    /// <summary>Creates a condition source whose import can be recognized after persistence.</summary>
    /// <param name="replaySource">The stable rules provenance used only by this imported source.</param>
    protected ConditionSource(RuleSource replaySource)
    {
        if (replaySource.IsEmpty)
            throw new System.ArgumentException(
                "A passive replay source is required.",
                nameof(replaySource)
            );
        this.replaySource = replaySource;
        hasReplaySource = true;
    }

    /// <summary>Reports stable provenance only for a source whose import may be replayed.</summary>
    internal bool TryGetReplaySource(out RuleSource source)
    {
        source = replaySource;
        return hasReplaySource;
    }

    public void Apply(IConditionTarget target)
    {
        for (int i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            target.Add(condition.Item1, this);
            condition.Item2.Add(target);
        }
    }

    public void Remove()
    {
        for (int i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            foreach (var target in condition.Item2)
                target.Remove(condition.Item1, this);
            condition.Item2 = new();
        }
    }
}

/*
public class Equipment : ConditionSource
{
    string Name;

    public void Equip(GameObject g)
    {
        // Get IConditionTarget target on gameobject from a script
        // this.Apply(target);
    }

    public void Dequip()
    {
        Remove();
    }
}
*/
