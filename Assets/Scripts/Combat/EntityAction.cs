using UnityEngine;

/// <summary>Controls how an action is presented in the shared action bar.</summary>
public enum EntityActionPresentation
{
    /// <summary>Uses the standard action styling.</summary>
    Action,

    /// <summary>Uses movement-action styling and may be offered during exploration.</summary>
    Movement,
}

public abstract class EntityAction
{
    // Done by Ryan Meyer 04/07/2026
    public abstract string ActionName { get; }
    public uint ActionCost { get; }

    /// <summary>Gets the action-bar presentation group for this entry.</summary>
    public virtual EntityActionPresentation Presentation => EntityActionPresentation.Action;

    /// <summary>Gets whether exploration authority may offer this action.</summary>
    public virtual bool IsExplorationAction => false;

    public EntityAction(uint cost)
    {
        this.ActionCost = cost;
    }

    /// <summary>Reports whether this action is currently available to the supplied controller.</summary>
    /// <param name="controller">The controller considering this action.</param>
    /// <returns>Whether the action may begin selection or execution now.</returns>
    public virtual bool IsAvailable(ActionController controller)
    {
        if (controller == null)
            return false;
        return controller.IsInDungeonExploration || ActionCost <= controller.ActionPoints;
    }

    protected void PayCost(ActionController ac)
    {
        if (ac != null && !ac.IsInDungeonExploration)
            ac.SpendActions(ActionCost);
    }

    /// <summary>
    /// Base caller for entity actions. Should be
    /// called once action is completed.
    /// </summary>
    /// <param name="target">The calling gameobject</param>
    public virtual void Invoke(GameObject target)
    {
        CombatManager.GetInstance().CheckForEndOfGame();
        OnGameplayStateCommitted.Invoke();
    }
}
