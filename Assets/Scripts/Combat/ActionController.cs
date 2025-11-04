using UnityEngine;
using System.Collections.Generic;

public class ActionController : MonoBehaviour
{
    [SerializeField]
    protected List<EntityActionEffect> Choices = new List<EntityActionEffect>();
    [SerializeField]
    protected List<EntityActionEffect> Movements = new List<EntityActionEffect>();
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

    public void TakeAction(EntityActionEffect action)
    {
        if (IsTurn)
            action.Invoke(this.gameObject);
    }

    public uint GetInitiative()
    {
        return (uint)Random.Range(1, 20);
    }
}
