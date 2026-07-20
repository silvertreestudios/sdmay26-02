using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Registers the complete Unity-free movement command and reducer slice.</summary>
    public static class MovementRuleDispatcherExtensions
    {
        private static readonly RuleSource MovementSource = RuleSource.FromSlug("movement");

        /// <summary>
        /// Adds nested movement budgets, path timing, permission issuance, atomic steps, and relocation.
        /// </summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="topology">The immutable encounter topology used for every validation pass.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseMovementRules(
            this RuleDispatcherBuilder builder,
            GridTopology topology
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (topology == null)
                throw new ArgumentNullException(nameof(topology));

            MovementPathValidator validator = new MovementPathValidator(topology);
            MovementPermission.Authority authority = new MovementPermission.Authority();
            return builder
                .RegisterHandler<BeginMovementBudgetOp, MovementBudgetStartOutcome>(
                    new BeginMovementBudgetHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitMovementBudgetStartOp, MovementBudgetStartOutcome>(
                    new CommitMovementBudgetStartReducer(),
                    MovementSource
                )
                .RegisterHandler<ResetMovementBudgetOp, MovementBudgetResetOutcome>(
                    new ResetMovementBudgetHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitMovementBudgetResetOp, MovementBudgetResetOutcome>(
                    new CommitMovementBudgetResetReducer(),
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
                    new MovePathHandler(validator, authority),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitMovementStepOp, MovementStepCommitOutcome>(
                    new CommitMovementStepReducer(topology),
                    MovementSource
                )
                .RegisterHandler<RelocateTokenOp, RelocationOutcome>(
                    new RelocateTokenHandler(validator),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitRelocationOp, RelocationOutcome>(
                    new CommitRelocationReducer(topology),
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

            MovementPathValidation validation = validator.Validate(
                context.Snapshot,
                op.Mover,
                op.BudgetId,
                op.Path,
                OccupiedTraversalAllowance.ForAnyPosition(op.Occupant)
            );
            if (!validation.IsValid)
                return Completed(new MovementPermissionRequestOutcome(validation.Failure));

            MovementPermission permission = authority.Issue(
                frame.RootId,
                frame.ParentId.Value,
                op.Mover,
                op.Occupant,
                validation.OccupiedPosition,
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

        public MovePathHandler(
            MovementPathValidator validator,
            MovementPermission.Authority authority
        )
        {
            this.validator = validator;
            this.authority = authority;
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

            OccupiedTraversalAllowance allowance = op.Permission.IsNone
                ? OccupiedTraversalAllowance.None
                : OccupiedTraversalAllowance.ForReservedPosition(
                    op.Permission.Occupant,
                    op.Permission.ReservedPosition
                );
            MovementPathValidation validation = validator.Validate(
                context.Snapshot,
                op.Mover,
                op.BudgetId,
                op.Path,
                allowance
            );
            if (!validation.IsValid)
                return Stopped(context, op, 0, 0, validation.Failure);

            int committedSteps = 0;
            int distanceSpent = 0;
            foreach (MovementStepPlan step in validation.Steps)
            {
                MovementTriggerId triggerId = new MovementTriggerId(frame.Id, committedSteps + 1);
                OpResult<MovementTriggerOutcome> trigger = await context.Dispatch(
                    new MovementLeavingSquareOp(
                        op.ActionOpId,
                        op.Mover,
                        step.From,
                        step.To,
                        triggerId,
                        MovementTriggerKind.Departure
                    )
                );
                MovementFailure triggerFailure = GetTriggerFailure(trigger, step.To, triggerId);
                if (triggerFailure.Kind != MovementFailureKind.None)
                    return Stopped(context, op, committedSteps, distanceSpent, triggerFailure);

                OpResult<MovementStepCommitOutcome> commit = await context.Dispatch(
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
                    )
                );
                MovementStepCommitOutcome committed = MovementHandlerValidation.RequireResolved(
                    commit,
                    "movement step"
                );
                if (!committed.DidMove)
                {
                    return Stopped(context, op, committedSteps, distanceSpent, committed.Failure);
                }

                committedSteps++;
                distanceSpent += committed.Cost.Distance.Feet;
                if (ContainsTraversalFact(commit))
                    authority.Consume(op.Permission);
            }

            return new MovePathOutcome(
                MovePathStatus.ReachedDestination,
                op.Path.Destination,
                committedSteps,
                new GridDistance(distanceSpent),
                default
            );
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

        private static bool ContainsTraversalFact(OpResult<MovementStepCommitOutcome> result)
        {
            foreach (RuleFact fact in result.Facts)
            {
                if (fact is OccupiedSpaceTraversedFact)
                    return true;
            }
            return false;
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
