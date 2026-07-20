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
        private readonly GridTopology topology;

        public CommitMovementStepReducer(GridTopology topology)
        {
            this.topology = topology ?? throw new ArgumentNullException(nameof(topology));
        }

        public ReductionResult<MovementStepCommitOutcome> Reduce(
            ReductionContext<CommitMovementStepOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            CommitMovementStepOp op = context.Op;
            bool reservedOccupantPresent = ReservedOccupantOccupies(op, state);
            MovementFailure failure = ValidateCurrentStep(op, state, reservedOccupantPresent);
            if (failure.Kind != MovementFailureKind.None)
            {
                return ReductionResult<MovementStepCommitOutcome>.Accept(
                    new MovementStepCommitOutcome(failure)
                );
            }

            MovementBudgetState budget = default;
            state.MovementBudgets.TryGet(op.Mover, out budget);
            MovementStepCost cost = CalculateCost(op, budget, reservedOccupantPresent);
            GridDistance remaining = new GridDistance(budget.Remaining.Feet - cost.Distance.Feet);
            MovementBudgetState updatedBudget = new MovementBudgetState(
                budget.Id,
                budget.Owner,
                remaining,
                cost.NextDiagonalPhase
            );

            state.Positions.Set(op.Mover, op.To);
            state.MovementBudgets.Set(op.Mover, updatedBudget);
            facts.Stage(
                new TokenMovedFact(
                    op.Mover,
                    op.From,
                    op.To,
                    cost.Distance,
                    remaining,
                    updatedBudget.DiagonalPhase,
                    op.BudgetId,
                    op.ActionOpId,
                    op.TriggerId,
                    op.TriggerKind
                )
            );

            if (reservedOccupantPresent)
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

            return ReductionResult<MovementStepCommitOutcome>.Accept(
                new MovementStepCommitOutcome(cost, remaining)
            );
        }

        private MovementFailure ValidateCurrentStep(
            CommitMovementStepOp op,
            RulesStateDraft state,
            bool reservedOccupantPresent
        )
        {
            if (!state.Positions.TryGet(op.Mover, out GridPosition current))
                return Failure(MovementFailureKind.MissingPosition, op);
            if (current != op.From)
                return new MovementFailure(
                    MovementFailureKind.StaleOrigin,
                    op.TriggerId.StepNumber,
                    current
                );
            if (!state.MovementBudgets.TryGet(op.Mover, out MovementBudgetState budget))
                return Failure(MovementFailureKind.MissingBudget, op);
            if (budget.Id != op.BudgetId || budget.Owner != op.Mover)
                return Failure(MovementFailureKind.BudgetMismatch, op);
            if (!topology.Contains(op.To))
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
            if (topology.IsBlocked(op.To))
            {
                return Failure(
                    op.IsDestination
                        ? MovementFailureKind.DestinationBlocked
                        : MovementFailureKind.Blocked,
                    op
                );
            }

            if (TryFindBlockingOccupant(state.Positions, op.Mover, op.To, out CreatureId occupant))
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
                    || HasOtherOccupant(state.Positions, op.Mover, op.To, op.Allowance.Occupant)
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
            return default;
        }

        private MovementStepCost CalculateCost(
            CommitMovementStepOp op,
            MovementBudgetState budget,
            bool reservedOccupantPresent
        )
        {
            TerrainCost terrain = topology.GetTerrainCost(op.To);
            if (reservedOccupantPresent)
                terrain = MovementCostRules.ApplyOccupiedSpaceFloor(terrain);
            return MovementCostRules.Calculate(op.From, op.To, terrain, budget.DiagonalPhase);
        }

        private static bool ReservedOccupantOccupies(
            CommitMovementStepOp op,
            RulesStateDraft state
        ) =>
            op.Allowance.HasOccupant
            && state.Positions.TryGet(op.Allowance.Occupant, out GridPosition occupantPosition)
            && occupantPosition == op.To;

        private bool BlocksDiagonalCorner(GridPosition from, GridPosition to)
        {
            int dx = to.X - from.X;
            int dz = to.Z - from.Z;
            if (Math.Abs(dx) != 1 || Math.Abs(dz) != 1 || from.Y != to.Y)
                return false;
            GridPosition sideX = new GridPosition(from.X + dx, from.Y, from.Z);
            GridPosition sideZ = new GridPosition(from.X, from.Y, from.Z + dz);
            return topology.IsBlocked(sideX) || topology.IsBlocked(sideZ);
        }

        private static MovementFailure Failure(MovementFailureKind kind, CommitMovementStepOp op) =>
            new MovementFailure(kind, op.TriggerId.StepNumber, op.To);

        private static bool TryFindBlockingOccupant(
            StateSliceDraft<CreatureId, GridPosition> positions,
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
            StateSliceDraft<CreatureId, GridPosition> positions,
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
    }

    internal sealed class CommitRelocationReducer
        : IOpReducer<CommitRelocationOp, RelocationOutcome>
    {
        private readonly GridTopology topology;

        public CommitRelocationReducer(GridTopology topology)
        {
            this.topology = topology ?? throw new ArgumentNullException(nameof(topology));
        }

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
            if (!topology.Contains(op.Destination))
            {
                return new MovementFailure(
                    MovementFailureKind.DestinationOutOfBounds,
                    0,
                    op.Destination
                );
            }
            if (topology.IsBlocked(op.Destination))
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
