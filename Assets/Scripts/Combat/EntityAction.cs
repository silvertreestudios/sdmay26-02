using System.Threading.Tasks;
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

    /// <summary>Awaits this action's authoritative cost unless exploration makes it free.</summary>
    /// <param name="ac">The controller paying through its attached encounter store.</param>
    /// <returns>The complete action-spend root, or a completed value during exploration.</returns>
    protected ValueTask PayCostAsync(ActionController ac)
    {
        if (ac != null && !ac.IsInDungeonExploration)
            return ac.SpendActionsAsync(ActionCost);
        return default;
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
