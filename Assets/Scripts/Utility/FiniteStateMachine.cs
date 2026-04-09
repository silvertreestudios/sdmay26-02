using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class FiniteStateMachine <T> where T : IFSMState<T>
{

    public T currentState { get; protected set;}
    protected T previousState;
    //public idle to the states themselves can set the FSM back to idle upon exiting, may change this to be handled by the API
    public bool isInTransition { get; private set; } = false;

    // change to new state and indicate if canceling
    // if a state is canceled, that tells the action controller to refund the cost of the action
    public virtual bool ChangeState(T newState)
    {
        if (isInTransition)
            return false;

        StateTransition(newState);
        return true;
    }

    protected void StateTransition(T newState)
    {
        isInTransition = true;
        if (currentState != null)currentState.Exit();
        //Debug.Log($"[FiniteStateMachine] Exited state: {currentState?.GetType().Name}");
        
        previousState = currentState;
        currentState = newState;

        if(currentState != null) currentState.Enter(this);

        isInTransition = false;
        //Debug.Log($"[FiniteStateMachine] Entered state: {currentState?.GetType().Name}");
    }

    public void RevertState()
    {
        if (previousState != null) ChangeState(previousState);
        //else Debug.LogWarning("[FiniteStateMachine] No previous state to revert to.");
    }

    
}
