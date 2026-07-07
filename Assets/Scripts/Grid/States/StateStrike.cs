using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using GridPublic;

namespace GridPrivate
{
    public class StateStrike : GridFSMState
    {
        protected GridFSM Fsm;
        private readonly GameObject Character;
        private readonly StrikeTargetRequest Request;
        private readonly CoroutineResult<StrikeTargetResult> Selection;
        protected GridAPIPrivate GridAPI = (GridAPIPrivate)GridPublic.GridAPI.GetInstance();
        protected IPathfinder Pathfinder;
        protected Tile[,] Tiles;
        protected List<Vector3Int> inRange = new List<Vector3Int>();
        protected List<GameObject> OccupantsInRange = new List<GameObject>();
        protected Vector3Int HoverCell;
        protected Vector3Int StartPosition;

        public StateStrike(GameObject character, StrikeTargetRequest request, CoroutineResult<StrikeTargetResult> selection, GridFSM fsm)
        {
            Fsm = fsm;
            Character = character;
            Request = request ?? new StrikeTargetRequest();
            Selection = selection;
            Tiles = GridAPI.GetTiles();
            StartPosition = Vector3Int.RoundToInt(Character.transform.position);
            Pathfinder = GridAPI.GetPathfinder();
            Pathfinder.Search(Character, StartPosition);
        }

        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            base.Enter(fsm);
            canCancel = true;
            OccupantsInRange.Clear();
            AIActionController ai = Character.GetComponent<AIActionController>();
            if (ai != null)
            {
                if (ai.BestTarget == null)
                    Debug.LogWarning("AI has no target, skipping strike");
                else
                    Selection.Value = StrikeTargeting.Evaluate(Character, ai.BestTarget, Tiles, Request);
                CoroutineRunner.Run(ChangeToIdle());
            }
            else
            {
                inRange = StrikeTargeting.CellsInRange(Tiles, StartPosition, Request);
                OnHighlightRange.Invoke(inRange);
                OnHover.AddListener(Hover);

                foreach (Vector3Int cell in inRange)
                {
                    Tile tile = Tiles[cell.x, cell.z];
                    if (tile == null)
                        continue;

                    foreach (GameObject occupant in tile.Occupants)
                    {
                        if (occupant == Character)
                            continue;

                        StrikeTargetResult result = StrikeTargeting.Evaluate(Character, occupant, Tiles, Request);
                        if (result != null && result.IsLegal)
                            OccupantsInRange.Add(occupant);
                    }
                }
            }
        }

        public override void Exit()
        {
            OccupantsInRange.Clear();
            OnHover.RemoveListener(Hover);
            OnHighlightRangeEnd.Invoke();
            OnActionComplete.Invoke();
        }

        public override void Leftclick()
        {
            if (HoverCell.x < 0 || HoverCell.z < 0 || HoverCell.x >= Tiles.GetLength(0) || HoverCell.z >= Tiles.GetLength(1))
                return;

            Tile tile = Tiles[HoverCell.x, HoverCell.z];
            if (tile == null || !inRange.Contains(HoverCell))
                return;

            if (tile.Occupants.Count == 0)
            {
                OnActionConfirm.Invoke();
                fsm.ChangeState(fsm.IdleState);
                return;
            }

            StrikeTargetResult result = StrikeTargeting.Evaluate(Character, tile.Occupants[0], Tiles, Request);
            if (result == null || !result.IsLegal)
                return;

            Selection.Value = result;
            OnActionConfirm.Invoke();
            fsm.ChangeState(fsm.IdleState);
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

        protected IEnumerator ChangeToIdle()
        {
            yield return null;
            fsm.ChangeState(this.fsm.IdleState);
        }
    }
}