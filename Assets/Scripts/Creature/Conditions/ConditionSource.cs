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
/*
public class Spell : ConditionSource
{
    public string Name;
    Condition c;

    public void Apply(List<GameObject> g)
    {
        Conditions c = g.GetComponent<Conditions>();
        c.Add(this, condition)

        int conditionDuration = 5; // Insert in turn queue right now. will not execute until the 6th attempt
        UnityAction callback = () => {
            c.Remove(this, condition);
        };
        TurnStep ts = new TurnStep(callback, conditionDuration);
        CombatManager.Add(Turnstep);
    }
}
*/
