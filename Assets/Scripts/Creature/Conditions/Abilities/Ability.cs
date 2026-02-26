using UnityEngine;
using UnityEngine.Events;

public class Ability : ConditionSource
{
    public string Name { get; protected set; }
    UnityAction<GameObject> ApplyCallback;

    public Ability(string name, UnityAction<GameObject> apply)
    {
        this.Name = name;
        ApplyCallback = apply;
    }

    public void Apply(GameObject g)
    {
        ApplyCallback(g);
        Debug.Log("Applied ability " + Name +" to " + g.name);
    }
}
