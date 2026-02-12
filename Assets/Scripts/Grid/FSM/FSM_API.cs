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

    // end turn
    public static void EndTurn()
    {
        if (!ActionFSM.isInTransition)
        {
            ActionFSM.ChangeState(ActionFSM.idleState, false);
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
    // construct Strike
    public static IEnumerator Strike(GameObject attacker, int range, CoroutineResult<GameObject> target, CoroutineResult<bool> canceled)
    {
        if(!ActionFSM.isInTransition && ActionFSM.currentState == ActionFSM.idleState)
        {
            State_Strike strikeState = new State_Strike(attacker, range, Controller);
            ActionFSM.ChangeState(strikeState, false);
            // wait until state is no longer Strike
            while (ActionFSM.currentState == strikeState)
            {
                yield return null;
            }
            target.Value = strikeState.target;
            canceled.Value = strikeState.canceled;
        } else
        {
            Debug.LogWarning("[FSM_API] Cannot initiate Strike while FSM is in transition or not in Idle state.");
            canceled.Value = true;
        }
        
    }
}