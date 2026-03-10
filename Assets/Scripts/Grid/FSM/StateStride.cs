using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

public class StateStride : GridFSMState
{
    // target character
    GameObject character;
    // reference to helper class
    GridCharacterController3D controller;
    public bool canceled { get; private set; } = false;
    private Vector3Int startCell;
    private List<Vector3Int> path;

    // compact constructor
    public StateStride(GameObject character, GridCharacterController3D controller)
    {
        this.controller = controller;
        this.character = character;
    }
    public override void Enter(FiniteStateMachine<GridFSMState> fsm)
    {
        base.Enter(fsm);
        // Debug.Log("[State_Stride] Entered Stride State");
        canceled = false;
        startCell = controller.coordinateConverter.GetCharacterCell(character);

        controller.isProcessingTurn = false;

        AIActionController ai = character.GetComponent<AIActionController>();
        if (ai != null) {
            
                controller.isProcessingTurn = true;
                // grab best path from the AI's controller, this should be set during its decision making process
                if(ai.bestPath == null || ai.bestPath.Count == 0)
                {
                    Debug.LogWarning("AI has no path to target, skipping movement");                    
                    this.fsm.ChangeState(this.fsm.idleState);
                } else {
                    Debug.Log("starting AI stride movement, path length: " + ai.bestPath.Count);
                    controller.StartCoroutine(ExecutePlayerMovement(ai.bestPath));
                }
        } else {

        //Highlight possible stride locations
        controller.rangeHighlighter.UpdateHighlights(startCell, controller.maxMovementDistance);
        }
    }

    //called by FSM machine once a state change is triggered
    public override bool Exit()
    {
        fsm.canceled = canceled;
        // Clear visual indicators
        controller.visualIndicator.Clear();
        controller.rangeHighlighter.ClearHighlights();
        return true;
    }
    public override void Leftclick()
    {
        if (controller.isProcessingTurn) return; // Cannot select path while moving

        // check if clicked cell is valid stride location, then display preview of path
        if(controller.TryValidateAndGetPath(controller.currentCamera, character, out List<Vector3Int> path))
        {
            this.path = path;
            controller.visualIndicator.ShowPath(path, false);
            controller.lastClickedCell = path[path.Count - 1];
        } else
        {
            // invalid cell, make it impossible to execute stride
            controller.lastClickedCell = Vector3Int.zero;
        }
        
    }

    public override void DoubleLeftclick()
    {
        if (controller.isProcessingTurn) return; // Cannot execute stride while moving

        // execute stride if a valid path is selected
        if (controller.visualIndicator.IsActive && controller.lastClickedCell == path[path.Count - 1])
        {
            controller.isProcessingTurn = true;
            controller.rangeHighlighter.ClearHighlights();
            controller.visualIndicator.Clear();
            
            // movement tracking is handled by character controller, not sure if this is the best design choice
            // could maybe cause issues if multiply actions try to read the movement information without cleaning up
            controller.StartCoroutine(ExecutePlayerMovement(path));
        }
    }

    private System.Collections.IEnumerator ExecutePlayerMovement(List<Vector3Int> path)
    {
        ITokenMovement movement = controller.GetMovementController(character);
        yield return controller.StartCoroutine(controller.ExecuteMovementInternal(character, movement, path));
        canceled = false;
        fsm.ChangeState(fsm.idleState);
    }

    public override void Rightclick()
    {
        if (controller.isProcessingTurn) return; // Cannot cancel while moving

        // cancel stride when right clicking
        // if you are reading this and want to make another action, try to keep right click consistent for cancelling
        // I like it because its how the UI in Rimworld works and I quite enjoy that game :)
        //Debug.Log("[State_Stride] Stride cancelled");
        canceled = true;
        fsm.ChangeState(fsm.idleState);

    }
    
}
