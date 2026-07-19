using System.Collections.Generic;
using UnityEngine;

public interface IConditionTarget
{
    public void Add(string condition, ConditionSource source);
    public bool Contains(string condition, ConditionSource source);
    public bool Contains(string condition);
    public void Remove(string condition, ConditionSource source);
    public void Change(
        string oldCondition,
        ConditionSource oldSource,
        string newCondition,
        ConditionSource newSource
    );
}
