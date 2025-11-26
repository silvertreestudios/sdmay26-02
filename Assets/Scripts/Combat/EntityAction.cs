using UnityEngine;

public abstract class EntityAction
{
    protected uint ActionCost;

    public EntityAction(uint cost)
    {
        this.ActionCost = cost;
    }

    public abstract void Invoke(GameObject target);
}