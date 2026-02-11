using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class Action_FSM : SingletonMonoBehaviour< Action_FSM >
{

    public FSM_State_Abstract currentState { get; private set; } = null;
    public FSM_State_Abstract previousState { get; private set; } = null;
    //public idle to the states themselves can set the FSM back to idle upon exiting, may change this to be handled by the API
    public State_Idle idleState { get; private set; } = new State_Idle();
    public bool isInTransition { get; private set; } = false;

    // change to new state and indicate if canceling
    // if a state is canceled, that tells the action controller to refund the cost of the action
    public void ChangeState(FSM_State_Abstract newState, bool isCancel)
    {
        if (isInTransition)
        {
            Debug.LogWarning("[Action_FSM] Attempted to change state while already in transition.");
            return;
        }

        StateTransition(newState, isCancel);
    }

    private void StateTransition(FSM_State_Abstract newState, bool isCancel)
    {
        isInTransition = true;
        Debug.Log ($"[Action_FSM] here {isCancel}");
        if (currentState != null)currentState.ExitState(isCancel);
        Debug.Log($"[Action_FSM] Exited state: {currentState?.GetType().Name}");
        
        previousState = currentState;
        currentState = newState;

        if(currentState != null) currentState.EnterState();

        isInTransition = false;
        Debug.Log($"[Action_FSM] Entered state: {currentState?.GetType().Name}");
    }

    public void RevertState()
    {
        if (previousState != null) ChangeState(previousState, true);
        else Debug.LogWarning("[Action_FSM] No previous state to revert to.");
    }


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ChangeState(idleState, false);
    }

    // Update is called once per frame
    void Update()
    {
        if (currentState != null && !isInTransition)
        {
            currentState.StateUpdate();
        }
    }

    void FixedUpdate()
    {
        if (currentState != null && !isInTransition)
        {
            currentState.FixedStateUpdate();
        }
    }

    
}
