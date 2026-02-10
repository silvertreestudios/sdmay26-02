using System;
using System.Collections;
using UnityEngine;

public static class FSM_API
{
    // set dependencies
    private static Action_FSM ActionFSM => Action_FSM.GetInstance();
    private static GridCharacterController3D Controller => GridCharacterController3D.Instance;


    // cancel current action
    public static void CancelCurrentAction()
    {
        if (!ActionFSM.isInTransition)
        {
            ActionFSM.ChangeState(ActionFSM.idleState, true);
        }
    }

    // contstruct stride
    public static IEnumerator Stride(GameObject character, CoroutineResult<bool> canceled)
    {
        if(!ActionFSM.isInTransition && ActionFSM.currentState == ActionFSM.idleState)
        {
            State_Stride strideState = new State_Stride(character, Controller);
            ActionFSM.ChangeState(strideState, false);
            // wait until state is no longer Stride
            while (ActionFSM.currentState == strideState)
            {
                yield return null;
            }
            // return cancelled status
            canceled.Value = strideState.canceled;
        } else
        {
            Debug.LogWarning("[FSM_API] Cannot initiate Stride while FSM is in transition or not in Idle state.");
            canceled.Value = true;
        }
        
    }
    // construct unarmed
    public static IEnumerator Unarmed(GameObject attacker, int range, CoroutineResult<GameObject> target, CoroutineResult<bool> canceled)
    {
        if(!ActionFSM.isInTransition && ActionFSM.currentState == ActionFSM.idleState)
        {
            State_Unarmed unarmedState = new State_Unarmed(attacker, range, Controller);
            ActionFSM.ChangeState(unarmedState, false);
            // wait until state is no longer Unarmed
            while (ActionFSM.currentState == unarmedState)
            {
                yield return null;
            }
            target.Value = unarmedState.target;
            canceled.Value = unarmedState.canceled;
        } else
        {
            Debug.LogWarning("[FSM_API] Cannot initiate Unarmed while FSM is in transition or not in Idle state.");
            canceled.Value = true;
        }
        
    }
}