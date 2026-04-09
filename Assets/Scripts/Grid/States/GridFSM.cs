using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UniversalEvents;

namespace GridPrivate
{
    public class GridFSM : FiniteStateMachine<GridFSMState>
    {


        //public idle to the states themselves can set the FSM back to idle upon exiting, may change this to be handled by the API
        public StateIdle idleState { get; private set; } = new StateIdle();
        public bool canceled = false;

        private float timeSinceLastClick = 0f;
        private float lastClickTime = 0f;
        private GridFSMState QueuedState = null;

        public GridFSM()
        {
            currentState = idleState;
            OnCancel.AddListener(() => ChangeState(new StateIdle()));
        }

        public override bool ChangeState(GridFSMState newState)
        {
            if (QueuedState != null && newState is StateIdle)
            {
                newState = QueuedState;
                QueuedState = null;
            }
            if (!currentState.canCancel)
                return false;
            return base.ChangeState(newState);
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
                if (timeSinceLastClick <= controller.doubleClickTime)
                {
                    currentState.DoubleLeftclick();
                }
                else
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
}