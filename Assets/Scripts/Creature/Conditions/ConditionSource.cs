using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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
        for(int i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            target.Add(condition.Item1, this);
            condition.Item2.Add(target);
        }
    }
    public void Remove()
    {
        for(int i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            foreach(var target in condition.Item2)
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
/*
public class Spell : ConditionSource
{
    public string Name;

    public void Apply(List<GameObject> g)
    {
        // Get IConditionTarget on gameobjects

        // Condition added 
        int conditionDuration = 0;
        UnityEvent e = new();
        TurnStep ts = new TurnStep(e, conditionDuration);
        // CombatManager.Add(Turnstep);

        // 
    }
}
*/