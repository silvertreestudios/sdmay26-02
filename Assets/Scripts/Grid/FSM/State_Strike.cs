using UnityEngine;
using System.Collections.Generic;

public class State_Strike : FSM_State_Abstract
{
    // target character
    GameObject character;
    // reference to helper class
    GridCharacterController3D controller;
    private GameObject selection = null;
    public GameObject target { get; private set; } = null;
    public bool canceled { get; private set; } = false;
    private int range;
    private List<GameObject> occupantsInRange = new List<GameObject>();
    private float timeSinceLastClick = 0f;
    private float lastClickTime = 0f;

    // compact constructor
    public State_Strike(GameObject character, int range, GridCharacterController3D controller)
    {
        this.controller = controller;
        this.character = character;
        this.range = range;
    }
    public override void EnterState()
    {
        lastClickTime = 0f;
        timeSinceLastClick = 0f;
        target = null;
        selection = null;
        canceled = false;
        occupantsInRange.Clear();
        //Set active player for helper functions
        controller.SetActivePlayer(character);
        //currently the highlights are set in this function, this needs to be moved here in the future
        occupantsInRange = controller.GetOccupantsInArea(character, range);

    }
    public override void ExitState(bool canceled)
    {
        this.canceled = canceled;
        occupantsInRange.Clear();
        controller.rangeHighlighter.ClearHighlights();
        Action_FSM.GetInstance().ChangeState(Action_FSM.GetInstance().idleState, canceled);
    }
    public override void Leftclick()
    {
        if (controller.TryGetClickedCell(controller.currentCamera, out Vector3Int targetCell))
        {
            List<GameObject> occupantsInCell = controller.gridMemory.GetOccupantsInArea(new List<Vector3Int> { targetCell });
            if (occupantsInCell.Count == 0)
            {
                Debug.Log("[State_Strike] No occupants in the selected cell.");
            } else
            {
                selection = occupantsInCell[0];
                Debug.Log($"[State_Strike] Target preview: {selection.name}");
            }
        }
    }

    public void doubleLeftclick()
    {
        if (occupantsInRange.Contains(selection))
        {
            target = selection;
            Debug.Log($"[State_Strike] Target confirmed: {target.name}");
            ExitState(false);
        }
        else
        {
            Debug.Log("[State_Strike] Selected an invalid target.");
        }
    }
    public override void Rightclick()
    {
        // cancel action when right clicking
        Debug.Log("[State_Strike] Action cancelled");
        selection = null;
        target = null;
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
