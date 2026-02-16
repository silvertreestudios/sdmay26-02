using UnityEngine;
using System.Collections.Generic;

public abstract class ActionController : MonoBehaviour
{
    
    protected List<EntityAction> Actions = new List<EntityAction>();
    //[SerializeField]
    protected List<EntityAction> Movements = new List<EntityAction>();
    protected bool IsTurn = false;
    public bool IsTakingAction { get; set; } = false;
    [field: SerializeField]
    public uint ActionPoints { get; set; }

    public abstract void StartTurn();
    public abstract void EndTurn();
    public void TakeAction(EntityAction action)
    {
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
}