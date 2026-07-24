using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Requests a fresh action-scoped allowance while preserving diagonal phase.</summary>
    public sealed class BeginMovementBudgetOp : IRuleOp<MovementBudgetStartOutcome>
    {
        /// <summary>Initializes a nested budget request for the action that owns this operation.</summary>
        /// <param name="actionOpId">The ancestor action frame opening the allowance.</param>
        /// <param name="mover">The creature that may spend the allowance.</param>
        /// <param name="allowance">The distance supplied by the movement action.</param>
        public BeginMovementBudgetOp(OpId actionOpId, CreatureId mover, GridDistance allowance)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException(
                    "An action operation ID is required.",
                    nameof(actionOpId)
                );
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            ActionOpId = actionOpId;
            Mover = mover;
            Allowance = allowance;
        }

        /// <summary>Gets the ancestor action frame opening the allowance.</summary>
        public OpId ActionOpId { get; }

        /// <summary>Gets the creature that may spend the allowance.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the new action-scoped movement distance.</summary>
        public GridDistance Allowance { get; }
    }

    internal sealed class CommitMovementBudgetStartOp : IRuleOp<MovementBudgetStartOutcome>
    {
        public CommitMovementBudgetStartOp(
            MovementBudgetId budgetId,
            CreatureId mover,
            GridDistance allowance
        )
        {
            if (budgetId.IsEmpty)
                throw new ArgumentException("A movement budget ID is required.", nameof(budgetId));
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            BudgetId = budgetId;
            Mover = mover;
            Allowance = allowance;
        }

        public MovementBudgetId BudgetId { get; }
        public CreatureId Mover { get; }
        public GridDistance Allowance { get; }
    }

    /// <summary>
    /// Requests the nested turn-boundary contract that removes a creature's allowance and resets
    /// its next diagonal to the 5-foot phase.
    /// </summary>
    public sealed class ResetMovementBudgetOp : IRuleOp<MovementBudgetResetOutcome>
    {
        internal ResetMovementBudgetOp(CreatureId mover)
        {
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            Mover = mover;
        }

        /// <summary>Gets the creature whose turn-persistent movement state is reset.</summary>
        public CreatureId Mover { get; }
    }

    internal sealed class CommitMovementBudgetResetOp : IRuleOp<MovementBudgetResetOutcome>
    {
        public CommitMovementBudgetResetOp(CreatureId mover)
        {
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            Mover = mover;
        }

        public CreatureId Mover { get; }
    }

    /// <summary>
    /// Opens the pre-commit timing point immediately before one qualifying path departure.
    /// </summary>
    /// <remarks>
    /// The operation retains the originating action and stable path-step trigger identity even
    /// when the action's frozen profile cannot trigger reactions. Middleware owns eligibility.
    /// For an authorized occupied run, each departure timing operation through its first legal
    /// exit runs while the preceding legal square remains authoritative. Those steps commit
    /// together while any reservation remains; otherwise ordinary step commits resume.
    /// </remarks>
    public sealed class MovementLeavingSquareOp : IRuleOp<MovementTriggerOutcome>
    {
        internal MovementLeavingSquareOp(
            OpId actionOpId,
            CreatureId mover,
            GridPosition from,
            GridPosition to,
            MovementTriggerId triggerId,
            MovementTriggerKind kind
        )
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException(
                    "An action operation ID is required.",
                    nameof(actionOpId)
                );
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            if (triggerId.IsEmpty)
                throw new ArgumentException(
                    "A movement trigger ID is required.",
                    nameof(triggerId)
                );
            if (!Enum.IsDefined(typeof(MovementTriggerKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ActionOpId = actionOpId;
            Mover = mover;
            From = from;
            To = to;
            TriggerId = triggerId;
            Kind = kind;
        }

        /// <summary>Gets the frozen originating action frame.</summary>
        public OpId ActionOpId { get; }

        /// <summary>Gets the creature about to leave <see cref="From"/>.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the still-authoritative departure cell.</summary>
        public GridPosition From { get; }

        /// <summary>Gets the intended next cell.</summary>
        public GridPosition To { get; }

        /// <summary>Gets the stable path-step trigger identity.</summary>
        public MovementTriggerId TriggerId { get; }

        /// <summary>Gets the semantic timing kind.</summary>
        public MovementTriggerKind Kind { get; }
    }

    /// <summary>Requests engine authority for occupied crossings on one exact path.</summary>
    public sealed class RequestMovementPermissionOp : IRuleOp<MovementPermissionRequestOutcome>
    {
        /// <summary>Initializes a nested request that reserves one occupied-space crossing.</summary>
        /// <param name="actionOpId">The ancestor action frame requesting authority.</param>
        /// <param name="mover">The creature that intends to cross the occupied cell.</param>
        /// <param name="occupant">The creature whose exact occupied cell is reserved.</param>
        /// <param name="budgetId">The current action-scoped movement budget.</param>
        /// <param name="path">The exact path for which permission is requested.</param>
        /// <param name="purpose">The non-ordinary rule use authorizing the crossing.</param>
        public RequestMovementPermissionOp(
            OpId actionOpId,
            CreatureId mover,
            CreatureId occupant,
            MovementBudgetId budgetId,
            MovementPath path,
            MovementPermissionPurpose purpose
        )
            : this(actionOpId, mover, new[] { occupant }, budgetId, path, purpose) { }

        /// <summary>Initializes a nested request that reserves every occupied crossing.</summary>
        /// <param name="actionOpId">The ancestor action frame requesting authority.</param>
        /// <param name="mover">The creature that intends to cross the occupied cells.</param>
        /// <param name="occupants">
        /// The ordered occupants crossed by the path. Repeated entries represent repeated
        /// crossings of the same creature's space.
        /// </param>
        /// <param name="budgetId">The current action-scoped movement budget.</param>
        /// <param name="path">The exact path for which permission is requested.</param>
        /// <param name="purpose">The non-ordinary rule use authorizing the crossings.</param>
        public RequestMovementPermissionOp(
            OpId actionOpId,
            CreatureId mover,
            IEnumerable<CreatureId> occupants,
            MovementBudgetId budgetId,
            MovementPath path,
            MovementPermissionPurpose purpose
        )
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException(
                    "An action operation ID is required.",
                    nameof(actionOpId)
                );
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            if (occupants == null)
                throw new ArgumentNullException(nameof(occupants));
            CreatureId[] requestedOccupants = occupants.ToArray();
            if (requestedOccupants.Length == 0 || requestedOccupants.Any(value => value.IsEmpty))
            {
                throw new ArgumentException(
                    "At least one non-empty occupant is required.",
                    nameof(occupants)
                );
            }
            if (budgetId.IsEmpty)
                throw new ArgumentException("A movement budget ID is required.", nameof(budgetId));
            if (purpose.IsEmpty || purpose == MovementPermissionPurpose.Ordinary)
                throw new ArgumentException(
                    "Occupied traversal requires a non-ordinary purpose.",
                    nameof(purpose)
                );
            ActionOpId = actionOpId;
            Mover = mover;
            Occupants = new ReadOnlyCollection<CreatureId>(requestedOccupants);
            BudgetId = budgetId;
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Purpose = purpose;
        }

        /// <summary>Gets the ancestor action frame requesting authority.</summary>
        public OpId ActionOpId { get; }

        /// <summary>Gets the creature that intends to cross the occupied cell.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the ordered creatures whose occupied cells are reserved.</summary>
        public IReadOnlyList<CreatureId> Occupants { get; }

        /// <summary>Gets the movement budget to which authority is bound.</summary>
        public MovementBudgetId BudgetId { get; }

        /// <summary>Gets the exact authorized path.</summary>
        public MovementPath Path { get; }

        /// <summary>Gets the intended non-ordinary use.</summary>
        public MovementPermissionPurpose Purpose { get; }
    }

    /// <summary>
    /// Performs nested path movement without representing or spending another PF2e action.
    /// </summary>
    public sealed class MovePathOp : IRuleOp<MovePathOutcome>
    {
        /// <summary>Initializes ordinary nested movement that cannot enter occupied cells.</summary>
        /// <param name="actionOpId">The ancestor action frame that caused movement.</param>
        /// <param name="mover">The creature to move.</param>
        /// <param name="budgetId">The current action-scoped budget to spend.</param>
        /// <param name="path">The exact path to validate and commit.</param>
        public MovePathOp(
            OpId actionOpId,
            CreatureId mover,
            MovementBudgetId budgetId,
            MovementPath path
        )
            : this(
                actionOpId,
                mover,
                budgetId,
                path,
                MovementPermission.None,
                MovementPermissionPurpose.Ordinary
            ) { }

        /// <summary>Initializes nested movement using engine-issued occupied-space authority.</summary>
        /// <param name="actionOpId">The ancestor action frame that caused movement.</param>
        /// <param name="mover">The creature to move.</param>
        /// <param name="budgetId">The current action-scoped budget to spend.</param>
        /// <param name="path">The exact path bound to the permission.</param>
        /// <param name="permission">The engine-issued occupied-space authority.</param>
        /// <param name="permissionPurpose">The intended use bound to the permission.</param>
        public MovePathOp(
            OpId actionOpId,
            CreatureId mover,
            MovementBudgetId budgetId,
            MovementPath path,
            MovementPermission permission,
            MovementPermissionPurpose permissionPurpose
        )
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException(
                    "An action operation ID is required.",
                    nameof(actionOpId)
                );
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            if (budgetId.IsEmpty)
                throw new ArgumentException("A movement budget ID is required.", nameof(budgetId));
            if (permissionPurpose.IsEmpty)
                throw new ArgumentException(
                    "A permission purpose is required.",
                    nameof(permissionPurpose)
                );
            ActionOpId = actionOpId;
            Mover = mover;
            BudgetId = budgetId;
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Permission = permission ?? throw new ArgumentNullException(nameof(permission));
            PermissionPurpose = permissionPurpose;
        }

        /// <summary>Gets the frozen originating action frame.</summary>
        public OpId ActionOpId { get; }

        /// <summary>Gets the creature whose authoritative position may change.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the action-scoped budget spent by successful steps.</summary>
        public MovementBudgetId BudgetId { get; }

        /// <summary>Gets the exact immutable path.</summary>
        public MovementPath Path { get; }

        /// <summary>Gets the opaque occupied-space authority, or <see cref="MovementPermission.None"/>.</summary>
        public MovementPermission Permission { get; }

        /// <summary>Gets the declared intended permission use.</summary>
        public MovementPermissionPurpose PermissionPurpose { get; }
    }

    internal sealed class CommitMovementStepOp : IRuleOp<MovementStepCommitOutcome>
    {
        public CommitMovementStepOp(
            OpId actionOpId,
            CreatureId mover,
            MovementBudgetId budgetId,
            GridPosition from,
            GridPosition to,
            MovementStepCost expectedCost,
            MovementTriggerId triggerId,
            MovementTriggerKind triggerKind,
            OccupiedTraversalAllowance allowance,
            MovementPermissionPurpose permissionPurpose,
            bool isDestination
        )
        {
            ActionOpId = actionOpId;
            Mover = mover;
            BudgetId = budgetId;
            From = from;
            To = to;
            ExpectedCost = expectedCost;
            TriggerId = triggerId;
            TriggerKind = triggerKind;
            Allowance = allowance;
            PermissionPurpose = permissionPurpose;
            IsDestination = isDestination;
        }

        public OpId ActionOpId { get; }
        public CreatureId Mover { get; }
        public MovementBudgetId BudgetId { get; }
        public GridPosition From { get; }
        public GridPosition To { get; }
        public MovementStepCost ExpectedCost { get; }
        public MovementTriggerId TriggerId { get; }
        public MovementTriggerKind TriggerKind { get; }
        public OccupiedTraversalAllowance Allowance { get; }
        public MovementPermissionPurpose PermissionPurpose { get; }
        public bool IsDestination { get; }
    }

    /// <summary>
    /// Commits one or more reserved occupied entries through the first currently legal exit.
    /// </summary>
    /// <remarks>
    /// The reducer validates the complete sequence before mutation so intermediate overlaps never
    /// escape as authoritative snapshots, even when consecutive creatures occupy the path.
    /// </remarks>
    internal sealed class CommitOccupiedMovementCrossingOp : IRuleOp<MovementCrossingCommitOutcome>
    {
        public CommitOccupiedMovementCrossingOp(
            CommitMovementStepOp entry,
            CommitMovementStepOp exit
        )
            : this(new[] { entry, exit }) { }

        public CommitOccupiedMovementCrossingOp(IEnumerable<CommitMovementStepOp> steps)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps));
            CommitMovementStepOp[] crossingSteps = steps.ToArray();
            if (crossingSteps.Length < 2 || crossingSteps.Any(step => step == null))
            {
                throw new ArgumentException(
                    "An occupied crossing requires at least two valid steps.",
                    nameof(steps)
                );
            }
            if (!crossingSteps[0].Allowance.HasOccupant)
                throw new ArgumentException(
                    "An occupied crossing requires an authorized entry step.",
                    nameof(steps)
                );
            for (int index = 1; index < crossingSteps.Length; index++)
            {
                if (crossingSteps[index - 1].To == crossingSteps[index].From)
                    continue;
                throw new ArgumentException(
                    "An occupied crossing requires one contiguous sequence of steps.",
                    nameof(steps)
                );
            }
            Steps = new ReadOnlyCollection<CommitMovementStepOp>(crossingSteps);
        }

        public IReadOnlyList<CommitMovementStepOp> Steps { get; }
        public CommitMovementStepOp Entry => Steps[0];
        public CommitMovementStepOp Exit => Steps[Steps.Count - 1];
    }

    /// <summary>
    /// Requests a nested authoritative relocation that spends no action or movement budget.
    /// </summary>
    public sealed class RelocateTokenOp : IRuleOp<RelocationOutcome>, IRuleSourcedOp
    {
        /// <summary>Initializes a nested authoritative relocation with explicit provenance.</summary>
        /// <param name="mover">The creature whose position changes.</param>
        /// <param name="expectedOrigin">The position that must still be authoritative.</param>
        /// <param name="destination">The legal, unoccupied destination.</param>
        /// <param name="originOpId">The ancestor operation that caused relocation.</param>
        /// <param name="kind">The mechanical relocation reason.</param>
        /// <param name="source">The rule source stamped on the relocation Fact.</param>
        public RelocateTokenOp(
            CreatureId mover,
            GridPosition expectedOrigin,
            GridPosition destination,
            OpId originOpId,
            RelocationKind kind,
            RuleSource source
        )
        {
            if (mover.IsEmpty)
                throw new ArgumentException("A mover is required.", nameof(mover));
            if (originOpId.IsEmpty)
                throw new ArgumentException(
                    "An origin operation ID is required.",
                    nameof(originOpId)
                );
            if (kind.IsEmpty)
                throw new ArgumentException("A relocation kind is required.", nameof(kind));
            if (source.IsEmpty)
                throw new ArgumentException("A relocation source is required.", nameof(source));
            Mover = mover;
            ExpectedOrigin = expectedOrigin;
            Destination = destination;
            OriginOpId = originOpId;
            Kind = kind;
            Source = source;
        }

        /// <summary>Gets the creature whose position may change.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the position expected immediately before relocation.</summary>
        public GridPosition ExpectedOrigin { get; }

        /// <summary>Gets the requested final position.</summary>
        public GridPosition Destination { get; }

        /// <summary>Gets the ancestor operation that caused the relocation.</summary>
        public OpId OriginOpId { get; }

        /// <summary>Gets the mechanical relocation reason.</summary>
        public RelocationKind Kind { get; }

        /// <summary>Gets the rule source stamped onto the committed relocation Fact.</summary>
        public RuleSource Source { get; }
    }

    internal sealed class CommitRelocationOp : IRuleOp<RelocationOutcome>, IRuleSourcedOp
    {
        public CommitRelocationOp(
            CreatureId mover,
            GridPosition expectedOrigin,
            GridPosition destination,
            OpId originOpId,
            RelocationKind kind,
            RuleSource source
        )
        {
            Mover = mover;
            ExpectedOrigin = expectedOrigin;
            Destination = destination;
            OriginOpId = originOpId;
            Kind = kind;
            Source = source;
        }

        public CreatureId Mover { get; }
        public GridPosition ExpectedOrigin { get; }
        public GridPosition Destination { get; }
        public OpId OriginOpId { get; }
        public RelocationKind Kind { get; }
        public RuleSource Source { get; }
    }

    /// <summary>
    /// Reports whether the explicit encounter or exploration transition removed a movement
    /// budget.
    /// </summary>
    public readonly struct MovementBudgetResetOutcome : IEquatable<MovementBudgetResetOutcome>
    {
        internal MovementBudgetResetOutcome(bool wasReset) => WasReset = wasReset;

        /// <summary>Gets whether an existing movement budget was removed.</summary>
        public bool WasReset { get; }

        /// <inheritdoc/>
        public bool Equals(MovementBudgetResetOutcome other) => WasReset == other.WasReset;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is MovementBudgetResetOutcome other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => WasReset.GetHashCode();
    }

    /// <summary>Reports whether an action-scoped movement budget was opened.</summary>
    public readonly struct MovementBudgetStartOutcome
    {
        internal MovementBudgetStartOutcome(MovementBudgetState budget)
        {
            IsStarted = true;
            Budget = budget;
            Failure = default;
        }

        internal MovementBudgetStartOutcome(MovementFailure failure)
        {
            IsStarted = false;
            Budget = default;
            Failure = failure;
        }

        /// <summary>Gets whether the budget was committed.</summary>
        public bool IsStarted { get; }

        /// <summary>Gets the committed budget when <see cref="IsStarted"/> is true.</summary>
        public MovementBudgetState Budget { get; }

        /// <summary>Gets the typed failure when the budget was not started.</summary>
        public MovementFailure Failure { get; }
    }

    /// <summary>Reports whether occupied-space authority was issued.</summary>
    public readonly struct MovementPermissionRequestOutcome
    {
        internal MovementPermissionRequestOutcome(MovementPermission permission)
        {
            IsGranted = true;
            Permission = permission;
            Failure = default;
        }

        internal MovementPermissionRequestOutcome(MovementFailure failure)
        {
            IsGranted = false;
            Permission = MovementPermission.None;
            Failure = failure;
        }

        /// <summary>Gets whether the engine issued a permission.</summary>
        public bool IsGranted { get; }

        /// <summary>Gets the opaque permission, or <see cref="MovementPermission.None"/>.</summary>
        public MovementPermission Permission { get; }

        /// <summary>Gets the typed rejection when permission was not granted.</summary>
        public MovementFailure Failure { get; }
    }

    internal readonly struct MovementStepCommitOutcome
    {
        public MovementStepCommitOutcome(MovementStepCost cost, GridDistance remaining)
        {
            DidMove = true;
            Cost = cost;
            Remaining = remaining;
            Failure = default;
        }

        public MovementStepCommitOutcome(MovementFailure failure)
        {
            DidMove = false;
            Cost = default;
            Remaining = default;
            Failure = failure;
        }

        public bool DidMove { get; }
        public MovementStepCost Cost { get; }
        public GridDistance Remaining { get; }
        public MovementFailure Failure { get; }
    }

    internal readonly struct MovementCrossingCommitOutcome
    {
        public MovementCrossingCommitOutcome(
            IEnumerable<MovementStepCost> costs,
            GridDistance remaining
        )
        {
            DidMove = true;
            Costs = new ReadOnlyCollection<MovementStepCost>(costs.ToArray());
            Remaining = remaining;
            Failure = default;
        }

        public MovementCrossingCommitOutcome(MovementFailure failure)
        {
            DidMove = false;
            Costs = Array.AsReadOnly(Array.Empty<MovementStepCost>());
            Remaining = default;
            Failure = failure;
        }

        public bool DidMove { get; }
        public IReadOnlyList<MovementStepCost> Costs { get; }
        public GridDistance Remaining { get; }
        public MovementFailure Failure { get; }
    }
}
