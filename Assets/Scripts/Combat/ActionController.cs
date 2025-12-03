using UnityEngine;
using System.Collections.Generic;

public class ActionController : MonoBehaviour
{
    [SerializeField]
    protected List<EntityAction> Choices = new List<EntityAction>();
    [SerializeField]
    protected List<EntityAction> Movements = new List<EntityAction>();
    private bool IsTurn = false;

    // Temporary field to store Stride action for testing
    [SerializeField]
    private Stride strideAction;

    protected void Start()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);
        
        strideAction = new Stride(1); // Cost of 1 action point
        
        if (Movements.Count == 0)
        {
            Movements.Add(strideAction);
        }
        else if (Movements[0] != strideAction)
        {
            Movements.Insert(0, strideAction);
        }
    }

    public void StartTurn()
    {
        IsTurn = true;
        
        // Notify GridCharacterController3D about active player
        GridCharacterController3D gridController = GridCharacterController3D.Instance;
        if (gridController != null)
        {
            // Extract character name from GameObject name
            string characterName = this.gameObject.name.Replace("Player ", "Player");
            gridController.SetActivePlayer(characterName);
        }
        
        // Provide Options
        // Prompt user or AI for action
        Debug.Log("Turn: " + this.gameObject.name);
    }

    // Changed from private to public so actions can call it
    [ContextMenu("End Turn")]
    public void EndTurn()
    {
        if (IsTurn)
        {
            IsTurn = false;
            Debug.Log("Turn End: " + this.gameObject.name);
            // Clean up turn state
            // I.E. UI, etc
            CombatManagerInterface.GetInstance().NextTurn();
        }
        else
        {
            Debug.LogWarning("Cannot end turn - it's not this character's turn!");
        }
    }

    /// <summary>
    /// Temporary function to invoke the Stride movement action for testing.
    /// Can be triggered from the Unity editor during runtime via right-click menu.
    /// </summary>
    [ContextMenu("Test Invoke Stride")]
    private void TestInvokeStride()
    {
        if (!IsTurn)
        {
            Debug.LogWarning("Cannot use Stride - it's not this character's turn!");
            return;
        }

        if (strideAction != null)
        {
            Debug.Log("Invoking Stride action...");
            strideAction.Invoke(this.gameObject);
        }
        else
        {
            Debug.LogWarning("Stride action is not initialized!");
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
