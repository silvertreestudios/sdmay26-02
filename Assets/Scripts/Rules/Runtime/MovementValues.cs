using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies one action-scoped movement allowance by its dispatcher-owned action frame.
    /// </summary>
    public readonly struct MovementBudgetId : IEquatable<MovementBudgetId>
    {
        internal MovementBudgetId(OpId actionOpId)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException(
                    "An action operation ID is required.",
                    nameof(actionOpId)
                );
            ActionOpId = actionOpId;
        }

        /// <summary>Gets the action frame that opened this budget.</summary>
        public OpId ActionOpId { get; }

        /// <summary>Gets whether this value is the unallocated default identity.</summary>
        public bool IsEmpty => ActionOpId.IsEmpty;

        /// <inheritdoc/>
        public bool Equals(MovementBudgetId other) => ActionOpId == other.ActionOpId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MovementBudgetId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ActionOpId.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => ActionOpId.ToString();

        /// <summary>Compares two budget identities.</summary>
        public static bool operator ==(MovementBudgetId left, MovementBudgetId right) =>
            left.Equals(right);

        /// <summary>Compares two budget identities.</summary>
        public static bool operator !=(MovementBudgetId left, MovementBudgetId right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Stores one creature's active action-scoped allowance and turn-persistent diagonal phase.
    /// </summary>
    /// <remarks>
    /// Beginning another movement action replaces the allowance and identity but preserves
    /// <see cref="DiagonalPhase"/>. Encounter timing resets the entire value at the turn boundary.
    /// </remarks>
    public readonly struct MovementBudgetState : IEquatable<MovementBudgetState>
    {
        /// <summary>Initializes an authoritative movement budget.</summary>
        /// <param name="id">The action-scoped budget identity.</param>
        /// <param name="owner">The creature allowed to spend the budget.</param>
        /// <param name="remaining">The unspent movement distance.</param>
        /// <param name="diagonalPhase">The price of the next diagonal step this turn.</param>
        public MovementBudgetState(
            MovementBudgetId id,
            CreatureId owner,
            GridDistance remaining,
            DiagonalMovementPhase diagonalPhase
        )
        {
            if (id.IsEmpty)
                throw new ArgumentException("A movement budget ID is required.", nameof(id));
            if (owner.IsEmpty)
                throw new ArgumentException("A movement budget owner is required.", nameof(owner));
            if (!Enum.IsDefined(typeof(DiagonalMovementPhase), diagonalPhase))
                throw new ArgumentOutOfRangeException(nameof(diagonalPhase));

            Id = id;
            Owner = owner;
            Remaining = remaining;
            DiagonalPhase = diagonalPhase;
        }

        /// <summary>Gets the action-scoped identity.</summary>
        public MovementBudgetId Id { get; }

        /// <summary>Gets the creature allowed to spend this budget.</summary>
        public CreatureId Owner { get; }

        /// <summary>Gets the unspent movement distance.</summary>
        public GridDistance Remaining { get; }

        /// <summary>Gets the price phase for the next diagonal step this turn.</summary>
        public DiagonalMovementPhase DiagonalPhase { get; }

        /// <inheritdoc/>
        public bool Equals(MovementBudgetState other) =>
            Id == other.Id
            && Owner == other.Owner
            && Remaining == other.Remaining
            && DiagonalPhase == other.DiagonalPhase;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is MovementBudgetState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Id, Owner, Remaining, DiagonalPhase);

        /// <summary>Compares two budget states by value.</summary>
        public static bool operator ==(MovementBudgetState left, MovementBudgetState right) =>
            left.Equals(right);

        /// <summary>Compares two budget states by value.</summary>
        public static bool operator !=(MovementBudgetState left, MovementBudgetState right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Captures an immutable path with an explicit expected origin and ordered destination steps.
    /// </summary>
    public sealed class MovementPath : IEquatable<MovementPath>
    {
        private readonly IReadOnlyList<GridPosition> steps;

        /// <summary>Initializes a path and defensively copies its ordered steps.</summary>
        /// <param name="origin">The authoritative position expected before the first step.</param>
        /// <param name="steps">The cells entered in order, excluding <paramref name="origin"/>.</param>
        public MovementPath(GridPosition origin, IEnumerable<GridPosition> steps)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps));

            Origin = origin;
            this.steps = new ReadOnlyCollection<GridPosition>(steps.ToArray());
        }

        /// <summary>Gets the position expected before movement begins.</summary>
        public GridPosition Origin { get; }

        /// <summary>Gets the ordered cells entered after <see cref="Origin"/>.</summary>
        public IReadOnlyList<GridPosition> Steps => steps;

        /// <summary>Gets the requested final cell, or the origin when the path has no steps.</summary>
        public GridPosition Destination => steps.Count == 0 ? Origin : steps[steps.Count - 1];

        /// <inheritdoc/>
        public bool Equals(MovementPath other) =>
            other != null && Origin == other.Origin && steps.SequenceEqual(other.steps);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MovementPath other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int hash = Origin.GetHashCode();
            foreach (GridPosition step in steps)
                hash = HashCode.Combine(hash, step);
            return hash;
        }
    }

    /// <summary>
    /// Names the rule-specific intended use of an occupied-space movement permission.
    /// </summary>
    public readonly struct MovementPermissionPurpose : IEquatable<MovementPermissionPurpose>
    {
        /// <summary>Gets the purpose used by ordinary movement that permits no occupied crossing.</summary>
        public static MovementPermissionPurpose Ordinary { get; } = FromSlug("ordinary");

        private MovementPermissionPurpose(string slug) =>
            Slug = StableId.Require(slug, nameof(slug));

        /// <summary>Gets the canonical purpose slug.</summary>
        public string Slug { get; }

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        /// <summary>Creates a canonical open purpose value from data or a rule definition.</summary>
        public static MovementPermissionPurpose FromSlug(string value) =>
            new MovementPermissionPurpose(Pf2eSlug.FromName(value));

        /// <inheritdoc/>
        public bool Equals(MovementPermissionPurpose other) =>
            string.Equals(Slug, other.Slug, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is MovementPermissionPurpose other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Slug ?? string.Empty;

        /// <summary>Compares two permission purposes by canonical slug.</summary>
        public static bool operator ==(
            MovementPermissionPurpose left,
            MovementPermissionPurpose right
        ) => left.Equals(right);

        /// <summary>Compares two permission purposes by canonical slug.</summary>
        public static bool operator !=(
            MovementPermissionPurpose left,
            MovementPermissionPurpose right
        ) => !left.Equals(right);
    }

    /// <summary>
    /// Represents opaque engine authority to traverse one reserved occupied-space path.
    /// </summary>
    /// <remarks>
    /// Callers can hold this immutable value but cannot construct an authorized instance. The
    /// movement runtime issues it for one root, parent frame, mover, occupant, budget, exact path,
    /// and purpose, then consumes it after the authorized occupied step commits.
    /// </remarks>
    public sealed class MovementPermission
    {
        /// <summary>Gets the ordinary value that permits no occupied-space traversal.</summary>
        public static MovementPermission None { get; } = new MovementPermission();

        private MovementPermission() { }

        private MovementPermission(
            OpId rootId,
            OpId parentFrameId,
            CreatureId mover,
            CreatureId occupant,
            GridPosition reservedPosition,
            MovementBudgetId budgetId,
            MovementPath path,
            MovementPermissionPurpose purpose
        )
        {
            RootId = rootId;
            ParentFrameId = parentFrameId;
            Mover = mover;
            Occupant = occupant;
            ReservedPosition = reservedPosition;
            BudgetId = budgetId;
            Path = path;
            Purpose = purpose;
        }

        internal bool IsNone => ReferenceEquals(this, None);
        internal OpId RootId { get; }
        internal OpId ParentFrameId { get; }
        internal CreatureId Mover { get; }
        internal CreatureId Occupant { get; }
        internal GridPosition ReservedPosition { get; }
        internal MovementBudgetId BudgetId { get; }
        internal MovementPath Path { get; }
        internal MovementPermissionPurpose Purpose { get; }

        internal sealed class Authority
        {
            private readonly HashSet<MovementPermission> issued = new HashSet<MovementPermission>();
            private readonly HashSet<MovementPermission> consumed =
                new HashSet<MovementPermission>();

            public MovementPermission Issue(
                OpId rootId,
                OpId parentFrameId,
                CreatureId mover,
                CreatureId occupant,
                GridPosition reservedPosition,
                MovementBudgetId budgetId,
                MovementPath path,
                MovementPermissionPurpose purpose
            )
            {
                MovementPermission permission = new MovementPermission(
                    rootId,
                    parentFrameId,
                    mover,
                    occupant,
                    reservedPosition,
                    budgetId,
                    path,
                    purpose
                );
                issued.Add(permission);
                return permission;
            }

            public MovementPermissionFailureKind Validate(
                MovementPermission permission,
                OpFrame<MovePathOp> frame
            )
            {
                if (permission == null)
                    return MovementPermissionFailureKind.NotIssued;
                if (permission.IsNone)
                {
                    return frame.Op.PermissionPurpose == MovementPermissionPurpose.Ordinary
                        ? MovementPermissionFailureKind.None
                        : MovementPermissionFailureKind.PurposeMismatch;
                }
                if (!issued.Contains(permission))
                    return MovementPermissionFailureKind.NotIssued;
                if (consumed.Contains(permission))
                    return MovementPermissionFailureKind.Reused;
                if (permission.RootId != frame.RootId)
                    return MovementPermissionFailureKind.RootMismatch;
                if (!frame.ParentId.HasValue || permission.ParentFrameId != frame.ParentId.Value)
                    return MovementPermissionFailureKind.ParentFrameMismatch;
                if (permission.Mover != frame.Op.Mover)
                    return MovementPermissionFailureKind.MoverMismatch;
                if (permission.BudgetId != frame.Op.BudgetId)
                    return MovementPermissionFailureKind.BudgetMismatch;
                if (!permission.Path.Equals(frame.Op.Path))
                    return MovementPermissionFailureKind.PathMismatch;
                if (permission.Purpose != frame.Op.PermissionPurpose)
                    return MovementPermissionFailureKind.PurposeMismatch;
                if (permission.Purpose == MovementPermissionPurpose.Ordinary)
                    return MovementPermissionFailureKind.PurposeMismatch;
                return MovementPermissionFailureKind.None;
            }

            public void Consume(MovementPermission permission)
            {
                if (permission == null || permission.IsNone || !issued.Contains(permission))
                    throw new InvalidOperationException(
                        "Only an issued permission can be consumed."
                    );
                if (!consumed.Add(permission))
                    throw new InvalidOperationException("A movement permission cannot be reused.");
            }
        }
    }

    /// <summary>Identifies why an occupied-space permission did not authorize a move.</summary>
    public enum MovementPermissionFailureKind
    {
        /// <summary>No permission failure occurred.</summary>
        None,

        /// <summary>An occupied crossing required an authorization.</summary>
        Required,

        /// <summary>The value was not issued by this movement runtime.</summary>
        NotIssued,

        /// <summary>The permission already authorized a committed crossing.</summary>
        Reused,

        /// <summary>The permission belongs to another root resolution.</summary>
        RootMismatch,

        /// <summary>The permission belongs to another parent frame.</summary>
        ParentFrameMismatch,

        /// <summary>The permission belongs to another mover.</summary>
        MoverMismatch,

        /// <summary>The permission belongs to another occupant.</summary>
        OccupantMismatch,

        /// <summary>The permission belongs to another movement budget.</summary>
        BudgetMismatch,

        /// <summary>The permission reserves another exact path.</summary>
        PathMismatch,

        /// <summary>The permission was issued for another rule use.</summary>
        PurposeMismatch,

        /// <summary>The requested reservation did not cross and exit the occupant's cell.</summary>
        InvalidReservation,
    }

    /// <summary>Identifies the typed reason a path or relocation could not commit normally.</summary>
    public enum MovementFailureKind
    {
        /// <summary>No failure occurred.</summary>
        None,

        /// <summary>The mover has no authoritative position.</summary>
        MissingPosition,

        /// <summary>The mover has no authoritative movement budget.</summary>
        MissingBudget,

        /// <summary>The supplied budget is not the mover's current budget.</summary>
        BudgetMismatch,

        /// <summary>The originating action identity is missing, unrelated, or belongs to another actor.</summary>
        InvalidActionProvenance,

        /// <summary>The path contains no destination step.</summary>
        EmptyPath,

        /// <summary>The current position no longer matches the requested origin.</summary>
        StaleOrigin,

        /// <summary>An intermediate cell lies outside the topology.</summary>
        OutOfBounds,

        /// <summary>The final cell lies outside the topology.</summary>
        DestinationOutOfBounds,

        /// <summary>Two consecutive cells do not form one ground-movement step.</summary>
        NonContiguous,

        /// <summary>A diagonal attempts to pass between two unavailable side cells.</summary>
        CornerBlocked,

        /// <summary>An intermediate cell is blocked by topology.</summary>
        Blocked,

        /// <summary>The final cell is blocked by topology.</summary>
        DestinationBlocked,

        /// <summary>An occupied cell lacks matching traversal authority.</summary>
        Occupied,

        /// <summary>The requested final cell is occupied and cannot be reserved as a destination.</summary>
        DestinationOccupied,

        /// <summary>The full preflight path exceeds the available movement distance.</summary>
        InsufficientMovement,

        /// <summary>The movement timing operation was interrupted before this step.</summary>
        TriggerInterrupted,

        /// <summary>The movement timing operation became invalid before this step.</summary>
        TriggerInvalid,

        /// <summary>The movement timing operation was cancelled before this step.</summary>
        TriggerCancelled,

        /// <summary>The permission did not match its engine-issued scope.</summary>
        PermissionRejected,
    }

    /// <summary>
    /// Carries a typed movement failure with its one-based step and relevant coordinate.
    /// </summary>
    public readonly struct MovementFailure : IEquatable<MovementFailure>
    {
        internal MovementFailure(
            MovementFailureKind kind,
            int stepNumber,
            GridPosition position,
            MovementPermissionFailureKind permissionFailure = MovementPermissionFailureKind.None
        )
        {
            if (kind == MovementFailureKind.None)
                throw new ArgumentException("A movement failure must have a reason.", nameof(kind));
            if (stepNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(stepNumber));
            if (
                kind == MovementFailureKind.PermissionRejected
                && permissionFailure == MovementPermissionFailureKind.None
            )
                throw new ArgumentException("A permission rejection requires a scoped reason.");

            Kind = kind;
            StepNumber = stepNumber;
            Position = position;
            PermissionFailure = permissionFailure;
        }

        /// <summary>Gets the failure category.</summary>
        public MovementFailureKind Kind { get; }

        /// <summary>Gets the one-based failing step, or zero for a path-wide prerequisite.</summary>
        public int StepNumber { get; }

        /// <summary>Gets the requested or observed coordinate associated with the failure.</summary>
        public GridPosition Position { get; }

        /// <summary>Gets the scoped permission reason when <see cref="Kind"/> is a rejection.</summary>
        public MovementPermissionFailureKind PermissionFailure { get; }

        /// <inheritdoc/>
        public bool Equals(MovementFailure other) =>
            Kind == other.Kind
            && StepNumber == other.StepNumber
            && Position == other.Position
            && PermissionFailure == other.PermissionFailure;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MovementFailure other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Kind, StepNumber, Position, PermissionFailure);
    }

    /// <summary>Describes whether a nested path reached its requested destination.</summary>
    public enum MovePathStatus
    {
        /// <summary>Every requested step committed.</summary>
        ReachedDestination,

        /// <summary>Movement stopped before every requested step committed.</summary>
        Stopped,
    }

    /// <summary>
    /// Reports the committed prefix of one nested movement path.
    /// </summary>
    public sealed class MovePathOutcome
    {
        internal MovePathOutcome(
            MovePathStatus status,
            GridPosition finalPosition,
            int committedSteps,
            GridDistance distanceSpent,
            MovementFailure failure
        )
        {
            Status = status;
            FinalPosition = finalPosition;
            CommittedSteps = committedSteps;
            DistanceSpent = distanceSpent;
            Failure = failure;
        }

        /// <summary>Gets whether the complete path committed.</summary>
        public MovePathStatus Status { get; }

        /// <summary>Gets the final authoritative position after the committed prefix.</summary>
        public GridPosition FinalPosition { get; }

        /// <summary>Gets the number of committed steps.</summary>
        public int CommittedSteps { get; }

        /// <summary>Gets the distance spent by committed steps.</summary>
        public GridDistance DistanceSpent { get; }

        /// <summary>Gets the typed stop reason, or the default value after full completion.</summary>
        public MovementFailure Failure { get; }

        /// <summary>Gets whether every requested step committed.</summary>
        public bool ReachedDestination => Status == MovePathStatus.ReachedDestination;
    }

    /// <summary>Identifies one stable departure timing point within a path operation.</summary>
    public readonly struct MovementTriggerId : IEquatable<MovementTriggerId>
    {
        internal MovementTriggerId(OpId movePathOpId, int stepNumber)
        {
            if (movePathOpId.IsEmpty)
                throw new ArgumentException(
                    "A path operation ID is required.",
                    nameof(movePathOpId)
                );
            if (stepNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepNumber));
            MovePathOpId = movePathOpId;
            StepNumber = stepNumber;
        }

        /// <summary>Gets the movement workflow frame that owns this trigger.</summary>
        public OpId MovePathOpId { get; }

        /// <summary>Gets the one-based departure sequence within the path.</summary>
        public int StepNumber { get; }

        /// <summary>Gets whether this value is the unallocated default identity.</summary>
        public bool IsEmpty => MovePathOpId.IsEmpty || StepNumber <= 0;

        /// <inheritdoc/>
        public bool Equals(MovementTriggerId other) =>
            MovePathOpId == other.MovePathOpId && StepNumber == other.StepNumber;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MovementTriggerId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(MovePathOpId, StepNumber);
    }

    /// <summary>Describes the type of movement timing point being exposed to middleware.</summary>
    public enum MovementTriggerKind
    {
        /// <summary>The mover is about to leave its current square for a committed path step.</summary>
        Departure,
    }

    /// <summary>Describes whether departure middleware permits the associated step to continue.</summary>
    public enum MovementTriggerDecision
    {
        /// <summary>The step may proceed to its authoritative commit.</summary>
        Continue,

        /// <summary>The path stops before the associated step commits.</summary>
        Interrupted,
    }

    /// <summary>Returns the typed decision from a movement timing point.</summary>
    public readonly struct MovementTriggerOutcome : IEquatable<MovementTriggerOutcome>
    {
        private MovementTriggerOutcome(MovementTriggerDecision decision) => Decision = decision;

        /// <summary>Gets the normal continue outcome.</summary>
        public static MovementTriggerOutcome Continue { get; } =
            new MovementTriggerOutcome(MovementTriggerDecision.Continue);

        /// <summary>Gets the interrupted outcome.</summary>
        public static MovementTriggerOutcome Interrupted { get; } =
            new MovementTriggerOutcome(MovementTriggerDecision.Interrupted);

        /// <summary>Gets the timing decision.</summary>
        public MovementTriggerDecision Decision { get; }

        /// <inheritdoc/>
        public bool Equals(MovementTriggerOutcome other) => Decision == other.Decision;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is MovementTriggerOutcome other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (int)Decision;
    }

    /// <summary>Names the mechanical reason for a relocation that does not spend movement.</summary>
    public readonly struct RelocationKind : IEquatable<RelocationKind>
    {
        private RelocationKind(string slug) => Slug = StableId.Require(slug, nameof(slug));

        /// <summary>Gets the canonical relocation-reason slug.</summary>
        public string Slug { get; }

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        /// <summary>Creates an open relocation reason from a stable rule slug.</summary>
        public static RelocationKind FromSlug(string value) =>
            new RelocationKind(Pf2eSlug.FromName(value));

        /// <inheritdoc/>
        public bool Equals(RelocationKind other) =>
            string.Equals(Slug, other.Slug, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RelocationKind other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Slug ?? string.Empty;
    }

    /// <summary>Reports a relocation commit or its typed rejection.</summary>
    public sealed class RelocationOutcome
    {
        internal RelocationOutcome(
            bool relocated,
            GridPosition finalPosition,
            MovementFailure failure
        )
        {
            Relocated = relocated;
            FinalPosition = finalPosition;
            Failure = failure;
        }

        /// <summary>Gets whether the authoritative position changed.</summary>
        public bool Relocated { get; }

        /// <summary>Gets the final authoritative position.</summary>
        public GridPosition FinalPosition { get; }

        /// <summary>Gets the typed rejection, or the default value after a successful commit.</summary>
        public MovementFailure Failure { get; }
    }

    internal readonly struct OccupiedTraversalAllowance
    {
        public static OccupiedTraversalAllowance None { get; } = default;

        private OccupiedTraversalAllowance(
            CreatureId occupant,
            bool hasReservedPosition,
            GridPosition reservedPosition
        )
        {
            HasOccupant = true;
            Occupant = occupant;
            HasReservedPosition = hasReservedPosition;
            ReservedPosition = reservedPosition;
        }

        public bool HasOccupant { get; }
        public CreatureId Occupant { get; }
        public bool HasReservedPosition { get; }
        public GridPosition ReservedPosition { get; }

        public static OccupiedTraversalAllowance ForAnyPosition(CreatureId occupant) =>
            Create(occupant, false, default);

        public static OccupiedTraversalAllowance ForReservedPosition(
            CreatureId occupant,
            GridPosition reservedPosition
        ) => Create(occupant, true, reservedPosition);

        private static OccupiedTraversalAllowance Create(
            CreatureId occupant,
            bool hasReservedPosition,
            GridPosition reservedPosition
        )
        {
            if (occupant.IsEmpty)
                throw new ArgumentException(
                    "An authorized occupant is required.",
                    nameof(occupant)
                );
            return new OccupiedTraversalAllowance(occupant, hasReservedPosition, reservedPosition);
        }
    }
}
