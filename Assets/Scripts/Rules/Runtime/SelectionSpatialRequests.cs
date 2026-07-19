using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Requests a path for one mover from a start cell to an offered destination.</summary>
    public sealed class PathSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<GridPosition> destinations;

        /// <summary>Gets the creature whose path is being selected.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the path's required starting cell.</summary>
        public GridPosition Start { get; }

        /// <summary>Gets allowed destination cells in presentation order.</summary>
        public IReadOnlyList<GridPosition> Destinations => destinations;

        /// <summary>Gets the maximum number of steps after the starting cell.</summary>
        public int MaximumSteps { get; }

        /// <summary>Creates a path request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="mover">The creature selecting a path.</param>
        /// <param name="start">The required starting cell.</param>
        /// <param name="destinations">Distinct allowed destination cells.</param>
        /// <param name="maximumSteps">The positive maximum step count.</param>
        public PathSelectionRequest(
            SelectionRequestId id,
            CreatureId mover,
            GridPosition start,
            IEnumerable<GridPosition> destinations,
            int maximumSteps
        )
            : base(id)
        {
            if (mover.IsEmpty)
                throw new ArgumentException("A path mover is required.", nameof(mover));
            if (maximumSteps <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumSteps));
            Mover = mover;
            Start = start;
            this.destinations = SelectionCollections.CopyDistinct(
                destinations,
                nameof(destinations),
                _ => false,
                string.Empty
            );
            MaximumSteps = maximumSteps;
        }

        internal bool Accepts(PathSelection selection) =>
            selection != null
            && selection.Positions.Count >= 2
            && selection.Positions.Count <= MaximumSteps + 1
            && selection.Positions[0].Equals(Start)
            && destinations.Contains(selection.Positions[selection.Positions.Count - 1]);
    }

    /// <summary>Requests one grid cell from a deterministic candidate set.</summary>
    public sealed class GridCellSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<GridPosition> candidates;

        /// <summary>Gets selectable cells in presentation order.</summary>
        public IReadOnlyList<GridPosition> Candidates => candidates;

        /// <summary>Creates a grid-cell request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="candidates">Distinct selectable cells.</param>
        public GridCellSelectionRequest(SelectionRequestId id, IEnumerable<GridPosition> candidates)
            : base(id) =>
            this.candidates = SelectionCollections.CopyDistinct(
                candidates,
                nameof(candidates),
                _ => false,
                string.Empty
            );

        internal bool Accepts(GridCellSelection selection) => candidates.Contains(selection.Cell);
    }

    /// <summary>Requests an area template, origin, and non-zero facing direction.</summary>
    public sealed class AreaSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<AreaTemplateId> templates;
        private readonly IReadOnlyList<GridPosition> origins;

        /// <summary>Gets selectable area templates in presentation order.</summary>
        public IReadOnlyList<AreaTemplateId> Templates => templates;

        /// <summary>Gets permitted origin cells in presentation order.</summary>
        public IReadOnlyList<GridPosition> Origins => origins;

        /// <summary>Creates an area request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="templates">Distinct non-empty template IDs.</param>
        /// <param name="origins">Distinct permitted origin cells.</param>
        public AreaSelectionRequest(
            SelectionRequestId id,
            IEnumerable<AreaTemplateId> templates,
            IEnumerable<GridPosition> origins
        )
            : base(id)
        {
            this.templates = SelectionCollections.CopyDistinct(
                templates,
                nameof(templates),
                template => template.IsEmpty,
                "Area templates cannot contain an empty ID."
            );
            this.origins = SelectionCollections.CopyDistinct(
                origins,
                nameof(origins),
                _ => false,
                string.Empty
            );
        }

        internal bool Accepts(AreaSelection selection) =>
            templates.Contains(selection.Template)
            && origins.Contains(selection.Orientation.Origin)
            && !selection.Orientation.IsEmpty;
    }
}
