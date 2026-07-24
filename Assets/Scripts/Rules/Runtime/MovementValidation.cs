using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Performs pure path and destination validation against an immutable topology and snapshot.
    /// </summary>
    internal sealed class MovementPathValidator
    {
        private readonly IGridTopologyProvider topologyProvider;

        public MovementPathValidator(GridTopology topology)
            : this(new FixedGridTopologyProvider(topology)) { }

        public MovementPathValidator(IGridTopologyProvider topologyProvider)
        {
            this.topologyProvider =
                topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
        }

        private GridTopology Topology =>
            topologyProvider.Current
            ?? throw new InvalidOperationException("A topology provider returned no snapshot.");

        public MovementPathValidation Validate(
            RulesSnapshot snapshot,
            CreatureId mover,
            MovementBudgetId budgetId,
            MovementPath path,
            OccupiedTraversalAllowance allowance
        ) =>
            Validate(
                snapshot,
                mover,
                budgetId,
                path,
                allowance.HasOccupant
                    ? new[] { allowance }
                    : Array.Empty<OccupiedTraversalAllowance>()
            );

        public MovementPathValidation Validate(
            RulesSnapshot snapshot,
            CreatureId mover,
            MovementBudgetId budgetId,
            MovementPath path,
            IReadOnlyList<OccupiedTraversalAllowance> allowances
        )
        {
            if (!snapshot.MovementBudgets.TryGet(mover, out MovementBudgetState budget))
            {
                return MovementPathValidation.Rejected(
                    new MovementFailure(MovementFailureKind.MissingBudget, 0, path.Origin)
                );
            }
            if (budget.Id != budgetId || budget.Owner != mover)
            {
                return MovementPathValidation.Rejected(
                    new MovementFailure(MovementFailureKind.BudgetMismatch, 0, path.Origin)
                );
            }

            return ValidateCore(
                snapshot,
                mover,
                path,
                budget.Remaining,
                budget.DiagonalPhase,
                allowances
            );
        }

        public MovementPathValidation ValidateActionPath(
            RulesSnapshot snapshot,
            CreatureId mover,
            MovementPath path,
            GridDistance allowance,
            DiagonalMovementPhase diagonalPhase,
            IReadOnlyList<OccupiedTraversalAllowance> occupiedTraversals
        ) => ValidateCore(snapshot, mover, path, allowance, diagonalPhase, occupiedTraversals);

        private MovementPathValidation ValidateCore(
            RulesSnapshot snapshot,
            CreatureId mover,
            MovementPath path,
            GridDistance allowance,
            DiagonalMovementPhase diagonalPhase,
            IReadOnlyList<OccupiedTraversalAllowance> occupiedTraversals
        )
        {
            if (!snapshot.Positions.TryGet(mover, out GridPosition current))
            {
                return MovementPathValidation.Rejected(
                    new MovementFailure(MovementFailureKind.MissingPosition, 0, path.Origin)
                );
            }
            if (current != path.Origin)
            {
                return MovementPathValidation.Rejected(
                    new MovementFailure(MovementFailureKind.StaleOrigin, 0, current)
                );
            }
            if (path.Steps.Count == 0)
            {
                return MovementPathValidation.Rejected(
                    new MovementFailure(MovementFailureKind.EmptyPath, 0, current)
                );
            }

            List<MovementStepPlan> plans = new List<MovementStepPlan>(path.Steps.Count);
            GridPosition from = current;
            int remaining = allowance.Feet;
            DiagonalMovementPhase phase = diagonalPhase;
            int usedTraversalCount = 0;
            List<OccupiedTraversalAllowance> committedTraversals =
                new List<OccupiedTraversalAllowance>(occupiedTraversals.Count);
            for (int index = 0; index < path.Steps.Count; index++)
            {
                int stepNumber = index + 1;
                GridPosition to = path.Steps[index];
                bool isDestination = stepNumber == path.Steps.Count;
                MovementFailure topologyFailure = ValidateTopologyStep(
                    from,
                    to,
                    stepNumber,
                    isDestination
                );
                if (topologyFailure.Kind != MovementFailureKind.None)
                    return MovementPathValidation.Rejected(topologyFailure);

                OccupiedTraversalAllowance committedAllowance = OccupiedTraversalAllowance.None;
                if (TryFindBlockingOccupant(snapshot, mover, to, out CreatureId occupant))
                {
                    if (isDestination)
                    {
                        return MovementPathValidation.Rejected(
                            new MovementFailure(
                                MovementFailureKind.DestinationOccupied,
                                stepNumber,
                                to
                            )
                        );
                    }
                    if (occupiedTraversals.Count == 0)
                    {
                        return MovementPathValidation.Rejected(
                            new MovementFailure(MovementFailureKind.Occupied, stepNumber, to)
                        );
                    }
                    if (usedTraversalCount >= occupiedTraversals.Count)
                    {
                        return MovementPathValidation.Rejected(
                            PermissionFailure(
                                MovementPermissionFailureKind.InvalidReservation,
                                stepNumber,
                                to
                            )
                        );
                    }
                    OccupiedTraversalAllowance matchedTraversal = occupiedTraversals[
                        usedTraversalCount
                    ];
                    if (matchedTraversal.Occupant != occupant)
                    {
                        return MovementPathValidation.Rejected(
                            PermissionFailure(
                                MovementPermissionFailureKind.OccupantMismatch,
                                stepNumber,
                                to
                            )
                        );
                    }
                    if (
                        matchedTraversal.HasReservedPosition
                        && matchedTraversal.ReservedPosition != to
                    )
                    {
                        return MovementPathValidation.Rejected(
                            PermissionFailure(
                                MovementPermissionFailureKind.InvalidReservation,
                                stepNumber,
                                to
                            )
                        );
                    }
                    if (HasOtherOccupant(snapshot, mover, to, matchedTraversal.Occupant))
                    {
                        return MovementPathValidation.Rejected(
                            PermissionFailure(
                                MovementPermissionFailureKind.OccupantMismatch,
                                stepNumber,
                                to
                            )
                        );
                    }

                    committedAllowance = matchedTraversal.HasReservedPosition
                        ? matchedTraversal
                        : OccupiedTraversalAllowance.ForReservedPosition(occupant, to);
                    usedTraversalCount++;
                    committedTraversals.Add(committedAllowance);
                }

                TerrainCost terrain = Topology.GetTerrainCost(to);
                if (committedAllowance.HasOccupant)
                    terrain = MovementCostRules.ApplyOccupiedSpaceFloor(terrain);
                MovementStepCost cost = MovementCostRules.Calculate(from, to, terrain, phase);
                if (cost.Distance.Feet > remaining)
                {
                    return MovementPathValidation.Rejected(
                        new MovementFailure(
                            MovementFailureKind.InsufficientMovement,
                            stepNumber,
                            to
                        )
                    );
                }

                remaining -= cost.Distance.Feet;
                phase = cost.NextDiagonalPhase;
                plans.Add(new MovementStepPlan(from, to, cost, committedAllowance, isDestination));
                from = to;
            }

            if (usedTraversalCount != occupiedTraversals.Count)
            {
                return MovementPathValidation.Rejected(
                    PermissionFailure(
                        MovementPermissionFailureKind.InvalidReservation,
                        0,
                        path.Destination
                    )
                );
            }

            return MovementPathValidation.Accepted(plans, committedTraversals);
        }

        public MovementFailure ValidateRelocation(
            RulesSnapshot snapshot,
            CreatureId mover,
            GridPosition expectedOrigin,
            GridPosition destination
        )
        {
            if (!snapshot.Positions.TryGet(mover, out GridPosition current))
                return new MovementFailure(MovementFailureKind.MissingPosition, 0, expectedOrigin);
            if (current != expectedOrigin)
                return new MovementFailure(MovementFailureKind.StaleOrigin, 0, current);
            if (!Topology.Contains(destination))
            {
                return new MovementFailure(
                    MovementFailureKind.DestinationOutOfBounds,
                    0,
                    destination
                );
            }
            if (Topology.IsBlocked(destination))
            {
                return new MovementFailure(MovementFailureKind.DestinationBlocked, 0, destination);
            }
            if (TryFindBlockingOccupant(snapshot, mover, destination, out CreatureId _))
            {
                return new MovementFailure(MovementFailureKind.DestinationOccupied, 0, destination);
            }
            return default;
        }

        private MovementFailure ValidateTopologyStep(
            GridPosition from,
            GridPosition to,
            int stepNumber,
            bool isDestination
        )
        {
            if (!Topology.Contains(to))
            {
                return new MovementFailure(
                    isDestination
                        ? MovementFailureKind.DestinationOutOfBounds
                        : MovementFailureKind.OutOfBounds,
                    stepNumber,
                    to
                );
            }
            if (!MovementCostRules.IsContiguous(from, to))
            {
                return new MovementFailure(MovementFailureKind.NonContiguous, stepNumber, to);
            }
            if (BlocksDiagonalCorner(from, to))
            {
                return new MovementFailure(MovementFailureKind.CornerBlocked, stepNumber, to);
            }
            if (Topology.IsBlocked(to))
            {
                return new MovementFailure(
                    isDestination
                        ? MovementFailureKind.DestinationBlocked
                        : MovementFailureKind.Blocked,
                    stepNumber,
                    to
                );
            }
            return default;
        }

        private bool BlocksDiagonalCorner(GridPosition from, GridPosition to)
        {
            int dx = to.X - from.X;
            int dz = to.Z - from.Z;
            if (Math.Abs(dx) != 1 || Math.Abs(dz) != 1 || from.Y != to.Y)
                return false;

            GridPosition sideX = new GridPosition(from.X + dx, from.Y, from.Z);
            GridPosition sideZ = new GridPosition(from.X, from.Y, from.Z + dz);
            return Topology.IsBlocked(sideX) || Topology.IsBlocked(sideZ);
        }

        internal static bool TryFindBlockingOccupant(
            RulesSnapshot snapshot,
            CreatureId mover,
            GridPosition position,
            out CreatureId occupant
        )
        {
            foreach (KeyValuePair<CreatureId, GridPosition> pair in snapshot.Positions)
            {
                if (pair.Key != mover && pair.Value == position)
                {
                    occupant = pair.Key;
                    return true;
                }
            }

            occupant = default;
            return false;
        }

        private static bool HasOtherOccupant(
            RulesSnapshot snapshot,
            CreatureId mover,
            GridPosition position,
            CreatureId permittedOccupant
        )
        {
            foreach (KeyValuePair<CreatureId, GridPosition> pair in snapshot.Positions)
            {
                if (pair.Key != mover && pair.Key != permittedOccupant && pair.Value == position)
                    return true;
            }
            return false;
        }

        internal static MovementFailure PermissionFailure(
            MovementPermissionFailureKind permissionFailure,
            int stepNumber,
            GridPosition position
        ) =>
            new MovementFailure(
                MovementFailureKind.PermissionRejected,
                stepNumber,
                position,
                permissionFailure
            );
    }

    internal sealed class MovementPathValidation
    {
        private static readonly IReadOnlyList<MovementStepPlan> NoSteps = Array.AsReadOnly(
            Array.Empty<MovementStepPlan>()
        );
        private static readonly IReadOnlyList<OccupiedTraversalAllowance> NoTraversals =
            Array.AsReadOnly(Array.Empty<OccupiedTraversalAllowance>());

        private MovementPathValidation(
            bool isValid,
            IReadOnlyList<MovementStepPlan> steps,
            MovementFailure failure,
            IReadOnlyList<OccupiedTraversalAllowance> occupiedTraversals
        )
        {
            IsValid = isValid;
            Steps = steps;
            Failure = failure;
            OccupiedTraversals = occupiedTraversals;
        }

        public bool IsValid { get; }
        public IReadOnlyList<MovementStepPlan> Steps { get; }
        public MovementFailure Failure { get; }
        public IReadOnlyList<OccupiedTraversalAllowance> OccupiedTraversals { get; }

        public static MovementPathValidation Accepted(
            List<MovementStepPlan> steps,
            List<OccupiedTraversalAllowance> occupiedTraversals
        ) =>
            new MovementPathValidation(
                true,
                Array.AsReadOnly(steps.ToArray()),
                default,
                Array.AsReadOnly(occupiedTraversals.ToArray())
            );

        public static MovementPathValidation Rejected(MovementFailure failure) =>
            new MovementPathValidation(false, NoSteps, failure, NoTraversals);
    }

    internal readonly struct MovementStepPlan
    {
        public MovementStepPlan(
            GridPosition from,
            GridPosition to,
            MovementStepCost cost,
            OccupiedTraversalAllowance allowance,
            bool isDestination
        )
        {
            From = from;
            To = to;
            Cost = cost;
            Allowance = allowance;
            IsDestination = isDestination;
        }

        public GridPosition From { get; }
        public GridPosition To { get; }
        public MovementStepCost Cost { get; }
        public OccupiedTraversalAllowance Allowance { get; }
        public bool IsDestination { get; }
    }
}
