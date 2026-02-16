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

        //Set active player for helper functions
        controller.SetActivePlayer(character);

        AI ai = character.GetComponent<AI>();
        if (ai != null) {
            // ask ai what tile
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
            if (controller.visualIndicator.IsActive && controller.lastClickedCell == path[path.Count - 1])
            {
                controller.isProcessingTurn = true;
                controller.rangeHighlighter.ClearHighlights();
                controller.visualIndicator.Clear();
                
                // movement tracking is handled by character controller, not sure if this is the best design choice
                // could maybe cause issues if multiply actions try to read the movement information without cleaning up
                controller.StartCoroutine(controller.ExecuteMovementInternal(character, controller.currentMovement, path));
                Exit();
            }
        }

        //Highlight possible stride locations
        controller.rangeHighlighter.UpdateHighlights(startCell, controller.maxMovementDistance);
    }
    public override bool Exit()
    {
        this.canceled = canceled;
        // Clear visual indicators and return to idle
        controller.visualIndicator.Clear();
        controller.rangeHighlighter.ClearHighlights();
        fsm.canceled = canceled;
        if(!fsm.ChangeState(fsm.idleState))
        {
            Debug.LogError("[State_Stride] Failed to change to idle state");
            return false;
        }
        // Debug.Log("[State_Stride] Exited Stride State");
        return true;
    }
    public override void Leftclick()
    {
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
        // execute stride if a valid path is selected
        if (controller.visualIndicator.IsActive && controller.lastClickedCell == path[path.Count - 1])
        {
            controller.isProcessingTurn = true;
            controller.rangeHighlighter.ClearHighlights();
            controller.visualIndicator.Clear();
            
            // movement tracking is handled by character controller, not sure if this is the best design choice
            // could maybe cause issues if multiply actions try to read the movement information without cleaning up
            controller.StartCoroutine(controller.ExecuteMovementInternal(character, controller.currentMovement, path));
            canceled = false;
            Exit();
        }
    }
    public override void Rightclick()
    {
        // cancel stride when right clicking
        // if you are reading this and want to make another action, try to keep right click consistent for cancelling
        // I like it because its how the UI in Rimworld works and I quite enjoy that game :)
        Debug.Log("[State_Stride] Stride cancelled");
        canceled = true;
        Exit();

    }
    
}
