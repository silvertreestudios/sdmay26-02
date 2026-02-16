using UnityEngine;

public abstract class EntityAction
{
    public uint ActionCost { get; }

    public EntityAction(uint cost)
    {
        this.ActionCost = cost;
    }

    protected void PayCost(PlayerActionController ac)
    {
        if (ac != null) 
            ac.ActionPoints -= ActionCost;
    }

    public abstract void Invoke(GameObject target);
}