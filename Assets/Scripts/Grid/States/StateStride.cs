using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using GridPublic;
using UnityEngine;

namespace GridPrivate
{
    /// <summary>Collects one immutable Stride path without applying movement or rules.</summary>
    public sealed class StateStride : GridFSMState
    {
        private readonly GameObject character;
        private readonly StridePathSelectionRequest request;
        private readonly CoroutineResult<SelectionOutcome<MovementPath>> selection;
        private readonly GridAPIPrivate grid = (GridAPIPrivate)GridAPI.GetInstance();
        private IPathfinder pathfinder;
        private Tile[,] tiles;
        private MovementPath pendingPath;

        /// <summary>Creates a player-facing selector for one frozen Stride request.</summary>
        /// <param name="character">The Unity token selecting a path.</param>
        /// <param name="request">The immutable rules preview constraints.</param>
        /// <param name="selection">The structural result completed by confirmation or cancel.</param>
        public StateStride(
            GameObject character,
            StridePathSelectionRequest request,
            CoroutineResult<SelectionOutcome<MovementPath>> selection
        )
        {
            this.character = character ?? throw new ArgumentNullException(nameof(character));
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            this.selection = selection ?? throw new ArgumentNullException(nameof(selection));
            this.selection.Value = SelectionOutcome<MovementPath>.Cancelled;
        }

        /// <inheritdoc/>
        public override void Enter(FiniteStateMachine<GridFSMState> stateMachine)
        {
            base.Enter(stateMachine);
            canCancel = true;
            tiles = grid.GetTiles();
            pathfinder = grid.GetPathfinder();
            Vector3Int origin = ToUnity(request.Origin);
            pathfinder.Search(character, origin);

            List<Vector3Int> reachable = pathfinder.InRange(
                character,
                origin,
                request.MaximumDistance.Feet / 5.0f
            );
            reachable.Remove(origin);
            reachable.RemoveAll(cell => !TryCreateAcceptedPath(cell, out _));
            OnHighlightRange.Invoke(reachable);
            OnHover.AddListener(HighlightPath);
            OnHoverEnd.AddListener(HideHighlightPath);
        }

        /// <inheritdoc/>
        public override void Exit()
        {
            OnHighlightRangeEnd.Invoke();
            OnHover.RemoveListener(HighlightPath);
            OnHoverEnd.RemoveListener(HideHighlightPath);
            HideHighlightPath();
        }

        /// <inheritdoc/>
        public override void Leftclick()
        {
            if (pendingPath == null || !canCancel)
                return;

            selection.Value = SelectionOutcome<MovementPath>.Completed(pendingPath);
            OnActionConfirm.Invoke();
            fsm.ChangeState(fsm.IdleState);
        }

        /// <inheritdoc/>
        public override void Rightclick()
        {
            if (canCancel)
                UniversalEvents.OnCancel.Invoke();
        }

        private void HighlightPath(List<Vector3Int> hover)
        {
            pendingPath = null;
            if (hover == null || hover.Count == 0)
            {
                HideHighlightPath();
                return;
            }

            Vector3Int destination = hover[0];
            if (!IsInBounds(destination) || !TryCreateAcceptedPath(destination, out pendingPath))
            {
                HideHighlightPath();
                return;
            }

            OnPreviewPath.Invoke(pendingPath.Steps.Select(ToUnity).ToList());
        }

        private bool TryCreateAcceptedPath(Vector3Int destination, out MovementPath movementPath)
        {
            movementPath = null;
            if (!IsInBounds(destination) || tiles[destination.x, destination.z] == null)
                return false;

            List<PathNode> path = pathfinder.Find(destination);
            if (path == null || path.Count <= 1)
                return false;

            MovementPath candidate = new MovementPath(
                request.Origin,
                path.Skip(1).Select(node => ToRules(node.Location))
            );
            if (!request.Accepts(candidate))
                return false;

            movementPath = candidate;
            return true;
        }

        private bool IsInBounds(Vector3Int cell) =>
            cell.x >= 0
            && cell.z >= 0
            && cell.x < tiles.GetLength(0)
            && cell.z < tiles.GetLength(1);

        private static void HideHighlightPath() => OnPreviewPath.Invoke(new List<Vector3Int>());

        private static GridPosition ToRules(Vector3Int value) =>
            new GridPosition(value.x, value.y, value.z);

        private static Vector3Int ToUnity(GridPosition value) =>
            new Vector3Int(value.X, value.Y, value.Z);
    }
}
