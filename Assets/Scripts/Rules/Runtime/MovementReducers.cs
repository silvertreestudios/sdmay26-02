using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    internal sealed class CommitMovementBudgetStartReducer
        : IOpReducer<CommitMovementBudgetStartOp, MovementBudgetStartOutcome>
    {
        public ReductionResult<MovementBudgetStartOutcome> Reduce(
            ReductionContext<CommitMovementBudgetStartOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                state.MovementBudgets.TryGet(context.Op.Mover, out MovementBudgetState current)
                && current.Id == context.Op.BudgetId
            )
            {
                return ReductionResult<MovementBudgetStartOutcome>.Accept(
                    new MovementBudgetStartOutcome(
                        new MovementFailure(
                            MovementFailureKind.BudgetMismatch,
                            0,
                            PositionOrDefault(state, context.Op.Mover)
                        )
                    )
                );
            }

            DiagonalMovementPhase phase = state.MovementBudgets.TryGet(
                context.Op.Mover,
                out current
            )
                ? current.DiagonalPhase
                : DiagonalMovementPhase.NextCostsFiveFeet;
            MovementBudgetState budget = new MovementBudgetState(
                context.Op.BudgetId,
                context.Op.Mover,
                context.Op.Allowance,
                phase
            );
            state.MovementBudgets.Set(context.Op.Mover, budget);
            facts.Stage(new MovementBudgetStartedFact(budget));
            return ReductionResult<MovementBudgetStartOutcome>.Accept(
                new MovementBudgetStartOutcome(budget)
            );
        }

        private static GridPosition PositionOrDefault(RulesStateDraft state, CreatureId creature) =>
            state.Positions.TryGet(creature, out GridPosition position) ? position : default;
    }

    internal sealed class CommitMovementBudgetResetReducer
        : IOpReducer<CommitMovementBudgetResetOp, MovementBudgetResetOutcome>
    {
        public ReductionResult<MovementBudgetResetOutcome> Reduce(
            ReductionContext<CommitMovementBudgetResetOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!state.MovementBudgets.TryGet(context.Op.Mover, out MovementBudgetState previous))
            {
                return ReductionResult<MovementBudgetResetOutcome>.Accept(
                    new MovementBudgetResetOutcome(false)
                );
            }

            state.MovementBudgets.Remove(context.Op.Mover);
            facts.Stage(new MovementBudgetResetFact(previous));
            return ReductionResult<MovementBudgetResetOutcome>.Accept(
                new MovementBudgetResetOutcome(true)
            );
        }
    }

    internal sealed class CommitMovementStepReducer
        : IOpReducer<CommitMovementStepOp, MovementStepCommitOutcome>
    {
        private readonly IGridTopologyProvider topologyProvider;

        public CommitMovementStepReducer(GridTopology topology)
            : this(new FixedGridTopologyProvider(topology)) { }

        public CommitMovementStepReducer(IGridTopologyProvider topologyProvider)
        {
            this.topologyProvider =
                topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
        }

        private GridTopology Topology =>
            topologyProvider.Current
            ?? throw new InvalidOperationException("A topology provider returned no snapshot.");

        public ReductionResult<MovementStepCommitOutcome> Reduce(
            ReductionContext<CommitMovementStepOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            CommitMovementStepOp op = context.Op;
            if (!state.Positions.TryGet(op.Mover, out GridPosition current))
                return Rejected(Failure(MovementFailureKind.MissingPosition, op));
            if (!state.MovementBudgets.TryGet(op.Mover, out MovementBudgetState budget))
                return Rejected(Failure(MovementFailureKind.MissingBudget, op));

            MovementFailure failure = PrepareStep(
                op,
                state.Positions,
                current,
                budget,
                out PreparedMovementStep prepared
            );
            if (failure.Kind != MovementFailureKind.None)
                return Rejected(failure);

            CommitPreparedStep(op, prepared, state, facts);

            return ReductionResult<MovementStepCommitOutcome>.Accept(
                new MovementStepCommitOutcome(prepared.Cost, prepared.UpdatedBudget.Remaining)
            );
        }

        /// <summary>
        /// Purely prepares a complete occupied run against one committed snapshot.
        /// </summary>
        /// <remarks>
        /// This uses the same contract as final reducer settlement so handlers can decide whether
        /// later departure timing may open without staging state or Facts.
        /// </remarks>
        internal MovementFailure ValidateCrossing(
            CommitOccupiedMovementCrossingOp op,
            RulesSnapshot snapshot
        )
        {
            if (!snapshot.Positions.TryGet(op.Entry.Mover, out GridPosition current))
                return Failure(MovementFailureKind.MissingPosition, op.Entry);
            if (!snapshot.MovementBudgets.TryGet(op.Entry.Mover, out MovementBudgetState budget))
            {
                return Failure(MovementFailureKind.MissingBudget, op.Entry);
            }

            return PrepareCrossing(
                op,
                snapshot.Positions,
                current,
                budget,
                out List<PreparedMovementStep> _
            );
        }

        internal MovementFailure PrepareCrossing(
            CommitOccupiedMovementCrossingOp op,
            RulesStateDraft state,
            out List<PreparedMovementStep> preparedSteps
        )
        {
            preparedSteps = new List<PreparedMovementStep>();
            if (!state.Positions.TryGet(op.Entry.Mover, out GridPosition current))
                return Failure(MovementFailureKind.MissingPosition, op.Entry);
            if (!state.MovementBudgets.TryGet(op.Entry.Mover, out MovementBudgetState budget))
            {
                return Failure(MovementFailureKind.MissingBudget, op.Entry);
            }

            return PrepareCrossing(op, state.Positions, current, budget, out preparedSteps);
        }

        private MovementFailure PrepareCrossing(
            CommitOccupiedMovementCrossingOp op,
            IReadOnlyCollection<KeyValuePair<CreatureId, GridPosition>> positions,
            GridPosition current,
            MovementBudgetState budget,
            out List<PreparedMovementStep> preparedSteps
        )
        {
            preparedSteps = new List<PreparedMovementStep>(op.Steps.Count);
            foreach (CommitMovementStepOp step in op.Steps)
            {
                MovementFailure failure = PrepareStep(
                    step,
                    positions,
                    current,
                    budget,
                    out PreparedMovementStep prepared
                );
                if (failure.Kind != MovementFailureKind.None)
                    return failure;

                preparedSteps.Add(prepared);
                current = step.To;
                budget = prepared.UpdatedBudget;
            }

            bool hasOccupiedTraversal = false;
            for (int index = 0; index + 1 < preparedSteps.Count; index++)
            {
                hasOccupiedTraversal |= preparedSteps[index].ReservedOccupantPresent;
            }
            PreparedMovementStep preparedExit = preparedSteps[preparedSteps.Count - 1];
            if (!hasOccupiedTraversal || preparedExit.ReservedOccupantPresent)
            {
                CommitMovementStepOp step = op.Exit;
                return MovementPathValidator.PermissionFailure(
                    MovementPermissionFailureKind.InvalidReservation,
                    step.TriggerId.StepNumber,
                    step.To
                );
            }
            return default;
        }

        internal MovementFailure PrepareStep(
            CommitMovementStepOp op,
            IReadOnlyCollection<KeyValuePair<CreatureId, GridPosition>> positions,
            GridPosition current,
            MovementBudgetState budget,
            out PreparedMovementStep prepared
        )
        {
            prepared = default;
            bool reservedOccupantPresent = ReservedOccupantOccupies(op, positions);
            if (current != op.From)
                return new MovementFailure(
                    MovementFailureKind.StaleOrigin,
                    op.TriggerId.StepNumber,
                    current
                );
            if (budget.Id != op.BudgetId || budget.Owner != op.Mover)
                return Failure(MovementFailureKind.BudgetMismatch, op);
            if (!Topology.Contains(op.To))
            {
                return Failure(
                    op.IsDestination
                        ? MovementFailureKind.DestinationOutOfBounds
                        : MovementFailureKind.OutOfBounds,
                    op
                );
            }
            if (!MovementCostRules.IsContiguous(op.From, op.To))
                return Failure(MovementFailureKind.NonContiguous, op);
            if (BlocksDiagonalCorner(op.From, op.To))
                return Failure(MovementFailureKind.CornerBlocked, op);
            if (Topology.IsBlocked(op.To))
            {
                return Failure(
                    op.IsDestination
                        ? MovementFailureKind.DestinationBlocked
                        : MovementFailureKind.Blocked,
                    op
                );
            }

            if (TryFindBlockingOccupant(positions, op.Mover, op.To, out CreatureId occupant))
            {
                if (op.IsDestination)
                    return Failure(MovementFailureKind.DestinationOccupied, op);
                if (!op.Allowance.HasOccupant)
                    return Failure(MovementFailureKind.Occupied, op);
                if (!op.Allowance.HasReservedPosition || op.Allowance.ReservedPosition != op.To)
                {
                    return MovementPathValidator.PermissionFailure(
                        MovementPermissionFailureKind.InvalidReservation,
                        op.TriggerId.StepNumber,
                        op.To
                    );
                }
                if (
                    occupant != op.Allowance.Occupant
                    || HasOtherOccupant(positions, op.Mover, op.To, op.Allowance.Occupant)
                )
                {
                    return MovementPathValidator.PermissionFailure(
                        MovementPermissionFailureKind.OccupantMismatch,
                        op.TriggerId.StepNumber,
                        op.To
                    );
                }
            }

            MovementStepCost currentCost = CalculateCost(op, budget, reservedOccupantPresent);
            if (!currentCost.Equals(op.ExpectedCost))
            {
                bool departureExplainsDifference =
                    op.Allowance.HasOccupant
                    && !reservedOccupantPresent
                    && CalculateCost(op, budget, true).Equals(op.ExpectedCost);
                if (!departureExplainsDifference)
                    return Failure(MovementFailureKind.BudgetMismatch, op);
            }
            if (currentCost.Distance.Feet > budget.Remaining.Feet)
                return Failure(MovementFailureKind.InsufficientMovement, op);

            GridDistance remaining = new GridDistance(
                budget.Remaining.Feet - currentCost.Distance.Feet
            );
            prepared = new PreparedMovementStep(
                currentCost,
                new MovementBudgetState(
                    budget.Id,
                    budget.Owner,
                    remaining,
                    currentCost.NextDiagonalPhase
                ),
                reservedOccupantPresent
            );
            return default;
        }

        internal static void CommitPreparedStep(
            CommitMovementStepOp op,
            PreparedMovementStep prepared,
            RulesStateDraft state,
            FactSink facts
        )
        {
            state.Positions.Set(op.Mover, op.To);
            state.MovementBudgets.Set(op.Mover, prepared.UpdatedBudget);
            facts.Stage(
                new TokenMovedFact(
                    op.Mover,
                    op.From,
                    op.To,
                    prepared.Cost.Distance,
                    prepared.UpdatedBudget.Remaining,
                    prepared.UpdatedBudget.DiagonalPhase,
                    op.BudgetId,
                    op.ActionOpId,
                    op.TriggerId,
                    op.TriggerKind
                )
            );

            if (prepared.ReservedOccupantPresent)
            {
                facts.Stage(
                    new OccupiedSpaceTraversedFact(
                        op.Mover,
                        op.Allowance.Occupant,
                        op.To,
                        op.BudgetId,
                        op.ActionOpId,
                        op.TriggerId,
                        op.PermissionPurpose
                    )
                );
            }
        }

        private static ReductionResult<MovementStepCommitOutcome> Rejected(
            MovementFailure failure
        ) =>
            ReductionResult<MovementStepCommitOutcome>.Accept(
                new MovementStepCommitOutcome(failure)
            );

        private MovementStepCost CalculateCost(
            CommitMovementStepOp op,
            MovementBudgetState budget,
            bool reservedOccupantPresent
        )
        {
            TerrainCost terrain = Topology.GetTerrainCost(op.To);
            if (reservedOccupantPresent)
                terrain = MovementCostRules.ApplyOccupiedSpaceFloor(terrain);
            return MovementCostRules.Calculate(op.From, op.To, terrain, budget.DiagonalPhase);
        }

        private static bool ReservedOccupantOccupies(
            CommitMovementStepOp op,
            IReadOnlyCollection<KeyValuePair<CreatureId, GridPosition>> positions
        )
        {
            if (!op.Allowance.HasOccupant)
                return false;

            foreach (KeyValuePair<CreatureId, GridPosition> pair in positions)
            {
                if (pair.Key == op.Allowance.Occupant)
                    return pair.Value == op.To;
            }
            return false;
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

        private static MovementFailure Failure(MovementFailureKind kind, CommitMovementStepOp op) =>
            new MovementFailure(kind, op.TriggerId.StepNumber, op.To);

        private static bool TryFindBlockingOccupant(
            IReadOnlyCollection<KeyValuePair<CreatureId, GridPosition>> positions,
            CreatureId mover,
            GridPosition position,
            out CreatureId occupant
        )
        {
            foreach (KeyValuePair<CreatureId, GridPosition> pair in positions)
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
            IReadOnlyCollection<KeyValuePair<CreatureId, GridPosition>> positions,
            CreatureId mover,
            GridPosition position,
            CreatureId permittedOccupant
        )
        {
            foreach (KeyValuePair<CreatureId, GridPosition> pair in positions)
            {
                if (pair.Key != mover && pair.Key != permittedOccupant && pair.Value == position)
                    return true;
            }
            return false;
        }

        internal readonly struct PreparedMovementStep
        {
            public PreparedMovementStep(
                MovementStepCost cost,
                MovementBudgetState updatedBudget,
                bool reservedOccupantPresent
            )
            {
                Cost = cost;
                UpdatedBudget = updatedBudget;
                ReservedOccupantPresent = reservedOccupantPresent;
            }

            public MovementStepCost Cost { get; }
            public MovementBudgetState UpdatedBudget { get; }
            public bool ReservedOccupantPresent { get; }
        }
    }

    internal sealed class CommitOccupiedMovementCrossingReducer
        : IOpReducer<CommitOccupiedMovementCrossingOp, MovementCrossingCommitOutcome>
    {
        private readonly CommitMovementStepReducer stepReducer;

        public CommitOccupiedMovementCrossingReducer(CommitMovementStepReducer stepReducer)
        {
            this.stepReducer = stepReducer ?? throw new ArgumentNullException(nameof(stepReducer));
        }

        public ReductionResult<MovementCrossingCommitOutcome> Reduce(
            ReductionContext<CommitOccupiedMovementCrossingOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            MovementFailure failure = stepReducer.PrepareCrossing(
                context.Op,
                state,
                out List<CommitMovementStepReducer.PreparedMovementStep> preparedSteps
            );
            if (failure.Kind != MovementFailureKind.None)
                return Rejected(failure);

            // No draft mutation or Fact staging occurs until the full crossing validates.
            // Observers receive its ordered movement Facts against the final legal exit snapshot.
            for (int index = 0; index < context.Op.Steps.Count; index++)
            {
                CommitMovementStepReducer.CommitPreparedStep(
                    context.Op.Steps[index],
                    preparedSteps[index],
                    state,
                    facts
                );
            }
            return ReductionResult<MovementCrossingCommitOutcome>.Accept(
                new MovementCrossingCommitOutcome(
                    preparedSteps.ConvertAll(prepared => prepared.Cost),
                    preparedSteps[preparedSteps.Count - 1].UpdatedBudget.Remaining
                )
            );
        }

        private static ReductionResult<MovementCrossingCommitOutcome> Rejected(
            MovementFailure failure
        ) =>
            ReductionResult<MovementCrossingCommitOutcome>.Accept(
                new MovementCrossingCommitOutcome(failure)
            );
    }

    internal sealed class CommitRelocationReducer
        : IOpReducer<CommitRelocationOp, RelocationOutcome>
    {
        private readonly IGridTopologyProvider topologyProvider;

        public CommitRelocationReducer(GridTopology topology)
            : this(new FixedGridTopologyProvider(topology)) { }

        public CommitRelocationReducer(IGridTopologyProvider topologyProvider)
        {
            this.topologyProvider =
                topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
        }

        private GridTopology Topology =>
            topologyProvider.Current
            ?? throw new InvalidOperationException("A topology provider returned no snapshot.");

        public ReductionResult<RelocationOutcome> Reduce(
            ReductionContext<CommitRelocationOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            CommitRelocationOp op = context.Op;
            MovementFailure failure = Validate(op, state);
            if (failure.Kind != MovementFailureKind.None)
            {
                GridPosition finalPosition = state.Positions.TryGet(
                    op.Mover,
                    out GridPosition current
                )
                    ? current
                    : op.ExpectedOrigin;
                return ReductionResult<RelocationOutcome>.Accept(
                    new RelocationOutcome(false, finalPosition, failure)
                );
            }

            state.Positions.Set(op.Mover, op.Destination);
            facts.Stage(
                new TokenRelocatedFact(
                    op.Mover,
                    op.ExpectedOrigin,
                    op.Destination,
                    op.OriginOpId,
                    op.Kind
                )
            );
            return ReductionResult<RelocationOutcome>.Accept(
                new RelocationOutcome(true, op.Destination, default)
            );
        }

        private MovementFailure Validate(CommitRelocationOp op, RulesStateDraft state)
        {
            if (!state.Positions.TryGet(op.Mover, out GridPosition current))
            {
                return new MovementFailure(
                    MovementFailureKind.MissingPosition,
                    0,
                    op.ExpectedOrigin
                );
            }
            if (current != op.ExpectedOrigin)
                return new MovementFailure(MovementFailureKind.StaleOrigin, 0, current);
            if (op.Destination == op.ExpectedOrigin)
            {
                return new MovementFailure(
                    MovementFailureKind.DestinationUnchanged,
                    0,
                    op.Destination
                );
            }
            if (!Topology.Contains(op.Destination))
            {
                return new MovementFailure(
                    MovementFailureKind.DestinationOutOfBounds,
                    0,
                    op.Destination
                );
            }
            if (Topology.IsBlocked(op.Destination))
            {
                return new MovementFailure(
                    MovementFailureKind.DestinationBlocked,
                    0,
                    op.Destination
                );
            }
            foreach (KeyValuePair<CreatureId, GridPosition> pair in state.Positions)
            {
                if (pair.Key != op.Mover && pair.Value == op.Destination)
                {
                    return new MovementFailure(
                        MovementFailureKind.DestinationOccupied,
                        0,
                        op.Destination
                    );
                }
            }
            return default;
        }
    }
}
