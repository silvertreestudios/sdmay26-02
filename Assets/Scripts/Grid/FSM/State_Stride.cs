using UnityEngine;
using System.Collections.Generic;

public class State_Stride : FSM_State_Abstract
{
    // target character
    GameObject character;
    // reference to helper class
    GridCharacterController3D controller;
    public bool canceled { get; private set; } = false;

    private float timeSinceLastClick = 0f;
    private float lastClickTime = 0f;
    private Vector3Int startCell;
    private List<Vector3Int> path;

    // compact constructor
    public State_Stride(GameObject character, GridCharacterController3D controller)
    {
        this.controller = controller;
        this.character = character;
    }
    public override void EnterState()
    {
        // Debug.Log("[State_Stride] Entered Stride State");
        canceled = false;
        lastClickTime = 0f;
        timeSinceLastClick = 0f;
        startCell = controller.coordinateConverter.GetCharacterCell(character);

        //Set active player for helper functions
        controller.SetActivePlayer(character);
        //Highlight possible stride locations
        controller.rangeHighlighter.UpdateHighlights(startCell, controller.maxMovementDistance);
    }
    public override void ExitState(bool canceled)
    {
        this.canceled = canceled;
        // Clear visual indicators and return to idle
        controller.visualIndicator.Clear();
        controller.rangeHighlighter.ClearHighlights();
        Action_FSM.GetInstance().ChangeState(Action_FSM.GetInstance().idleState, canceled);
        // Debug.Log("[State_Stride] Exited Stride State");
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

    public void doubleLeftclick()
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
            ExitState(false);
        }
    }
    public override void Rightclick()
    {
        // cancel stride when right clicking
        // if you are reading this and want to make another action, try to keep right click consistent for cancelling
        // I like it because its how the UI in Rimworld works and I quite enjoy that game :)
        Debug.Log("[State_Stride] Stride cancelled");
        ExitState(true);

    }
    public override void StateUpdate()
    {
        // update function that is called from the Action_FSM every frame
        timeSinceLastClick = Time.time - lastClickTime;
        if (InputCompat.LeftClickDown())
        {
            lastClickTime = Time.time;
            if(timeSinceLastClick <= controller.doubleClickTime)
            {
                doubleLeftclick();
            } else
            {
                Leftclick();
            }
        }

        if (InputCompat.RightClickDown())
        {
            Rightclick();
        }

    }
    
}
