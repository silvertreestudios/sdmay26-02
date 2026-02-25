using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;
using Game.Strikes;

public abstract class ActionController : MonoBehaviour
{
    //[SerializeField]
    protected List<EntityAction> Actions = new List<EntityAction>();
    //[SerializeField]
    protected List<EntityAction> Movements = new List<EntityAction>();
    protected bool IsTurn = false;
    public bool IsTakingAction { get; set; } = false;
    [field: SerializeField]
    public uint ActionPoints { get; set; }

    [SerializeField]
    List<string> _actionNames = new List<string>(); // Temporary list of action names to add for testing purposes.  TODO remove

    

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public abstract void StartTurn();
    public abstract void EndTurn();



    public void TakeAction(EntityAction action)
    {
        if (action == null)
        {
            Debug.LogWarning("No action provided to TakeAction!");
            return;
        }
        Debug.Log("Attempting to take action: " + action);
        uint cost = action.ActionCost;
        if (!IsTurn || cost > ActionPoints)
            return;
        IsTakingAction = true;
        action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        return (uint)Random.Range(1, 20);
    }
    public List<EntityAction> GetActions()
    {
        return Actions;
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
}
