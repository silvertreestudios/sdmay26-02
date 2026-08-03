using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UniversalEvents;

namespace GridPrivate
{
    public class GridFSM : FiniteStateMachine<GridFSMState>
    {
        public StateIdle IdleState { get; private set; } = new StateIdle();

        private float TimeSinceLastClick = 0f;
        private float LastClickTime = 0f;
        private GridFSMState QueuedState = null;
        private Func<bool> tryCancelIdleOperation = () => false;

        public GridFSM()
        {
            CurrentState = IdleState;
            IdleState.Enter(this);
            OnCancel.AddListener(() =>
            {
                if (CurrentState is StateIdle)
                    return;

                Debug.Log("Action Cancel");
                if (ChangeState(IdleState))
                    OnActionCancel.Invoke();
            });
        }

        /// <summary>Sets the owner-specific cancellation boundary used by idle right-click.</summary>
        internal void BindIdleCancellation(Func<bool> tryCancel) =>
            tryCancelIdleOperation =
                tryCancel ?? throw new ArgumentNullException(nameof(tryCancel));

        /// <summary>Attempts to cancel work that intentionally remains in the idle grid state.</summary>
        internal bool TryCancelIdleOperation() => tryCancelIdleOperation();

        public override bool ChangeState(GridFSMState newState)
        {
            if (QueuedState != null && newState is StateIdle)
            {
                newState = QueuedState;
                QueuedState = null;
            }
            if (!CurrentState.canCancel)
                return false;
            return base.ChangeState(newState);
        }

        /// <summary>
        /// Gets whether runtime map replacement can proceed without interrupting an action.
        /// </summary>
        internal bool CanResetForGridRebind => !IsInTransition && CurrentState is StateIdle;

        /// <summary>
        /// Returns this subscribed FSM to its reusable idle state without creating another
        /// static-event subscription.
        /// </summary>
        internal bool TryResetForGridRebind()
        {
            if (!CanResetForGridRebind)
                return false;

            QueuedState = null;
            PreviousState = null;
            return true;
        }

        // Update is called once per frame
        public void InputUpdate()
        {
            if (HUDController.IsPointerOverHUD)
                return;

            TimeSinceLastClick = Time.time - LastClickTime;
            if (InputCompat.LeftClickDown())
            {
                LastClickTime = Time.time;
                if (TimeSinceLastClick <= 0.5)
                {
                    CurrentState.DoubleLeftclick();
                }
                else
                {
                    CurrentState.Leftclick();
                }
            }

            if (InputCompat.RightClickDown())
            {
                CurrentState.Rightclick();
            }
        }
    }
}
