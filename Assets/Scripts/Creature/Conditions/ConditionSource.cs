using System.Collections.Generic;

/// <summary>
/// A container and delegator for conditions.
/// </summary>
public class ConditionSource
{
    /// <summary>
    /// The names of the source
    /// </summary>
    List<string> sourceQualifiers = new();
    List<(string, List<IConditionTarget>)> conditions = new();

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
