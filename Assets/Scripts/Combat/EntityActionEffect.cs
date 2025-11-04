using UnityEngine;

public abstract class EntityActionEffect
{
    protected uint ActionCost;

    public EntityActionEffect(uint cost)
    {
        this.ActionCost = cost;
    }

    public abstract void Invoke(GameObject target);
}