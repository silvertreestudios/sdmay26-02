using System.Threading.Tasks;
using Game.Creature;
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
    /// Atomically validates a selected living encounter target and spends this action's cost.
    /// </summary>
    /// <param name="ac">The controller paying through its attached encounter store.</param>
    /// <param name="requiredLivingTarget">
    /// The selected Strike target that must still be a living participant when payment commits.
    /// </param>
    /// <returns>The complete target-aware action-spend root, or a completed exploration value.</returns>
    protected ValueTask PayStrikeCostAsync(
        ActionController ac,
        CreatureComponent requiredLivingTarget
    )
    {
        if (ac != null && !ac.IsInDungeonExploration)
            return ac.SpendStrikeActionsAsync(requiredLivingTarget, ActionCost);
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
