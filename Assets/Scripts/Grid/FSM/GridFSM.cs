using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class GridFSM : FiniteStateMachine<GridFSMState>
{

    
    //public idle to the states themselves can set the FSM back to idle upon exiting, may change this to be handled by the API
    public StateIdle idleState { get; private set; } = new StateIdle();
    public bool canceled = false;

    private float timeSinceLastClick = 0f;
    private float lastClickTime = 0f;

    public GridFSM()
    {
        currentState = idleState;
    }

    public override bool ChangeState(GridFSMState newState)
    {
        
        if (base.ChangeState(newState))
        {
            timeSinceLastClick = 0f;
            lastClickTime = 0f;
            return true;
        } 
        return false;
        
    }

    // Update is called once per frame
    public void FSMUpdate(GridCharacterController3D controller)
    {
        
        // if (currentState != null && !isInTransition)
        // {
        //     currentState.StateUpdate();
        // }
        timeSinceLastClick = Time.time - lastClickTime;
        if (InputCompat.LeftClickDown())
        {
            lastClickTime = Time.time;
            if(timeSinceLastClick <= controller.doubleClickTime)
            {
                currentState.DoubleLeftclick();
            } else
            {
                currentState.Leftclick();
            }
        }

        if (InputCompat.RightClickDown())
        {
            currentState.Rightclick();
        }
    }

    public void FSMFixedUpdate()
    {
        // if (currentState != null && !isInTransition)
        // {
        //     currentState.FixedStateUpdate();
        // }
    }

    
}
