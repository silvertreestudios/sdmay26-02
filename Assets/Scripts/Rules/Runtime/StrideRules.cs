using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Defines the one-action Stride workflow and its immutable base profile.</summary>
    public sealed class StrideActionDefinition
        : IActionDefinition<MovementPath, StrideActionOp, MovePathOutcome>,
            IActionCatalog
    {
        private static readonly Trait MoveTrait = Trait.FromSlug("move");
        private static readonly ActionProfile Profile = ActionProfile.OneAction(
            new[] { MoveTrait }
        );
        private readonly IGridTopologyProvider topologyProvider;
        private readonly MovementPathValidator pathValidator;

        /// <summary>Gets Stride's stable action-definition identity.</summary>
        public static ActionDefinitionId DefinitionId { get; } = new ActionDefinitionId("stride");

        /// <summary>Creates a Stride definition against encounter topology snapshots.</summary>
        /// <param name="topologyProvider">
        /// The provider whose current immutable topology is captured for preview and read during
        /// authoritative validation.
        /// </param>
        public StrideActionDefinition(IGridTopologyProvider topologyProvider)
        {
            this.topologyProvider =
                topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
            pathValidator = new MovementPathValidator(topologyProvider);
        }

        /// <inheritdoc/>
        public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (actor.IsEmpty)
                throw new ArgumentException("An actor is required.", nameof(actor));
            if (!snapshot.Creatures.TryGet(actor, out CreatureState _))
                return ActionAvailability.Unavailable("The actor is not registered.");
            if (!snapshot.Positions.TryGet(actor, out GridPosition _))
                return ActionAvailability.Unavailable("The actor has no grid position.");
            if (!snapshot.LandSpeeds.TryGet(actor, out GridDistance speed) || speed.Feet == 0)
            {
                return ActionAvailability.Unavailable("The actor has no land Speed.");
            }
            if (
                !snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                || economy.ActionsRemaining < ActionCost.One.Amount
            )
            {
                return ActionAvailability.Unavailable("The actor does not have an action.");
            }
            return ActionAvailability.Available;
        }

        /// <inheritdoc/>
        public SelectionWorkflow<MovementPath> CreateSelectionWorkflow(
            RulesSnapshot snapshot,
            CreatureId actor
        )
        {
            ActionAvailability availability = GetAvailability(snapshot, actor);
            if (availability is UnavailableActionAvailability unavailable)
                return SelectionWorkflow.Invalid<MovementPath>(unavailable.Reason);

            GridPosition origin = snapshot.Positions[actor];
            GridDistance speed = snapshot.LandSpeeds[actor];
            DiagonalMovementPhase phase = StridePathRules.GetDiagonalPhase(snapshot, actor);
            MovementPathValidator previewValidator = new MovementPathValidator(
                new FixedGridTopologyProvider(topologyProvider.Current)
            );
            return SelectionWorkflow.From(
                new StridePathSelectionRequest(
                    snapshot,
                    actor,
                    origin,
                    speed,
                    phase,
                    previewValidator
                )
            );
        }

        /// <inheritdoc/>
        public StrideActionOp CreateOp(CreatureId actor, MovementPath selection) =>
            new StrideActionOp(actor, selection);

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId != DefinitionId)
                throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
            return Profile;
        }

        internal MovementPathValidation ValidatePath(
            RulesSnapshot snapshot,
            CreatureId actor,
            MovementPath path
        ) => StridePathRules.Validate(pathValidator, snapshot, actor, path);
    }

    /// <summary>Requests one complete path for a Stride preview.</summary>
    public sealed class StridePathSelectionRequest : ActionSelectionRequest<MovementPath>
    {
        private readonly RulesSnapshot snapshot;
        private readonly DiagonalMovementPhase diagonalPhase;
        private readonly MovementPathValidator pathValidator;

        internal StridePathSelectionRequest(
            RulesSnapshot snapshot,
            CreatureId actor,
            GridPosition origin,
            GridDistance maximumDistance,
            DiagonalMovementPhase diagonalPhase,
            MovementPathValidator pathValidator
        )
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            if (actor.IsEmpty)
                throw new ArgumentException("An actor is required.", nameof(actor));
            Actor = actor;
            Origin = origin;
            MaximumDistance = maximumDistance;
            this.diagonalPhase = diagonalPhase;
            this.pathValidator =
                pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
        }

        /// <summary>Gets the creature selecting a path.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the position from which every accepted path must begin.</summary>
        public GridPosition Origin { get; }

        /// <summary>Gets the maximum movement distance available to the preview.</summary>
        public GridDistance MaximumDistance { get; }

        /// <inheritdoc/>
        public override bool Accepts(MovementPath selection)
        {
            if (selection == null || selection.Origin != Origin)
                return false;
            return StridePathRules
                .Validate(pathValidator, snapshot, Actor, selection, MaximumDistance, diagonalPhase)
                .IsValid;
        }
    }

    /// <summary>Represents one authoritative one-action Stride attempt.</summary>
    public sealed class StrideActionOp : ActionOp<MovePathOutcome>
    {
        /// <summary>Creates an immutable Stride attempt.</summary>
        /// <param name="actor">The creature attempting to Stride.</param>
        /// <param name="path">The complete path selected before dispatch.</param>
        public StrideActionOp(CreatureId actor, MovementPath path)
            : base(actor, StrideActionDefinition.DefinitionId) =>
            Path = path ?? throw new ArgumentNullException(nameof(path));

        /// <summary>Gets the exact path to validate and attempt.</summary>
        public MovementPath Path { get; }
    }

    /// <summary>Registers the pure Stride action on a rules dispatcher.</summary>
    public static class StrideRuleDispatcherExtensions
    {
        /// <summary>Adds Stride validation and execution to a configured action runtime.</summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="definition">The shared Stride definition and topology boundary.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseStrideRules(
            this RuleDispatcherBuilder builder,
            StrideActionDefinition definition
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            return builder
                .RegisterHandler<StrideActionOp, MovePathOutcome>(new StrideActionHandler())
                .RegisterActionValidator(new StrideActionValidator(definition));
        }
    }

    internal sealed class StrideActionValidator : IActionValidator<StrideActionOp>
    {
        private readonly StrideActionDefinition definition;

        public StrideActionValidator(StrideActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        public ActionValidationResult Validate(
            OpFrame<StrideActionOp> frame,
            RulesSnapshot snapshot
        )
        {
            MovementPathValidation validation = definition.ValidatePath(
                snapshot,
                frame.Op.Actor,
                frame.Op.Path
            );
            return validation.IsValid
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid(StridePathRules.Describe(validation.Failure));
        }
    }

    internal sealed class StrideActionHandler : IOpHandler<StrideActionOp, MovePathOutcome>
    {
        private static readonly MovementPermissionPurpose FriendlyTraversal =
            MovementPermissionPurpose.FromSlug("friendly-traversal");

        public async ValueTask<MovePathOutcome> Handle(
            OpFrame<StrideActionOp> frame,
            OpHandlerContext context
        )
        {
            StrideActionOp op = frame.Op;
            if (!context.Snapshot.LandSpeeds.TryGet(op.Actor, out GridDistance speed))
                return Stopped(op.Path.Origin, MovementFailureKind.MissingBudget);

            MovementBudgetStartOutcome started = RequireResolved(
                await context.Dispatch(new BeginMovementBudgetOp(frame.Id, op.Actor, speed)),
                "Stride movement budget"
            );
            if (!started.IsStarted)
                return new MovePathOutcome(
                    MovePathStatus.Stopped,
                    op.Path.Origin,
                    0,
                    default,
                    started.Failure
                );

            CreatureId occupant = StridePathRules.FindIntermediateOccupant(
                context.Snapshot,
                op.Actor,
                op.Path
            );
            if (occupant.IsEmpty)
            {
                return RequireResolved(
                    await context.Dispatch(
                        new MovePathOp(frame.Id, op.Actor, started.Budget.Id, op.Path)
                    ),
                    "Stride path"
                );
            }
            if (!StridePathRules.AreFriendly(context.Snapshot, op.Actor, occupant))
                return Stopped(op.Path.Origin, MovementFailureKind.Occupied);

            MovementPermissionRequestOutcome permission = RequireResolved(
                await context.Dispatch(
                    new RequestMovementPermissionOp(
                        frame.Id,
                        op.Actor,
                        occupant,
                        started.Budget.Id,
                        op.Path,
                        FriendlyTraversal
                    )
                ),
                "Stride friendly traversal"
            );
            if (!permission.IsGranted)
            {
                return new MovePathOutcome(
                    MovePathStatus.Stopped,
                    op.Path.Origin,
                    0,
                    default,
                    permission.Failure
                );
            }

            return RequireResolved(
                await context.Dispatch(
                    new MovePathOp(
                        frame.Id,
                        op.Actor,
                        started.Budget.Id,
                        op.Path,
                        permission.Permission,
                        FriendlyTraversal
                    )
                ),
                "Stride path"
            );
        }

        private static TResult RequireResolved<TResult>(
            OpResult<TResult> result,
            string operationName
        )
        {
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            throw new InvalidOperationException($"{operationName} did not resolve.");
        }

        private static MovePathOutcome Stopped(GridPosition position, MovementFailureKind kind) =>
            new MovePathOutcome(
                MovePathStatus.Stopped,
                position,
                0,
                default,
                new MovementFailure(kind, 0, position)
            );
    }

    internal static class StridePathRules
    {
        public static MovementPathValidation Validate(
            MovementPathValidator validator,
            RulesSnapshot snapshot,
            CreatureId actor,
            MovementPath path
        )
        {
            if (!snapshot.LandSpeeds.TryGet(actor, out GridDistance speed))
            {
                return MovementPathValidation.Rejected(
                    new MovementFailure(MovementFailureKind.MissingBudget, 0, path.Origin)
                );
            }
            return Validate(
                validator,
                snapshot,
                actor,
                path,
                speed,
                GetDiagonalPhase(snapshot, actor)
            );
        }

        public static MovementPathValidation Validate(
            MovementPathValidator validator,
            RulesSnapshot snapshot,
            CreatureId actor,
            MovementPath path,
            GridDistance speed,
            DiagonalMovementPhase phase
        )
        {
            CreatureId occupant = FindIntermediateOccupant(snapshot, actor, path);
            if (!occupant.IsEmpty && !AreFriendly(snapshot, actor, occupant))
            {
                return MovementPathValidation.Rejected(
                    new MovementFailure(MovementFailureKind.Occupied, 0, path.Origin)
                );
            }
            OccupiedTraversalAllowance allowance = occupant.IsEmpty
                ? OccupiedTraversalAllowance.None
                : OccupiedTraversalAllowance.ForAnyPosition(occupant);
            return validator.ValidateActionPath(snapshot, actor, path, speed, phase, allowance);
        }

        public static CreatureId FindIntermediateOccupant(
            RulesSnapshot snapshot,
            CreatureId actor,
            MovementPath path
        )
        {
            for (int index = 0; index + 1 < path.Steps.Count; index++)
            {
                if (
                    MovementPathValidator.TryFindBlockingOccupant(
                        snapshot,
                        actor,
                        path.Steps[index],
                        out CreatureId occupant
                    )
                )
                {
                    return occupant;
                }
            }
            return default;
        }

        public static bool AreFriendly(
            RulesSnapshot snapshot,
            CreatureId actor,
            CreatureId occupant
        ) =>
            snapshot.Creatures.TryGet(actor, out CreatureState actorState)
            && snapshot.Creatures.TryGet(occupant, out CreatureState occupantState)
            && actorState.Player == occupantState.Player;

        public static DiagonalMovementPhase GetDiagonalPhase(
            RulesSnapshot snapshot,
            CreatureId actor
        ) =>
            snapshot.MovementBudgets.TryGet(actor, out MovementBudgetState budget)
                ? budget.DiagonalPhase
                : DiagonalMovementPhase.NextCostsFiveFeet;

        public static string Describe(MovementFailure failure) =>
            $"Stride path was rejected: {failure.Kind} at step {failure.StepNumber}.";
    }
}
