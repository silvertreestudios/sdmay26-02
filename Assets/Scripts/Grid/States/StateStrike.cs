using UnityEngine;
using System.Collections.Generic;
using Game.Creature;

namespace GridPrivate
{
    public class StateStrike : GridFSMState
    {
        protected GridFSM Fsm;
        // target character
        GameObject Character;
        CoroutineResult<GameObject> Selection;
        protected GridAPIPrivate GridAPI = (GridAPIPrivate)GridPublic.GridAPI.GetInstance();
        protected IPathfinder Pathfinder;
        protected Tile[,] Tiles;
        protected List<GameObject> OccupantsInRange = new List<GameObject>();
        protected Vector3Int HoverCell;
        protected float Range;
        protected Vector3Int StartPosition;


        // compact constructor
        public StateStrike(GameObject character, float range, CoroutineResult<GameObject> selection, GridFSM fsm)
        {
            Fsm = fsm;
            Character = character;
            Selection = selection;
            Tiles = GridAPI.GetTiles();
            StartPosition = Vector3Int.RoundToInt(Character.transform.position);
            Pathfinder = GridAPI.GetPathfinder();
            Pathfinder.Search(Character, StartPosition);
            Range = range;
        }
        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            Debug.Log("Enter");
            base.Enter(fsm);
            canCancel = true;
            OccupantsInRange.Clear();
            AIActionController ai = Character.GetComponent<AIActionController>();
            if (ai != null)
            {

                // grab best target from the AI's controller, this should be set during its decision making process
                if (ai.BestTarget == null)
                    Debug.LogWarning("AI has no target, skipping strike");
                else
                    Selection.Value = ai.BestTarget;
                this.fsm.ChangeState(this.fsm.IdleState);
            }
            else
            {
                List<Vector3Int> inRange = Pathfinder.CalculateEmination(StartPosition, Range);
                OnHighlightRange.Invoke(inRange);
                OnHover.AddListener(Hover);

                foreach(Vector3Int cell in inRange)
                {
                    Tile tile = Tiles[cell.x, cell.z];
                    if (tile != null)
                    {
                        foreach(GameObject occupant in tile.Occupants)
                            OccupantsInRange.Add(occupant);
                    }
                }
                OccupantsInRange.Remove(Character);
            }
        }

        public override void Exit()
        {
            OccupantsInRange.Clear();
            OnHover.RemoveListener(Hover);
            OnHighlightRangeEnd.Invoke();
        }
        public override void Leftclick()
        {
            Tile tile = Tiles[HoverCell.x, HoverCell.z];
            Debug.Log("Tile: " + HoverCell + " " + (tile != null));
            if (tile != null)
            {
                Debug.Log("Count: " + tile.Occupants.Count);
                if (tile.Occupants.Count > 0)
                    Selection.Value = tile.Occupants[0];

                fsm.ChangeState(fsm.IdleState);
            }
        }

        public override void Rightclick()
        {
            if (!canCancel) return;
            UniversalEvents.OnCancel.Invoke();
        }

        protected void Hover(List<Vector3Int> hoverList)
        {
            if (hoverList.Count > 0)
                HoverCell = hoverList[0];
        }
    }
}