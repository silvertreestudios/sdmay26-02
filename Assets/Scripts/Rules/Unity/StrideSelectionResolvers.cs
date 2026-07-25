using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using GridPublic;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>Resolves Stride paths through the live player grid-selection state.</summary>
    internal sealed class PlayerStrideSelectionResolver : ISelectionResolver
    {
        private readonly GameObject character;
        private readonly GridAPI grid;

        internal PlayerStrideSelectionResolver(GameObject character, GridAPI grid)
        {
            this.character = character ?? throw new ArgumentNullException(nameof(character));
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<TSelection>> Select<TSelection>(
            ActionSelectionRequest<TSelection> request,
            CancellationToken cancellationToken
        )
        {
            if (
                request is not StridePathSelectionRequest strideRequest
                || typeof(TSelection) != typeof(MovementPath)
            )
            {
                return new ValueTask<SelectionOutcome<TSelection>>(
                    SelectionOutcome<TSelection>.Invalid(
                        "The player Stride resolver received an unsupported request."
                    )
                );
            }

            return new ValueTask<SelectionOutcome<TSelection>>(
                ResolveAndConvert<TSelection>(strideRequest, cancellationToken)
            );
        }

        private async Task<SelectionOutcome<TSelection>> ResolveAndConvert<TSelection>(
            StridePathSelectionRequest request,
            CancellationToken cancellationToken
        )
        {
            if (cancellationToken.IsCancellationRequested)
                return SelectionOutcome<TSelection>.Cancelled;

            CoroutineResult<SelectionOutcome<MovementPath>> result = new CoroutineResult<
                SelectionOutcome<MovementPath>
            >
            {
                Value = SelectionOutcome<MovementPath>.Cancelled,
            };
            await UnityCoroutineTask.Run(SelectPath(request, result));
            if (cancellationToken.IsCancellationRequested)
                return SelectionOutcome<TSelection>.Cancelled;
            return (SelectionOutcome<TSelection>)(object)result.Value;
        }

        private IEnumerator SelectPath(
            StridePathSelectionRequest request,
            CoroutineResult<SelectionOutcome<MovementPath>> result
        ) => grid.SelectStridePath(character, request, result);
    }

    /// <summary>Resolves the longest legal Stride prefix from one AI-planned grid path.</summary>
    internal sealed class PlannedStrideSelectionResolver : ISelectionResolver
    {
        private readonly IReadOnlyList<GridPosition> plannedCells;

        internal PlannedStrideSelectionResolver(IEnumerable<Vector3Int> plannedCells)
        {
            if (plannedCells == null)
                throw new ArgumentNullException(nameof(plannedCells));
            this.plannedCells = plannedCells
                .Select(cell => new GridPosition(cell.x, cell.y, cell.z))
                .ToArray();
        }

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<TSelection>> Select<TSelection>(
            ActionSelectionRequest<TSelection> request,
            CancellationToken cancellationToken
        )
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<SelectionOutcome<TSelection>>(
                    SelectionOutcome<TSelection>.Cancelled
                );
            }
            if (
                request is not StridePathSelectionRequest strideRequest
                || typeof(TSelection) != typeof(MovementPath)
            )
            {
                return new ValueTask<SelectionOutcome<TSelection>>(
                    SelectionOutcome<TSelection>.Invalid(
                        "The planned Stride resolver received an unsupported request."
                    )
                );
            }

            SelectionOutcome<MovementPath> outcome = Resolve(strideRequest);
            return new ValueTask<SelectionOutcome<TSelection>>(
                (SelectionOutcome<TSelection>)(object)outcome
            );
        }

        private SelectionOutcome<MovementPath> Resolve(StridePathSelectionRequest request)
        {
            if (plannedCells.Count <= 1 || plannedCells[0] != request.Origin)
            {
                return SelectionOutcome<MovementPath>.Invalid(
                    "The AI planner did not provide a path from the Stride origin."
                );
            }

            for (int count = plannedCells.Count - 1; count > 0; count--)
            {
                MovementPath candidate = new MovementPath(
                    request.Origin,
                    plannedCells.Skip(1).Take(count)
                );
                if (request.Accepts(candidate))
                    return SelectionOutcome<MovementPath>.Completed(candidate);
            }

            return SelectionOutcome<MovementPath>.Invalid(
                "The AI planner did not provide a legal Stride prefix."
            );
        }
    }
}
