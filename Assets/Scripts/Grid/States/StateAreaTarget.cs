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

        // Absence means no pending aim. Pure output is independent of Unity object lifetime.
        private AreaSelectionSnapshot PendingResult;
        private AreaPlacement PendingPlacement;
        private readonly GameObject OriginalSource;
        private readonly bool HasObjectSource;

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
            OriginalSource = Source.SourceObject;
            HasObjectSource = !ReferenceEquals(OriginalSource, null);
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
            OnHoverEnd.AddListener(ClearPreview);
        }

        public override void Exit()
        {
            PendingResult = null;
            PendingPlacement = null;
            OnGridHover.RemoveListener(HandleGridHover);
            OnHoverEnd.RemoveListener(ClearPreview);
            OnPreviewAreaEnd.Invoke();
            OnHighlightRangeEnd.Invoke();
            OnActionComplete.Invoke();
        }

        public override void Leftclick()
        {
            if (!IsSourceValid())
            {
                fsm.ChangeState(fsm.IdleState);
                return;
            }
            if (PendingResult == null || PendingPlacement == null)
                return;

            AreaTargetCapture refreshed = AreaTargetCapture.Capture(
                Source,
                Tiles,
                Request,
                PendingPlacement
            );
            AreaSelectionSnapshot current = AreaTargeting.Evaluate(refreshed.Snapshot);
            bool unchanged = Equivalent(PendingResult, current);
            if (!unchanged || !current.IsLegal)
            {
                ShowPreview(current);
                return;
            }
            if (Selection != null)
                Selection.Value = refreshed.Resolve();
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

        private void Preview(AreaPlacement placement)
        {
            if (!IsSourceValid() || placement == null)
            {
                ClearPreview();
                return;
            }
            PendingPlacement = placement;
            AreaTargetCapture capture = AreaTargetCapture.Capture(
                Source,
                Tiles,
                Request,
                placement
            );
            ShowPreview(AreaTargeting.Evaluate(capture.Snapshot));
        }

        private bool IsSourceValid() =>
            ReferenceEquals(Tiles, GridAPI.GetTiles())
            && ReferenceEquals(OriginalSource, Source.SourceObject)
            && (!HasObjectSource || OriginalSource != null);

        private void ShowPreview(AreaSelectionSnapshot result)
        {
            PendingResult = result;
            OnHighlightRange.Invoke(AreaTargeting.CellsInPlacementRange(result.Snapshot));
            if (result.IsLegal)
                OnPreviewArea.Invoke(new List<Vector3Int>(result.Cells));
            else
                OnPreviewAreaEnd.Invoke();
        }

        // Capture IDs are not world revisions. Compare semantics including blocked candidates
        // and cover rays so confirmation cannot silently accept a changed target set.
        private static bool Equivalent(
            AreaSelectionSnapshot previous,
            AreaSelectionSnapshot current
        )
        {
            TargetingSnapshot a = previous.Snapshot;
            TargetingSnapshot b = current.Snapshot;
            if (
                a.SourceCell != b.SourceCell
                || a.SourceEntityId != b.SourceEntityId
                || a.Shape != b.Shape
                || a.SizeFeet != b.SizeFeet
                || a.RangeFeet != b.RangeFeet
                || a.LineWidthFeet != b.LineWidthFeet
                || a.IncludeCenter != b.IncludeCenter
                || a.RequiresLineOfEffect != b.RequiresLineOfEffect
                || a.PlacementShape != b.PlacementShape
                || a.PlacementOriginCell != b.PlacementOriginCell
                || a.OriginCorner != b.OriginCorner
                || a.Direction != b.Direction
                || previous.Cells.Count != current.Cells.Count
                || previous.Creatures.Count != current.Creatures.Count
            )
                return false;
            for (int i = 0; i < previous.Cells.Count; i++)
                if (previous.Cells[i] != current.Cells[i])
                    return false;
            for (int i = 0; i < previous.Creatures.Count; i++)
                if (!previous.Creatures[i].Equals(current.Creatures[i]))
                    return false;
            return true;
        }

        private void ClearPreview()
        {
            PendingResult = null;
            PendingPlacement = null;
            OnPreviewAreaEnd.Invoke();
        }

        private IEnumerator ChangeToIdle()
        {
            yield return null;
            Fsm.ChangeState(Fsm.IdleState);
        }
    }
}
