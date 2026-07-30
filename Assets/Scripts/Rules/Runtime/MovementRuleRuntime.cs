using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Registers the complete Unity-free movement command and reducer slice.</summary>
    public static class MovementRuleDispatcherExtensions
    {
        private const string BudgetResetComposition = "movement-budget-reset";
        private static readonly RuleSource MovementSource = RuleSource.FromSlug("movement");

        /// <summary>Registers the topology-free movement-budget reset operation and reducer.</summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseMovementBudgetResetRules(
            this RuleDispatcherBuilder builder
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (!builder.TryUseEngineComposition(BudgetResetComposition))
                return builder;
            return builder
                .RegisterHandler<ResetMovementBudgetOp, MovementBudgetResetOutcome>(
                    new ResetMovementBudgetHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitMovementBudgetResetOp, MovementBudgetResetOutcome>(
                    new CommitMovementBudgetResetReducer(),
                    MovementSource
                );
        }

        /// <summary>
        /// Adds nested movement budgets, path timing, permission issuance, atomic steps, and relocation.
        /// </summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="topology">The immutable encounter topology used for every validation pass.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseMovementRules(
            this RuleDispatcherBuilder builder,
            GridTopology topology
        ) => builder.UseMovementRules(new FixedGridTopologyProvider(topology));

        /// <summary>
        /// Adds movement rules whose immutable topology snapshot may be replaced between roots.
        /// </summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="topologyProvider">
        /// The provider that holds one stable topology throughout each active root.
        /// </param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseMovementRules(
            this RuleDispatcherBuilder builder,
            IGridTopologyProvider topologyProvider
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (topologyProvider == null)
                throw new ArgumentNullException(nameof(topologyProvider));

            builder.UseMovementBudgetResetRules();
            MovementPathValidator validator = new MovementPathValidator(topologyProvider);
            MovementPermission.Authority authority = new MovementPermission.Authority();
            CommitMovementStepReducer stepReducer = new CommitMovementStepReducer(topologyProvider);
            return builder
                .RegisterHandler<BeginMovementBudgetOp, MovementBudgetStartOutcome>(
                    new BeginMovementBudgetHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitMovementBudgetStartOp, MovementBudgetStartOutcome>(
                    new CommitMovementBudgetStartReducer(),
                    MovementSource
                )
                .RegisterHandler<MovementLeavingSquareOp, MovementTriggerOutcome>(
                    new MovementLeavingSquareHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<RequestMovementPermissionOp, MovementPermissionRequestOutcome>(
                    new RequestMovementPermissionHandler(validator, authority),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<MovePathOp, MovePathOutcome>(
                    new MovePathHandler(validator, authority, stepReducer),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitMovementStepOp, MovementStepCommitOutcome>(
                    stepReducer,
                    MovementSource
                )
                .RegisterEngineReducer<
                    CommitOccupiedMovementCrossingOp,
                    MovementCrossingCommitOutcome
                >(new CommitOccupiedMovementCrossingReducer(stepReducer), MovementSource)
                .RegisterHandler<RelocateTokenOp, RelocationOutcome>(
                    new RelocateTokenHandler(validator),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitRelocationOp, RelocationOutcome>(
                    new CommitRelocationReducer(topologyProvider),
                    MovementSource
                );
        }
    }

    internal sealed class BeginMovementBudgetHandler
        : IOpHandler<BeginMovementBudgetOp, MovementBudgetStartOutcome>
    {
        public async ValueTask<MovementBudgetStartOutcome> Handle(
            OpFrame<BeginMovementBudgetOp> frame,
            OpHandlerContext context
        )
        {
            MovementFailure provenance = MovementHandlerValidation.ValidateActionProvenance(
                frame.Id,
                frame.Op.ActionOpId,
                frame.Op.Mover,
                context,
                default
            );
            if (provenance.Kind != MovementFailureKind.None)
                return new MovementBudgetStartOutcome(provenance);

            OpResult<MovementBudgetStartOutcome> commit = await context.Dispatch(
                new CommitMovementBudgetStartOp(
                    new MovementBudgetId(frame.Op.ActionOpId),
                    frame.Op.Mover,
                    frame.Op.Allowance
                )
            );
            return MovementHandlerValidation.RequireResolved(commit, "movement budget start");
        }
    }

    internal sealed class ResetMovementBudgetHandler
        : IOpHandler<ResetMovementBudgetOp, MovementBudgetResetOutcome>
    {
        public async ValueTask<MovementBudgetResetOutcome> Handle(
            OpFrame<ResetMovementBudgetOp> frame,
            OpHandlerContext context
        )
        {
            OpResult<MovementBudgetResetOutcome> commit = await context.Dispatch(
                new CommitMovementBudgetResetOp(frame.Op.Mover)
            );
            return MovementHandlerValidation.RequireResolved(commit, "movement budget reset");
        }
    }

    internal sealed class MovementLeavingSquareHandler
        : IOpHandler<MovementLeavingSquareOp, MovementTriggerOutcome>
    {
        public ValueTask<MovementTriggerOutcome> Handle(
            OpFrame<MovementLeavingSquareOp> frame,
            OpHandlerContext context
        ) => new ValueTask<MovementTriggerOutcome>(MovementTriggerOutcome.Continue);
    }

    internal sealed class RequestMovementPermissionHandler
        : IOpHandler<RequestMovementPermissionOp, MovementPermissionRequestOutcome>
    {
        private readonly MovementPathValidator validator;
        private readonly MovementPermission.Authority authority;

        public RequestMovementPermissionHandler(
            MovementPathValidator validator,
            MovementPermission.Authority authority
        )
        {
            this.validator = validator;
            this.authority = authority;
        }

        public ValueTask<MovementPermissionRequestOutcome> Handle(
            OpFrame<RequestMovementPermissionOp> frame,
            OpHandlerContext context
        )
        {
            RequestMovementPermissionOp op = frame.Op;
            MovementFailure provenance = MovementHandlerValidation.ValidateActionProvenance(
                frame.Id,
                op.ActionOpId,
                op.Mover,
                context,
                op.Path.Origin
            );
            if (provenance.Kind != MovementFailureKind.None)
                return Completed(new MovementPermissionRequestOutcome(provenance));
            if (op.BudgetId.ActionOpId != op.ActionOpId)
            {
                return Completed(
                    new MovementPermissionRequestOutcome(
                        new MovementFailure(MovementFailureKind.BudgetMismatch, 0, op.Path.Origin)
                    )
                );
            }
            if (!frame.ParentId.HasValue)
            {
                return Completed(
                    new MovementPermissionRequestOutcome(
                        MovementPathValidator.PermissionFailure(
                            MovementPermissionFailureKind.ParentFrameMismatch,
                            0,
                            op.Path.Origin
                        )
                    )
                );
            }

            List<OccupiedTraversalAllowance> allowances = new List<OccupiedTraversalAllowance>(
                op.Occupants.Count
            );
            foreach (CreatureId occupant in op.Occupants)
                allowances.Add(OccupiedTraversalAllowance.ForAnyPosition(occupant));
            MovementPathValidation validation = validator.Validate(
                context.Snapshot,
                op.Mover,
                op.BudgetId,
                op.Path,
                allowances
            );
            if (!validation.IsValid)
                return Completed(new MovementPermissionRequestOutcome(validation.Failure));

            MovementPermission permission = authority.Issue(
                frame.RootId,
                frame.ParentId.Value,
                op.Mover,
                validation.OccupiedTraversals,
                op.BudgetId,
                op.Path,
                op.Purpose
            );
            return Completed(new MovementPermissionRequestOutcome(permission));
        }

        private static ValueTask<MovementPermissionRequestOutcome> Completed(
            MovementPermissionRequestOutcome outcome
        ) => new ValueTask<MovementPermissionRequestOutcome>(outcome);
    }

    internal sealed class MovePathHandler : IOpHandler<MovePathOp, MovePathOutcome>
    {
        private readonly MovementPathValidator validator;
        private readonly MovementPermission.Authority authority;
        private readonly CommitMovementStepReducer stepReducer;

        public MovePathHandler(
            MovementPathValidator validator,
            MovementPermission.Authority authority,
            CommitMovementStepReducer stepReducer
        )
        {
            this.validator = validator;
            this.authority = authority;
            this.stepReducer = stepReducer;
        }

        public async ValueTask<MovePathOutcome> Handle(
            OpFrame<MovePathOp> frame,
            OpHandlerContext context
        )
        {
            MovePathOp op = frame.Op;
            MovementPermissionFailureKind permissionFailure = authority.Validate(
                op.Permission,
                frame
            );
            if (permissionFailure != MovementPermissionFailureKind.None)
            {
                return Stopped(
                    context,
                    op,
                    0,
                    0,
                    MovementPathValidator.PermissionFailure(permissionFailure, 0, op.Path.Origin)
                );
            }

            MovementFailure provenance = MovementHandlerValidation.ValidateActionProvenance(
                frame.Id,
                op.ActionOpId,
                op.Mover,
                context,
                op.Path.Origin
            );
            if (provenance.Kind != MovementFailureKind.None)
                return Stopped(context, op, 0, 0, provenance);
            if (op.BudgetId.ActionOpId != op.ActionOpId)
            {
                return Stopped(
                    context,
                    op,
                    0,
                    0,
                    new MovementFailure(MovementFailureKind.BudgetMismatch, 0, op.Path.Origin)
                );
            }

            IReadOnlyList<OccupiedTraversalAllowance> allowances = op.Permission.IsNone
                ? Array.Empty<OccupiedTraversalAllowance>()
                : op.Permission.Reservations;
            MovementPathValidation validation = validator.Validate(
                context.Snapshot,
                op.Mover,
                op.BudgetId,
                op.Path,
                allowances
            );
            if (!validation.IsValid)
                return Stopped(context, op, 0, 0, validation.Failure);

            int committedSteps = 0;
            int distanceSpent = 0;
            bool permissionConsumed = false;
            for (int stepIndex = 0; stepIndex < validation.Steps.Count; stepIndex++)
            {
                MovementStepPlan step = validation.Steps[stepIndex];
                MovementTriggerId triggerId = new MovementTriggerId(frame.Id, stepIndex + 1);
                OpResult<MovementTriggerOutcome> trigger = await DispatchDeparture(
                    context,
                    op,
                    step,
                    triggerId
                );
                MovementFailure triggerFailure = GetTriggerFailure(trigger, step.To, triggerId);
                if (triggerFailure.Kind != MovementFailureKind.None)
                    return Stopped(context, op, committedSteps, distanceSpent, triggerFailure);

                // A consecutive run of reserved occupants has to settle with its first legal exit
                // in one transaction so no committed snapshot overlaps the mover with an ally.
                if (ReservedOccupantStillOccupies(context.Snapshot, step))
                {
                    List<CommitMovementStepOp> crossingSteps = new List<CommitMovementStepOp>
                    {
                        CreateCommitOp(op, step, triggerId),
                    };
                    int maximumCrossingEnd = stepIndex;
                    do
                    {
                        maximumCrossingEnd++;
                        MovementStepPlan crossingStep = validation.Steps[maximumCrossingEnd];
                        crossingSteps.Add(
                            CreateCommitOp(
                                op,
                                crossingStep,
                                new MovementTriggerId(frame.Id, maximumCrossingEnd + 1)
                            )
                        );
                    } while (
                        ReservedOccupantStillOccupies(
                            context.Snapshot,
                            validation.Steps[maximumCrossingEnd]
                        )
                    );
                    CommitOccupiedMovementCrossingOp maximumCrossing =
                        new CommitOccupiedMovementCrossingOp(crossingSteps);

                    // Entry middleware may have invalidated the run. Apply the reducer's pure
                    // preparation contract before later departure timing can commit reactions.
                    MovementFailure crossingFailure = stepReducer.ValidateCrossing(
                        maximumCrossing,
                        context.Snapshot
                    );
                    if (crossingFailure.Kind != MovementFailureKind.None)
                    {
                        return Stopped(context, op, committedSteps, distanceSpent, crossingFailure);
                    }

                    MovementFailure laterTriggerFailure = default;
                    int failedTriggerStep = -1;
                    int crossingEnd = maximumCrossingEnd;
                    for (int index = stepIndex + 1; index <= maximumCrossingEnd; index++)
                    {
                        MovementStepPlan crossingStep = validation.Steps[index];
                        MovementTriggerId crossingTriggerId = new MovementTriggerId(
                            frame.Id,
                            index + 1
                        );
                        OpResult<MovementTriggerOutcome> crossingTrigger = await DispatchDeparture(
                            context,
                            op,
                            crossingStep,
                            crossingTriggerId
                        );
                        MovementFailure currentTriggerFailure = GetTriggerFailure(
                            crossingTrigger,
                            crossingStep.To,
                            crossingTriggerId
                        );
                        if (
                            laterTriggerFailure.Kind == MovementFailureKind.None
                            && currentTriggerFailure.Kind != MovementFailureKind.None
                        )
                        {
                            laterTriggerFailure = currentTriggerFailure;
                            failedTriggerStep = index;
                        }
                        if (!ReservedOccupantStillOccupies(context.Snapshot, crossingStep))
                        {
                            crossingEnd = index;
                            break;
                        }
                    }

                    List<CommitMovementStepOp> actualCrossingSteps = crossingSteps.GetRange(
                        0,
                        crossingEnd - stepIndex + 1
                    );
                    bool occupiedReservationRemains = false;
                    for (int index = 0; index + 1 < actualCrossingSteps.Count; index++)
                    {
                        if (
                            ReservedOccupantStillOccupies(
                                context.Snapshot,
                                validation.Steps[stepIndex + index]
                            )
                        )
                        {
                            occupiedReservationRemains = true;
                            break;
                        }
                    }

                    // Timing can vacate every reservation before settlement. Restore ordinary
                    // per-step commits without firing the already-run departure timing twice.
                    if (!occupiedReservationRemains)
                    {
                        for (int index = stepIndex; index <= crossingEnd; index++)
                        {
                            if (index == failedTriggerStep)
                            {
                                return Stopped(
                                    context,
                                    op,
                                    committedSteps,
                                    distanceSpent,
                                    laterTriggerFailure
                                );
                            }
                            MovementStepCommitOutcome committedVacantStep = await CommitStep(
                                context,
                                actualCrossingSteps[index - stepIndex]
                            );
                            if (!committedVacantStep.DidMove)
                            {
                                return Stopped(
                                    context,
                                    op,
                                    committedSteps,
                                    distanceSpent,
                                    committedVacantStep.Failure
                                );
                            }
                            committedSteps++;
                            distanceSpent += committedVacantStep.Cost.Distance.Feet;
                        }
                        stepIndex = crossingEnd;
                        continue;
                    }

                    CommitOccupiedMovementCrossingOp crossingOp =
                        new CommitOccupiedMovementCrossingOp(actualCrossingSteps);
                    RulesSnapshot beforeCrossing = context.Snapshot;
                    OpResult<MovementCrossingCommitOutcome> crossing;
                    try
                    {
                        crossing = await context.Dispatch(crossingOp);
                    }
                    catch
                    {
                        if (
                            !permissionConsumed
                            && CrossingCommitted(beforeCrossing, context.Snapshot, crossingOp)
                        )
                        {
                            authority.Consume(op.Permission);
                        }
                        throw;
                    }

                    MovementCrossingCommitOutcome committedCrossing =
                        MovementHandlerValidation.RequireResolved(crossing, "occupied crossing");
                    if (!committedCrossing.DidMove)
                    {
                        return Stopped(
                            context,
                            op,
                            committedSteps,
                            distanceSpent,
                            committedCrossing.Failure
                        );
                    }

                    committedSteps += actualCrossingSteps.Count;
                    foreach (MovementStepCost cost in committedCrossing.Costs)
                        distanceSpent += cost.Distance.Feet;
                    if (!permissionConsumed && ContainsTraversalFact(crossing))
                    {
                        authority.Consume(op.Permission);
                        permissionConsumed = true;
                    }
                    if (laterTriggerFailure.Kind != MovementFailureKind.None)
                    {
                        return Stopped(
                            context,
                            op,
                            committedSteps,
                            distanceSpent,
                            laterTriggerFailure
                        );
                    }

                    stepIndex = crossingEnd;
                    continue;
                }

                MovementStepCommitOutcome committed = await CommitStep(
                    context,
                    CreateCommitOp(op, step, triggerId)
                );
                if (!committed.DidMove)
                {
                    return Stopped(context, op, committedSteps, distanceSpent, committed.Failure);
                }

                committedSteps++;
                distanceSpent += committed.Cost.Distance.Feet;
            }

            return new MovePathOutcome(
                MovePathStatus.ReachedDestination,
                op.Path.Destination,
                committedSteps,
                new GridDistance(distanceSpent),
                default
            );
        }

        private static ValueTask<OpResult<MovementTriggerOutcome>> DispatchDeparture(
            OpHandlerContext context,
            MovePathOp op,
            MovementStepPlan step,
            MovementTriggerId triggerId
        ) =>
            context.Dispatch(
                new MovementLeavingSquareOp(
                    op.ActionOpId,
                    op.Mover,
                    step.From,
                    step.To,
                    triggerId,
                    MovementTriggerKind.Departure
                )
            );

        private static CommitMovementStepOp CreateCommitOp(
            MovePathOp op,
            MovementStepPlan step,
            MovementTriggerId triggerId
        ) =>
            new CommitMovementStepOp(
                op.ActionOpId,
                op.Mover,
                op.BudgetId,
                step.From,
                step.To,
                step.Cost,
                triggerId,
                MovementTriggerKind.Departure,
                step.Allowance,
                op.PermissionPurpose,
                step.IsDestination
            );

        private static async ValueTask<MovementStepCommitOutcome> CommitStep(
            OpHandlerContext context,
            CommitMovementStepOp op
        )
        {
            OpResult<MovementStepCommitOutcome> commit = await context.Dispatch(op);
            return MovementHandlerValidation.RequireResolved(commit, "movement step");
        }

        private static MovementFailure GetTriggerFailure(
            OpResult<MovementTriggerOutcome> result,
            GridPosition position,
            MovementTriggerId triggerId
        )
        {
            if (result is ResolvedOpResult<MovementTriggerOutcome> resolved)
            {
                return resolved.Value.Decision == MovementTriggerDecision.Continue
                    ? default
                    : new MovementFailure(
                        MovementFailureKind.TriggerInterrupted,
                        triggerId.StepNumber,
                        position
                    );
            }
            if (result is InterruptedOpResult<MovementTriggerOutcome>)
            {
                return new MovementFailure(
                    MovementFailureKind.TriggerInterrupted,
                    triggerId.StepNumber,
                    position
                );
            }
            if (result is InvalidOpResult<MovementTriggerOutcome>)
            {
                return new MovementFailure(
                    MovementFailureKind.TriggerInvalid,
                    triggerId.StepNumber,
                    position
                );
            }
            return new MovementFailure(
                MovementFailureKind.TriggerCancelled,
                triggerId.StepNumber,
                position
            );
        }

        private static bool ContainsTraversalFact<TResult>(OpResult<TResult> result)
        {
            foreach (RuleFact fact in result.Facts)
            {
                if (fact is OccupiedSpaceTraversedFact)
                    return true;
            }
            return false;
        }

        private static bool ReservedOccupantStillOccupies(
            RulesSnapshot snapshot,
            MovementStepPlan step
        ) =>
            step.Allowance.HasOccupant
            && snapshot.Positions.TryGet(step.Allowance.Occupant, out GridPosition occupantPosition)
            && occupantPosition == step.To;

        private static bool CrossingCommitted(
            RulesSnapshot before,
            RulesSnapshot after,
            CommitOccupiedMovementCrossingOp op
        )
        {
            CommitMovementStepOp entry = op.Entry;
            CommitMovementStepOp exit = op.Exit;
            if (after.Version != before.Version + 1)
                return false;
            if (
                !before.Positions.TryGet(entry.Mover, out GridPosition previousPosition)
                || previousPosition != entry.From
                || !after.Positions.TryGet(entry.Mover, out GridPosition currentPosition)
                || currentPosition != exit.To
            )
            {
                return false;
            }
            foreach (CommitMovementStepOp step in op.Steps)
            {
                if (!step.Allowance.HasOccupant)
                    continue;
                bool occupiedBefore = before.Positions.TryGet(
                    step.Allowance.Occupant,
                    out GridPosition occupantPositionBefore
                );
                bool occupiedAfter = after.Positions.TryGet(
                    step.Allowance.Occupant,
                    out GridPosition occupantPositionAfter
                );
                if (
                    occupiedBefore != occupiedAfter
                    || (occupiedBefore && occupantPositionBefore != occupantPositionAfter)
                )
                {
                    return false;
                }
            }
            if (
                !before.MovementBudgets.TryGet(entry.Mover, out MovementBudgetState previousBudget)
                || !after.MovementBudgets.TryGet(entry.Mover, out MovementBudgetState currentBudget)
                || previousBudget.Id != entry.BudgetId
                || currentBudget.Id != entry.BudgetId
            )
            {
                return false;
            }

            return currentBudget.Remaining.Feet < previousBudget.Remaining.Feet;
        }

        private static MovePathOutcome Stopped(
            OpHandlerContext context,
            MovePathOp op,
            int committedSteps,
            int distanceSpent,
            MovementFailure failure
        )
        {
            GridPosition finalPosition = context.Snapshot.Positions.TryGet(
                op.Mover,
                out GridPosition current
            )
                ? current
                : op.Path.Origin;
            return new MovePathOutcome(
                MovePathStatus.Stopped,
                finalPosition,
                committedSteps,
                new GridDistance(distanceSpent),
                failure
            );
        }
    }

    internal sealed class RelocateTokenHandler : IOpHandler<RelocateTokenOp, RelocationOutcome>
    {
        private readonly MovementPathValidator validator;

        public RelocateTokenHandler(MovementPathValidator validator)
        {
            this.validator = validator;
        }

        public async ValueTask<RelocationOutcome> Handle(
            OpFrame<RelocateTokenOp> frame,
            OpHandlerContext context
        )
        {
            RelocateTokenOp op = frame.Op;
            if (
                !context.Trace.Exists(op.OriginOpId)
                || !context.Trace.IsDescendantOf(frame.Id, op.OriginOpId)
            )
            {
                return new RelocationOutcome(
                    false,
                    CurrentOrExpected(context, op),
                    new MovementFailure(
                        MovementFailureKind.InvalidActionProvenance,
                        0,
                        op.ExpectedOrigin
                    )
                );
            }

            MovementFailure preflight = validator.ValidateRelocation(
                context.Snapshot,
                op.Mover,
                op.ExpectedOrigin,
                op.Destination
            );
            if (preflight.Kind != MovementFailureKind.None)
                return new RelocationOutcome(false, CurrentOrExpected(context, op), preflight);

            OpResult<RelocationOutcome> commit = await context.Dispatch(
                new CommitRelocationOp(
                    op.Mover,
                    op.ExpectedOrigin,
                    op.Destination,
                    op.OriginOpId,
                    op.Kind,
                    op.Source
                )
            );
            return MovementHandlerValidation.RequireResolved(commit, "relocation");
        }

        private static GridPosition CurrentOrExpected(
            OpHandlerContext context,
            RelocateTokenOp op
        ) =>
            context.Snapshot.Positions.TryGet(op.Mover, out GridPosition current)
                ? current
                : op.ExpectedOrigin;
    }

    internal static class MovementHandlerValidation
    {
        public static MovementFailure ValidateActionProvenance(
            OpId currentFrameId,
            OpId actionOpId,
            CreatureId mover,
            OpHandlerContext context,
            GridPosition position
        )
        {
            if (
                !context.Trace.Exists(actionOpId)
                || !context.Trace.IsDescendantOf(currentFrameId, actionOpId)
            )
            {
                return new MovementFailure(
                    MovementFailureKind.InvalidActionProvenance,
                    0,
                    position
                );
            }

            try
            {
                return context.Trace.GetAction(actionOpId).Actor == mover
                    ? default
                    : new MovementFailure(MovementFailureKind.InvalidActionProvenance, 0, position);
            }
            catch (InvalidOperationException)
            {
                return new MovementFailure(
                    MovementFailureKind.InvalidActionProvenance,
                    0,
                    position
                );
            }
        }

        public static TResult RequireResolved<TResult>(
            OpResult<TResult> result,
            string operationName
        )
        {
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            throw new InvalidOperationException(
                $"The mandatory {operationName} operation returned {result.Status}."
            );
        }
    }
}
