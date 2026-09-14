using Game.Creature;
using UnityEngine;
using UnityEngine.Events;

public class Condition
{
    public string Name { get; protected set; }
    UnityAction<GameObject> ApplyCallback;

    public Condition(string name, UnityAction<GameObject> apply)
    {
        this.Name = name;
        ApplyCallback = apply;
    }

    /// <summary>Applies this condition to a target for one source.</summary>
    /// <remarks>
    /// An override may commit feature mechanics before calling this implementation to add the
    /// display and persistence entry. This prevents a failed mechanic from leaving a false entry.
    /// </remarks>
    /// <param name="source">The source displayed and persisted with the condition.</param>
    /// <param name="obj">The target that receives the condition.</param>
    public virtual void Apply(ConditionSource source, GameObject obj)
    {
        obj.GetComponent<Conditions>().Add(Name, source);
        ApplyCallback(obj);
    }
}
