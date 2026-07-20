using UnityEngine;

public abstract class EntityAction
{
    // Done by Ryan Meyer 04/07/2026
    public abstract string ActionName { get; }
    public uint ActionCost { get; }

    public EntityAction(uint cost)
    {
        this.ActionCost = cost;
    }

    protected void PayCost(ActionController ac)
    {
        if (ac != null && !ac.IsInDungeonExploration)
            ac.ActionPoints -= ActionCost;
    }

    /// <summary>
    /// Base caller for entity actions. Should be
    /// called once action is completed.
    /// </summary>
    /// <param name="target">The calling gameobject</param>
    public virtual void Invoke(GameObject target)
    {
        CombatManager.GetInstance().CheckForEndOfGame();
    }
}
