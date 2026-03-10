using System;
using System.Collections;
using UnityEngine;

public class Grid : GridAPI
{
    GridFSM GridFSM = new GridFSM();
    // cancel current action
    public override void CancelCurrentAction()
    {
        if (!GridFSM.isInTransition)
        {
            GridFSM.ChangeState(GridFSM.idleState);
        }
    }

    // contstruct stride
    public override IEnumerator Stride(GameObject character, CoroutineResult<bool> canceled)
    {
        if (!GridFSM.isInTransition && GridFSM.currentState == GridFSM.idleState)
        {
            StateStride strideState = new StateStride(character, Controller);
            if (GridFSM.ChangeState(strideState))
            {
                // wait until state is no longer Stride
                while (GridFSM.currentState == strideState)
                {
                    yield return null;
                }
                // return cancelled status
                canceled.Value = GridFSM.canceled;
            }
            else
            {
                canceled.Value = true;
            }
        }
        else
        {
            canceled.Value = true;
        }
    }
    // construct Strike
    public override IEnumerator Strike(GameObject attacker, int range, CoroutineResult<GameObject> target, CoroutineResult<bool> canceled)
    {
        if(!GridFSM.isInTransition && GridFSM.currentState == GridFSM.idleState)
        {
            StateStrike strikeState = new StateStrike(attacker, range, Controller);
            if (GridFSM.ChangeState(strikeState))
            {
                // wait until state is no longer Strike
                while (GridFSM.currentState == strikeState)
                {
                    yield return null;
                }
                target.Value = strikeState.target;
                canceled.Value = GridFSM.canceled;
            }
        } else
        {
            canceled.Value = true;
        }
        
    }
    void Update()
    {

        GridFSM.FSMUpdate(Controller);
    }
    void FixedUpdate()
    {
        GridFSM.FSMFixedUpdate();
    }

}