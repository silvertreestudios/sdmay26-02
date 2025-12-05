using UnityEngine;

public abstract class EntityAction
{
    protected uint ActionCost;

    public EntityAction(uint cost)
    {
        this.ActionCost = cost;
    }

    public uint Cost()
    {
        return ActionCost;
    }

    public abstract void Invoke(GameObject target);
}