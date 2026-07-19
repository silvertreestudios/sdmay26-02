using System.Collections;
using System.Collections.Generic;
using GridPublic;
using UnityEngine;

namespace GridPrivate
{
    public class StateAreaTarget : GridFSMState
    {
        private readonly AreaTargetSource Source;
        private readonly AreaTargetRequest Request;
        private readonly CoroutineResult<AreaTargetResult> Selection;
        private readonly GridFSM Fsm;
        private readonly GridAPIPrivate GridAPI = (GridAPIPrivate)GridPublic.GridAPI.GetInstance();
        private readonly Tile[,] Tiles;
        private readonly Vector3Int StartPosition;
        private AreaTargetResult PendingResult;

        public StateAreaTarget(
            GameObject character,
            AreaTargetRequest request,
            CoroutineResult<AreaTargetResult> selection,
            GridFSM fsm
        )
            : this(new AreaTargetSource(character), request, selection, fsm) { }

        public StateAreaTarget(
            AreaTargetSource source,
            AreaTargetRequest request,
            CoroutineResult<AreaTargetResult> selection,
            GridFSM fsm
        )
        {
            Source = source ?? new AreaTargetSource();
            Request = request ?? new AreaTargetRequest();
            Selection = selection;
            Fsm = fsm;
            Tiles = GridAPI.GetTiles();
            StartPosition = Source.OriginCell;
        }

        public override void Enter(FiniteStateMachine<GridFSMState> fsm)
        {
            base.Enter(fsm);
            canCancel = true;

            if (
                Source.SourceObject != null
                && Source.SourceObject.GetComponent<AIActionController>() != null
            )
            {
                Debug.LogWarning("AI area targeting is not implemented in this grid state.");
                CoroutineRunner.Run(ChangeToIdle());
                return;
            }

            if (Request.Shape == AreaShape.Emanation)
            {
                AreaPlacement placement = new()
                {
                    Shape = Request.Shape,
                    OriginCell = StartPosition,
                    OriginCorner = new Vector2Int(StartPosition.x, StartPosition.z),
                    Direction = AreaDirection.East,
                };
                Preview(placement);
                return;
            }

            OnHighlightRange.Invoke(
                AreaTargeting.CellsInPlacementRange(Tiles, StartPosition, Request)
            );
            OnGridHover.AddListener(HandleGridHover);
            OnHover.AddListener(HandleLegacyHover);
            OnHoverEnd.AddListener(ClearPreview);
        }

        public override void Exit()
        {
            OnGridHover.RemoveListener(HandleGridHover);
            OnHover.RemoveListener(HandleLegacyHover);
            OnHoverEnd.RemoveListener(ClearPreview);
            OnPreviewAreaEnd.Invoke();
            OnHighlightRangeEnd.Invoke();
            OnActionComplete.Invoke();
        }

        public override void Leftclick()
        {
            if (PendingResult == null || !PendingResult.IsLegal)
                return;

            if (Selection != null)
                Selection.Value = PendingResult;
            OnActionConfirm.Invoke();
            fsm.ChangeState(fsm.IdleState);
        }

        public override void Rightclick()
        {
            if (!canCancel)
                return;
            UniversalEvents.OnCancel.Invoke();
        }

        private void HandleGridHover(GridHoverInfo hover)
        {
            AreaPlacement placement = AreaTargeting.PlacementFromHover(Source, Request, hover);
            Preview(placement);
        }

        private void HandleLegacyHover(List<Vector3Int> hover)
        {
            if (hover == null || hover.Count == 0)
            {
                ClearPreview();
                return;
            }

            Vector3Int cell = hover[0];
            GridHoverInfo info = new()
            {
                Cell = cell,
                WorldPosition = new Vector3(cell.x, cell.y, cell.z),
                NearestCorner = new Vector2Int(cell.x, cell.z),
            };
            HandleGridHover(info);
        }

        private void Preview(AreaPlacement placement)
        {
            PendingResult = AreaTargeting.Evaluate(Source, Tiles, Request, placement);
            if (PendingResult == null)
            {
                OnPreviewAreaEnd.Invoke();
                return;
            }
            OnPreviewArea.Invoke(PendingResult.Cells);
        }

        private void ClearPreview()
        {
            PendingResult = null;
            OnPreviewAreaEnd.Invoke();
        }

        private IEnumerator ChangeToIdle()
        {
            yield return null;
            Fsm.ChangeState(Fsm.IdleState);
        }
    }
}
