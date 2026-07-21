using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies the PF2e terrain category that adds movement cost when a creature enters a cell.
    /// </summary>
    public enum TerrainCostKind
    {
        /// <summary>The cell adds no terrain surcharge.</summary>
        Normal,

        /// <summary>The cell adds 5 feet to the cost of entering it.</summary>
        Difficult,

        /// <summary>The cell adds 10 feet to the cost of entering it.</summary>
        GreaterDifficult,
    }

    /// <summary>
    /// Stores the immutable movement surcharge for one grid cell.
    /// </summary>
    /// <remarks>
    /// The fixed values implement the 5-foot PF2e grid model: difficult terrain adds 5 feet and
    /// greater difficult terrain adds 10 feet after the normal orthogonal or alternating diagonal
    /// cost is determined.
    /// </remarks>
    public readonly struct TerrainCost : IEquatable<TerrainCost>
    {
        /// <summary>Gets normal terrain with no surcharge.</summary>
        public static TerrainCost Normal { get; } = new TerrainCost(TerrainCostKind.Normal);

        /// <summary>Gets difficult terrain with a 5-foot surcharge.</summary>
        public static TerrainCost Difficult { get; } = new TerrainCost(TerrainCostKind.Difficult);

        /// <summary>Gets greater difficult terrain with a 10-foot surcharge.</summary>
        public static TerrainCost GreaterDifficult { get; } =
            new TerrainCost(TerrainCostKind.GreaterDifficult);

        private TerrainCost(TerrainCostKind kind)
        {
            Kind = kind;
            AdditionalFeet = kind switch
            {
                TerrainCostKind.Normal => 0,
                TerrainCostKind.Difficult => 5,
                TerrainCostKind.GreaterDifficult => 10,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        /// <summary>Gets the semantic terrain category.</summary>
        public TerrainCostKind Kind { get; }

        /// <summary>Gets the feet added to the cost of entering the cell.</summary>
        public int AdditionalFeet { get; }

        /// <inheritdoc/>
        public bool Equals(TerrainCost other) => Kind == other.Kind;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is TerrainCost other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (int)Kind;

        /// <summary>Compares two terrain costs by category.</summary>
        public static bool operator ==(TerrainCost left, TerrainCost right) => left.Equals(right);

        /// <summary>Compares two terrain costs by category.</summary>
        public static bool operator !=(TerrainCost left, TerrainCost right) => !left.Equals(right);
    }

    /// <summary>
    /// Defines inclusive integer bounds for an immutable rules grid.
    /// </summary>
    public readonly struct GridBounds : IEquatable<GridBounds>
    {
        /// <summary>Initializes inclusive minimum and maximum coordinates.</summary>
        /// <param name="minimum">The lowest valid coordinate on every axis.</param>
        /// <param name="maximum">The highest valid coordinate on every axis.</param>
        public GridBounds(GridPosition minimum, GridPosition maximum)
        {
            if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
                throw new ArgumentException("Grid bounds must have an ordered inclusive range.");

            Minimum = minimum;
            Maximum = maximum;
        }

        /// <summary>Gets the inclusive lower coordinate.</summary>
        public GridPosition Minimum { get; }

        /// <summary>Gets the inclusive upper coordinate.</summary>
        public GridPosition Maximum { get; }

        /// <summary>Determines whether a coordinate lies within every inclusive axis range.</summary>
        public bool Contains(GridPosition position) =>
            position.X >= Minimum.X
            && position.X <= Maximum.X
            && position.Y >= Minimum.Y
            && position.Y <= Maximum.Y
            && position.Z >= Minimum.Z
            && position.Z <= Maximum.Z;

        /// <inheritdoc/>
        public bool Equals(GridBounds other) =>
            Minimum == other.Minimum && Maximum == other.Maximum;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GridBounds other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Minimum, Maximum);

        /// <summary>Compares two bounds by their inclusive endpoints.</summary>
        public static bool operator ==(GridBounds left, GridBounds right) => left.Equals(right);

        /// <summary>Compares two bounds by their inclusive endpoints.</summary>
        public static bool operator !=(GridBounds left, GridBounds right) => !left.Equals(right);
    }

    /// <summary>
    /// Overrides the default traversable, normal-terrain behavior for one in-bounds grid cell.
    /// </summary>
    public readonly struct GridCell : IEquatable<GridCell>
    {
        /// <summary>Initializes one immutable topology override.</summary>
        /// <param name="position">The cell coordinate.</param>
        /// <param name="isBlocked">Whether ground movement cannot enter the cell.</param>
        /// <param name="terrainCost">The surcharge applied when the cell is entered.</param>
        public GridCell(GridPosition position, bool isBlocked, TerrainCost terrainCost)
        {
            Position = position;
            IsBlocked = isBlocked;
            TerrainCost = terrainCost;
        }

        /// <summary>Gets the overridden coordinate.</summary>
        public GridPosition Position { get; }

        /// <summary>Gets whether ground movement cannot enter this cell.</summary>
        public bool IsBlocked { get; }

        /// <summary>Gets the terrain surcharge for entering this cell.</summary>
        public TerrainCost TerrainCost { get; }

        /// <inheritdoc/>
        public bool Equals(GridCell other) =>
            Position == other.Position
            && IsBlocked == other.IsBlocked
            && TerrainCost == other.TerrainCost;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GridCell other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Position, IsBlocked, TerrainCost);

        /// <summary>Compares two cell overrides by value.</summary>
        public static bool operator ==(GridCell left, GridCell right) => left.Equals(right);

        /// <summary>Compares two cell overrides by value.</summary>
        public static bool operator !=(GridCell left, GridCell right) => !left.Equals(right);
    }

    /// <summary>
    /// Provides a Unity-free, immutable grid topology for movement validation.
    /// </summary>
    /// <remarks>
    /// In-bounds cells omitted from <paramref name="cells"/> are traversable normal terrain.
    /// Creature occupancy is deliberately absent and is derived from <see cref="RulesSnapshot.Positions"/>.
    /// </remarks>
    public sealed class GridTopology
    {
        private readonly IReadOnlyDictionary<GridPosition, GridCell> cells;

        /// <summary>Initializes a topology from inclusive bounds and sparse cell overrides.</summary>
        /// <param name="bounds">The complete valid coordinate range.</param>
        /// <param name="cells">Blocked or non-normal cells, with no duplicate coordinates.</param>
        public GridTopology(GridBounds bounds, IEnumerable<GridCell> cells)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));

            Dictionary<GridPosition, GridCell> copied = new Dictionary<GridPosition, GridCell>();
            foreach (GridCell cell in cells)
            {
                if (!bounds.Contains(cell.Position))
                    throw new ArgumentException(
                        $"Cell {cell.Position} lies outside the supplied topology bounds.",
                        nameof(cells)
                    );
                if (!copied.TryAdd(cell.Position, cell))
                    throw new ArgumentException(
                        $"Cell {cell.Position} is declared more than once.",
                        nameof(cells)
                    );
            }

            Bounds = bounds;
            this.cells = new ReadOnlyDictionary<GridPosition, GridCell>(copied);
        }

        /// <summary>Gets the inclusive coordinate bounds.</summary>
        public GridBounds Bounds { get; }

        /// <summary>Determines whether a coordinate is part of the topology.</summary>
        public bool Contains(GridPosition position) => Bounds.Contains(position);

        /// <summary>Determines whether a coordinate is blocked or outside the topology.</summary>
        public bool IsBlocked(GridPosition position) =>
            !Contains(position)
            || (cells.TryGetValue(position, out GridCell cell) && cell.IsBlocked);

        /// <summary>Gets the terrain surcharge for an in-bounds cell.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The coordinate is outside the topology.</exception>
        public TerrainCost GetTerrainCost(GridPosition position)
        {
            if (!Contains(position))
                throw new ArgumentOutOfRangeException(nameof(position));
            return cells.TryGetValue(position, out GridCell cell)
                ? cell.TerrainCost
                : TerrainCost.Normal;
        }
    }

    /// <summary>
    /// Records which alternating PF2e diagonal price applies to the next diagonal step.
    /// </summary>
    public enum DiagonalMovementPhase
    {
        /// <summary>The next diagonal costs its 5-foot base price.</summary>
        NextCostsFiveFeet,

        /// <summary>The next diagonal costs its 10-foot base price.</summary>
        NextCostsTenFeet,
    }

    /// <summary>
    /// Describes one validated ground-movement step's cost and resulting diagonal phase.
    /// </summary>
    public readonly struct MovementStepCost : IEquatable<MovementStepCost>
    {
        internal MovementStepCost(
            GridDistance distance,
            DiagonalMovementPhase nextDiagonalPhase,
            bool isDiagonal
        )
        {
            Distance = distance;
            NextDiagonalPhase = nextDiagonalPhase;
            IsDiagonal = isDiagonal;
        }

        /// <summary>Gets the total distance spent for this step.</summary>
        public GridDistance Distance { get; }

        /// <summary>Gets the phase that applies after this step commits.</summary>
        public DiagonalMovementPhase NextDiagonalPhase { get; }

        /// <summary>Gets whether the step changed both horizontal grid axes.</summary>
        public bool IsDiagonal { get; }

        /// <inheritdoc/>
        public bool Equals(MovementStepCost other) =>
            Distance == other.Distance
            && NextDiagonalPhase == other.NextDiagonalPhase
            && IsDiagonal == other.IsDiagonal;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MovementStepCost other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Distance, NextDiagonalPhase, IsDiagonal);
    }

    /// <summary>
    /// Calculates deterministic PF2e ground-movement costs without reading rules or Unity state.
    /// </summary>
    public static class MovementCostRules
    {
        /// <summary>
        /// Applies the difficult-terrain floor for occupied traversal without downgrading greater
        /// difficult terrain supplied by topology.
        /// </summary>
        internal static TerrainCost ApplyOccupiedSpaceFloor(TerrainCost terrain) =>
            terrain.Kind == TerrainCostKind.Normal ? TerrainCost.Difficult : terrain;

        /// <summary>Determines whether two cells form one horizontal ground-movement step.</summary>
        public static bool IsContiguous(GridPosition from, GridPosition to)
        {
            int dx = Math.Abs(to.X - from.X);
            int dz = Math.Abs(to.Z - from.Z);
            return from.Y == to.Y && dx <= 1 && dz <= 1 && dx + dz > 0;
        }

        /// <summary>
        /// Calculates the 5-foot-grid cost of entering one contiguous cell.
        /// </summary>
        /// <param name="from">The departure cell.</param>
        /// <param name="to">The destination cell.</param>
        /// <param name="terrain">The destination cell's terrain surcharge.</param>
        /// <param name="phase">The phase before this step.</param>
        /// <returns>The total cost and phase after the step.</returns>
        /// <exception cref="ArgumentException">The cells are not one contiguous ground step.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> is undefined.</exception>
        public static MovementStepCost Calculate(
            GridPosition from,
            GridPosition to,
            TerrainCost terrain,
            DiagonalMovementPhase phase
        )
        {
            if (!Enum.IsDefined(typeof(DiagonalMovementPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (!IsContiguous(from, to))
                throw new ArgumentException("Movement cost requires one contiguous ground step.");

            bool diagonal = from.X != to.X && from.Z != to.Z;
            int baseFeet = 5;
            DiagonalMovementPhase nextPhase = phase;
            if (diagonal)
            {
                baseFeet = phase == DiagonalMovementPhase.NextCostsFiveFeet ? 5 : 10;
                nextPhase =
                    phase == DiagonalMovementPhase.NextCostsFiveFeet
                        ? DiagonalMovementPhase.NextCostsTenFeet
                        : DiagonalMovementPhase.NextCostsFiveFeet;
            }

            return new MovementStepCost(
                new GridDistance(baseFeet + terrain.AdditionalFeet),
                nextPhase,
                diagonal
            );
        }
    }
}
