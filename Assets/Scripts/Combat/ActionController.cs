using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;
using Game.Strikes;

public abstract class ActionController : MonoBehaviour
{
    // Fields
    protected List<EntityAction> Actions = new();
    protected List<EntityAction> Movements = new();
    protected List<EntityAction> Reactions = new();
    protected bool IsTurn = false;
    public bool IsTakingAction { get; set; } = false;
    [field: SerializeField]
    public uint ActionPoints { get; set; }
    public bool Reacted { get; set; }
    public uint StrikePenalty { get; set; } = 0;

    //Events
    public OnResetActionPoints ResetActionPointsEvent { get; protected set; } = new();
    public OnGetActions GetActionsEvent { get; protected set; } = new();
    public OnGetMovements GetMovementsEvent { get; protected set; } = new();
    public OnGetReactions GetReactionsEvent { get; protected set; } = new();

    [SerializeField]
    List<string> _actionNames = new List<string>(); // Temporary list of action names to add for testing purposes.  TODO remove

    

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public virtual void StartTurn()
    {
        IsTurn = true;
        Ref<uint> newActionPoints = new(3);
        ResetActionPointsEvent.Invoke(newActionPoints);
        ActionPoints = newActionPoints.Value;
        StrikePenalty = 0;
        HUDController.GetInstance().SetStrikeWeaponText("");
    }

    public abstract void EndTurn();


    /// <summary>
    /// Returns a copied list of all actions the controller can perform, excluding movements
    /// </summary>
    /// <returns></returns>
    public List<EntityAction> GetActions()
    {
        List<EntityAction> available = new(Actions);
        GetActionsEvent.Invoke(available);
        return available;
    }

    /// <summary>
    /// Returns a copied list of all movements the controller can perform
    /// </summary>
    /// <returns></returns>
    public List<EntityAction> GetMovements()
    {
        List<EntityAction> available = new(Movements);
        GetMovementsEvent.Invoke(available);
        return available;
    }

    /// <summary>
    /// Returns a copied list of all reactions the controller can perform
    /// </summary>
    /// <returns></returns>
    public List<EntityAction> GetReactions()
    {
        List<EntityAction> available = new(Reactions);
        GetReactionsEvent.Invoke(available);
        return available;
    }

    /// <summary>
    /// Performs a given action for this controller
    /// </summary>
    /// <param name="action"></param>
    public void TakeAction(EntityAction action)
    {
        if (action == null)
        {
            Debug.LogWarning("No action provided to TakeAction!");
            return;
        }
        //Debug.Log("Attempting to take action: " + action);
        uint cost = action.ActionCost;
        if (!IsTurn || cost > ActionPoints)
            return;
        IsTakingAction = true;
        action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        int initiativeBonus = this.gameObject.GetComponent<CreatureComponent>().GetSkillMod("perception", 0);
        uint roll = (uint)Random.Range(1, 20);
        Debug.Log(this.gameObject.name + " rolled initiative: " + roll +" +"+ initiativeBonus +" = " + (roll + initiativeBonus));
        roll += (uint)initiativeBonus;
        return roll;
    }
    public void AddAction(EntityAction action)
    {
        Actions.Add(action);
        _actionNames.Add(action.ToString()); // Add action name to the list for testing purposes
    }
    public void RemoveAction(EntityAction action)
    {
        Actions.Remove(action);
    }

    public string GetActionNames() // Temporary method for testing purposes to display available actions in log
    {
        string names = "";
        for(int i = 0; i < Actions.Count; i++)
        {
            names += i+": "+Actions[i]+"   ";
        }
        return names;
    }
}
