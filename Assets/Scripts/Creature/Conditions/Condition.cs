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

    /// <summary>
    /// Applies this Condition to a GameObject given a source
    /// </summary>
    /// <param name="source"</param>
    /// <param name="obj"></param>
    public void Apply(ConditionSource source, GameObject obj)
    {
        obj.GetComponent<Conditions>().Add(Name, source);
        ApplyCallback(obj);
    }
}
