using UnityEngine;
using UnityEngine.Events;

public class Slowed : Condition
{
    public Slowed(string name, UnityAction<GameObject> apply) : base(name, apply)
    {
    }
}
