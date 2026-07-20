using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FiniteStateMachine<T>
    where T : IFSMState<T>
{
    public T CurrentState { get; protected set; }
    protected T PreviousState;

    //public idle to the states themselves can set the FSM back to idle upon exiting, may change this to be handled by the API
    public bool IsInTransition { get; private set; } = false;

    // change to new state and indicate if canceling
    // if a state is canceled, that tells the action controller to refund the cost of the action
    public virtual bool ChangeState(T newState)
    {
        if (IsInTransition)
            return false;

        StateTransition(newState);
        return true;
    }

    protected void StateTransition(T newState)
    {
        IsInTransition = true;
        if (CurrentState != null)
            CurrentState.Exit();
        //Debug.Log($"[FiniteStateMachine] Exited state: {currentState?.GetType().Name}");

        PreviousState = CurrentState;
        CurrentState = newState;

        if (CurrentState != null)
            CurrentState.Enter(this);

        IsInTransition = false;
        //Debug.Log($"[FiniteStateMachine] Entered state: {currentState?.GetType().Name}");
    }

    public void RevertState()
    {
        if (PreviousState != null)
            ChangeState(PreviousState);
        //else Debug.LogWarning("[FiniteStateMachine] No previous state to revert to.");
    }
}
