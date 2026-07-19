using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Contains an immutable ordered grid path.</summary>
    public sealed class PathSelection : IEquatable<PathSelection>
    {
        private readonly IReadOnlyList<GridPosition> positions;

        /// <summary>Gets the path positions from starting cell through destination.</summary>
        public IReadOnlyList<GridPosition> Positions => positions;

        /// <summary>Creates an immutable path selection.</summary>
        /// <param name="positions">At least one position in traversal order.</param>
        public PathSelection(IEnumerable<GridPosition> positions)
        {
            if (positions == null)
                throw new ArgumentNullException(nameof(positions));
            GridPosition[] copied = positions.ToArray();
            if (copied.Length == 0)
                throw new ArgumentException("A selected path cannot be empty.", nameof(positions));
            this.positions = Array.AsReadOnly(copied);
        }

        /// <inheritdoc/>
        public bool Equals(PathSelection other) =>
            other != null && positions.SequenceEqual(other.positions);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PathSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => SelectionCollections.OrderedHashCode(positions);
    }

    /// <summary>Contains one selected grid cell.</summary>
    public readonly struct GridCellSelection : IEquatable<GridCellSelection>
    {
        /// <summary>Gets the selected cell.</summary>
        public GridPosition Cell { get; }

        /// <summary>Creates a grid-cell selection.</summary>
        /// <param name="cell">The selected rules-grid position.</param>
        public GridCellSelection(GridPosition cell) => Cell = cell;

        /// <inheritdoc/>
        public bool Equals(GridCellSelection other) => Cell.Equals(other.Cell);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GridCellSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Cell.GetHashCode();
    }

    /// <summary>Defines an area's origin and the distinct cell toward which it faces.</summary>
    public readonly struct AreaOrientation : IEquatable<AreaOrientation>
    {
        /// <summary>Gets the cell from which the area is anchored.</summary>
        public GridPosition Origin { get; }

        /// <summary>Gets the distinct cell defining the area's facing direction.</summary>
        public GridPosition Facing { get; }

        /// <summary>Gets whether this is an uninitialized orientation with no direction.</summary>
        public bool IsEmpty => Origin.Equals(Facing);

        /// <summary>Creates an area orientation.</summary>
        /// <param name="origin">The template origin.</param>
        /// <param name="facing">A distinct cell that defines direction.</param>
        public AreaOrientation(GridPosition origin, GridPosition facing)
        {
            if (origin.Equals(facing))
                throw new ArgumentException(
                    "Area facing must differ from its origin.",
                    nameof(facing)
                );
            Origin = origin;
            Facing = facing;
        }

        /// <inheritdoc/>
        public bool Equals(AreaOrientation other) =>
            Origin.Equals(other.Origin) && Facing.Equals(other.Facing);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is AreaOrientation other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Origin, Facing);
    }

    /// <summary>Combines one data-defined area template with its selected orientation.</summary>
    public readonly struct AreaSelection : IEquatable<AreaSelection>
    {
        /// <summary>Gets the selected area template.</summary>
        public AreaTemplateId Template { get; }

        /// <summary>Gets the selected origin and facing.</summary>
        public AreaOrientation Orientation { get; }

        /// <summary>Creates a complete area selection.</summary>
        /// <param name="template">The selected template.</param>
        /// <param name="orientation">Its non-empty orientation.</param>
        public AreaSelection(AreaTemplateId template, AreaOrientation orientation)
        {
            if (template.IsEmpty)
                throw new ArgumentException("An area template is required.", nameof(template));
            if (orientation.IsEmpty)
                throw new ArgumentException(
                    "An area orientation is required.",
                    nameof(orientation)
                );
            Template = template;
            Orientation = orientation;
        }

        /// <inheritdoc/>
        public bool Equals(AreaSelection other) =>
            Template.Equals(other.Template) && Orientation.Equals(other.Orientation);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is AreaSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Template, Orientation);
    }
}
