using UnityEngine;
using System.Collections.Generic;

public class ActionController : MonoBehaviour
{
    [SerializeField]
    protected List<EntityAction> Choices = new List<EntityAction>();
    [SerializeField]
    protected List<EntityAction> Movements = new List<EntityAction>();
    private bool IsTurn = false;

    protected void Start()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);
    }

    public void StartTurn()
    {
        IsTurn = true;
        // Provide Options
        // Prompt user or AI for action
        // Debugging code:
        Debug.Log("Turn: " + this);
    }

    [ContextMenu("End Turn")]
    private void EndTurn()
    {
        if (IsTurn)
        {
            IsTurn = false;
            Debug.Log("Turn End.");
            // Clean up turn state
            // I.E. UI, etc
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    public void TakeAction(EntityAction action)
    {
        if (IsTurn)
            action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        return (uint)Random.Range(1, 20);
    }
}
