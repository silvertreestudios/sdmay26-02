using UnityEngine;
using System.Collections.Generic;

namespace GridPrivate
{
    public class StateStrike : GridFSMState
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
        private Vector3Int startCell;


        // compact constructor
        public StateStrike(GameObject character, int range, GridCharacterController3D controller)
        {
            this.controller = controller;
            this.character = character;
            this.range = range;
        }
        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            base.Enter(fsm);
            target = null;
            selection = null;
            canceled = false;
            occupantsInRange.Clear();
            startCell = controller.coordinateConverter.GetCharacterCell(character);
            controller.isProcessingTurn = false;
            AIActionController ai = character.GetComponent<AIActionController>();
            if (ai != null)
            {

                // grab best target from the AI's controller, this should be set during its decision making process
                if (ai.bestTarget == null)
                {
                    Debug.LogWarning("AI has no target, skipping strike");
                }
                else
                {
                    target = ai.bestTarget;
                    Debug.Log($"[State_Strike] Target acquired: {target.name}");
                }
                this.fsm.ChangeState(this.fsm.idleState);
            }
            else
            {
                controller.rangeHighlighter.UpdateHighlights(startCell, range, showAttackRange: true);
                //currently I am filtering out friendly targets in StrikeOccupantsInArea
                //but this may not be 100% accurate to the pathfinder 2E rules
                //TODO talk to Cole and Chris about this implementation
                occupantsInRange = controller.StrikeOccupantsInArea(character, range);
            }

        }
        public override bool Exit()
        {
            fsm.canceled = canceled;
            occupantsInRange.Clear();
            controller.rangeHighlighter.ClearHighlights();
            return true;
        }
        public override void Leftclick()
        {
            if (controller.TryGetClickedCell(controller.currentCamera, out Vector3Int targetCell))
            {
                List<GameObject> occupantsInCell = controller.gridMemory.GetOccupantsInArea(new List<Vector3Int> { targetCell });
                if (occupantsInCell.Count == 0)
                {
                    Debug.Log("[State_Strike] No occupants in the selected cell.");
                }
                else
                {
                    selection = occupantsInCell[0];
                    Debug.Log($"[State_Strike] Target preview: {selection.name}");
                }
            }
        }

        public override void DoubleLeftclick()
        {
            if (occupantsInRange.Contains(selection))
            {
                target = selection;
                //Debug.Log($"[State_Strike] Target confirmed: {target.name}");
                fsm.ChangeState(fsm.idleState);
            }
            else
            {
                Debug.Log("[State_Strike] Selected an invalid target.");
            }
        }
        public override void Rightclick()
        {
            // cancel action when right clicking
            //Debug.Log("[State_Strike] Action cancelled");
            selection = null;
            target = null;
            canceled = true;
            fsm.ChangeState(fsm.idleState);
        }


    }
}