using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Identifies one persistent door independently of its Unity representation.</summary>
    public readonly struct DoorId : IEquatable<DoorId>
    {
        /// <summary>Creates a stable door identity.</summary>
        /// <param name="value">The non-empty map-document identifier.</param>
        public DoorId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A door ID is required.", nameof(value));
            Value = value;
        }

        /// <summary>Gets the stable map-document identifier.</summary>
        public string Value { get; }

        /// <summary>Gets whether this value contains no usable identity.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

        /// <inheritdoc/>
        public bool Equals(DoorId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DoorId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>Compares two door identities by ordinal value.</summary>
        public static bool operator ==(DoorId left, DoorId right) => left.Equals(right);

        /// <summary>Compares two door identities by ordinal value.</summary>
        public static bool operator !=(DoorId left, DoorId right) => !left.Equals(right);
    }

    /// <summary>Stores one door's durable identity, map cell, and authoritative open state.</summary>
    public sealed class DoorState : IEquatable<DoorState>
    {
        /// <summary>Creates complete persistent state for one ordinary unlocked door.</summary>
        /// <param name="id">The door's stable map-document identity.</param>
        /// <param name="cell">The exact grid cell occupied by the door.</param>
        /// <param name="isOpen">Whether the door has already been opened.</param>
        public DoorState(DoorId id, GridPosition cell, bool isOpen)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A door ID is required.", nameof(id));
            Id = id;
            Cell = cell;
            IsOpen = isOpen;
        }

        /// <summary>Gets the stable map-document identity.</summary>
        public DoorId Id { get; }

        /// <summary>Gets the exact grid cell occupied by the door.</summary>
        public GridPosition Cell { get; }

        /// <summary>Gets whether the door is authoritatively open.</summary>
        public bool IsOpen { get; }

        internal DoorState Open() => IsOpen ? this : new DoorState(Id, Cell, true);

        /// <inheritdoc/>
        public bool Equals(DoorState other) =>
            other != null && Id == other.Id && Cell == other.Cell && IsOpen == other.IsOpen;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DoorState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Id, Cell, IsOpen);
    }

    /// <summary>
    /// Immutable, feature-owned aggregate for every authoritative door on one generated floor.
    /// </summary>
    internal sealed class DoorWorldState
    {
        private readonly Dictionary<DoorId, DoorState> doors;

        internal DoorWorldState(IEnumerable<DoorState> doors)
        {
            if (doors == null)
                throw new ArgumentNullException(nameof(doors));

            this.doors = new Dictionary<DoorId, DoorState>();
            foreach (DoorState door in doors)
            {
                if (door == null)
                    throw new ArgumentException("Door state cannot contain null.", nameof(doors));
                if (!this.doors.TryAdd(door.Id, door))
                    throw new ArgumentException("Door identities must be unique.", nameof(doors));
            }
        }

        private DoorWorldState(Dictionary<DoorId, DoorState> doors) => this.doors = doors;

        internal IEnumerable<DoorState> Doors => doors.Values;

        internal bool TryGet(DoorId id, out DoorState door) => doors.TryGetValue(id, out door);

        internal DoorWorldState WithDoor(DoorState door)
        {
            Dictionary<DoorId, DoorState> updated = new(doors) { [door.Id] = door };
            return new DoorWorldState(updated);
        }
    }

    /// <summary>Identifies why an attempt to open an ordinary door was rejected.</summary>
    public enum DoorOpenRejection
    {
        /// <summary>The interaction is legal.</summary>
        None,

        /// <summary>The requested stable door identity is not part of the floor.</summary>
        DoorDoesNotExist,

        /// <summary>The actor is not a player character.</summary>
        ActorIsNotPlayerCharacter,

        /// <summary>The actor is not alive.</summary>
        ActorIsNotAlive,

        /// <summary>The door is already open.</summary>
        DoorAlreadyOpen,

        /// <summary>The actor is not in a cardinally adjacent cell.</summary>
        ActorIsNotAdjacent,

        /// <summary>The actor does not own the exact active encounter turn.</summary>
        ActorDoesNotOwnTurn,
    }

    /// <summary>Captures immutable actor facts for one attempt to open a door.</summary>
    public readonly struct DoorOpenRequest
    {
        /// <summary>Creates a rules request without retaining mutable host objects.</summary>
        /// <param name="door">The target door.</param>
        /// <param name="actorCell">The actor's authoritative or captured grid cell.</param>
        /// <param name="actorIsPlayerCharacter">Whether the actor belongs to the player party.</param>
        /// <param name="actorIsAlive">Whether the actor is alive.</param>
        public DoorOpenRequest(
            DoorId door,
            GridPosition actorCell,
            bool actorIsPlayerCharacter,
            bool actorIsAlive
        )
        {
            if (door.IsEmpty)
                throw new ArgumentException("A door ID is required.", nameof(door));
            Door = door;
            ActorCell = actorCell;
            ActorIsPlayerCharacter = actorIsPlayerCharacter;
            ActorIsAlive = actorIsAlive;
        }

        /// <summary>Gets the target door.</summary>
        public DoorId Door { get; }

        /// <summary>Gets the actor's grid cell.</summary>
        public GridPosition ActorCell { get; }

        /// <summary>Gets whether the actor belongs to the player party.</summary>
        public bool ActorIsPlayerCharacter { get; }

        /// <summary>Gets whether the actor is alive.</summary>
        public bool ActorIsAlive { get; }
    }

    /// <summary>Reports pure legality for one door-opening request.</summary>
    public readonly struct DoorOpenValidation
    {
        internal DoorOpenValidation(DoorOpenRejection rejection) => Rejection = rejection;

        /// <summary>Gets whether the request may commit.</summary>
        public bool IsValid => Rejection == DoorOpenRejection.None;

        /// <summary>Gets the rejection reason, or <see cref="DoorOpenRejection.None"/>.</summary>
        public DoorOpenRejection Rejection { get; }

        /// <summary>Gets a stable diagnostic suitable for an invalid operation result.</summary>
        public string Reason =>
            Rejection switch
            {
                DoorOpenRejection.None => string.Empty,
                DoorOpenRejection.DoorDoesNotExist => "The selected door does not exist.",
                DoorOpenRejection.ActorIsNotPlayerCharacter =>
                    "Only a player character can open this door.",
                DoorOpenRejection.ActorIsNotAlive => "A defeated actor cannot open a door.",
                DoorOpenRejection.DoorAlreadyOpen => "The selected door is already open.",
                DoorOpenRejection.ActorIsNotAdjacent =>
                    "The actor must be cardinally adjacent to the door.",
                DoorOpenRejection.ActorDoesNotOwnTurn =>
                    "The actor does not own the active encounter turn.",
                _ => throw new InvalidOperationException($"Unknown door rejection {Rejection}."),
            };
    }

    /// <summary>Reports the stable door identity committed by an open operation.</summary>
    public readonly struct DoorOpenedOutcome : IEquatable<DoorOpenedOutcome>
    {
        /// <summary>Creates a committed door-opening result.</summary>
        /// <param name="door">The opened door.</param>
        public DoorOpenedOutcome(DoorId door) => Door = door;

        /// <summary>Gets the door that opened.</summary>
        public DoorId Door { get; }

        /// <inheritdoc/>
        public bool Equals(DoorOpenedOutcome other) => Door == other.Door;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DoorOpenedOutcome other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Door.GetHashCode();
    }

    /// <summary>Requests the sole authoritative closed-to-open door transition.</summary>
    public sealed class OpenDoorOp : IRuleOp<DoorOpenedOutcome>
    {
        /// <summary>Creates an ordinary door-opening request.</summary>
        /// <param name="request">The immutable door and actor facts to validate at commit.</param>
        public OpenDoorOp(DoorOpenRequest request) => Request = request;

        /// <summary>Gets the immutable interaction request.</summary>
        public DoorOpenRequest Request { get; }
    }

    internal sealed class CommitDoorOpenOp : IRuleOp<DoorOpenedOutcome>
    {
        internal CommitDoorOpenOp(DoorOpenRequest request) => Request = request;

        internal DoorOpenRequest Request { get; }
    }

    /// <summary>Reports one committed closed-to-open door transition.</summary>
    public sealed class DoorOpenedFact : RuleFact
    {
        /// <summary>Creates a committed door-state fact.</summary>
        /// <param name="door">The opened door.</param>
        /// <param name="cell">The door's stable grid cell.</param>
        public DoorOpenedFact(DoorId door, GridPosition cell)
        {
            Door = door;
            Cell = cell;
        }

        /// <summary>Gets the opened door.</summary>
        public DoorId Door { get; }

        /// <summary>Gets the stable cell whose topology changed.</summary>
        public GridPosition Cell { get; }
    }

    /// <summary>Defines Interact for opening an ordinary door.</summary>
    public sealed class DoorInteractActionDefinition : IActionCatalog
    {
        private static readonly ActionProfile Profile = ActionProfile.OneAction(
            new[] { Trait.FromSlug("manipulate") }
        );

        /// <summary>Gets the stable action identity for Open Door as Interact.</summary>
        public static ActionDefinitionId DefinitionId { get; } =
            new ActionDefinitionId("interact-open-door");

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId == DefinitionId)
                return Profile;
            throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
        }
    }

    /// <summary>Requests Open Door through the mandatory encounter action lifecycle.</summary>
    public sealed class DoorInteractActionOp : ActionOp<DoorOpenedOutcome>
    {
        /// <summary>Creates a one-action Interact request for a stable door.</summary>
        /// <param name="actor">The encounter creature attempting the interaction.</param>
        /// <param name="door">The stable target door.</param>
        public DoorInteractActionOp(CreatureId actor, DoorId door)
            : base(actor, DoorInteractActionDefinition.DefinitionId)
        {
            if (door.IsEmpty)
                throw new ArgumentException("A door ID is required.", nameof(door));
            Door = door;
        }

        /// <summary>Gets the stable target door.</summary>
        public DoorId Door { get; }
    }

    /// <summary>Owns the durable door-state dispatcher for one generated floor.</summary>
    public sealed class DoorWorldRuntime
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("door-world");
        private readonly object gate = new();
        private readonly RuleDispatcher dispatcher;

        /// <summary>Creates one authoritative store from the complete floor-door sequence.</summary>
        /// <param name="doors">All doors on the floor, with stable identity and restored state.</param>
        /// <param name="rollService">The required deterministic or production roll source.</param>
        public DoorWorldRuntime(IEnumerable<DoorState> doors, IRollService rollService)
        {
            if (doors == null)
                throw new ArgumentNullException(nameof(doors));
            DoorWorldState initialState = new(doors);
            RulesStateSeed seed = new RulesStateSeed().SeedState(DoorRules.StateKey, initialState);
            dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                rollService ?? throw new ArgumentNullException(nameof(rollService))
            )
                .RegisterHandler<OpenDoorOp, DoorOpenedOutcome>(new OpenDoorHandler())
                .RegisterReducer<CommitDoorOpenOp, DoorOpenedOutcome>(new OpenDoorReducer(), Source)
                .Build();
        }

        /// <summary>Gets the latest authoritative floor-door snapshot.</summary>
        public RulesSnapshot Snapshot => dispatcher.Snapshot;

        /// <summary>Gets whether one stable door is authoritatively open.</summary>
        /// <param name="door">The stable floor-door identity.</param>
        /// <returns><see langword="true"/> when the registered door is open.</returns>
        /// <exception cref="KeyNotFoundException">The door is not part of this floor.</exception>
        public bool IsOpen(DoorId door) => DoorRules.GetDoor(Snapshot, door).IsOpen;

        /// <summary>Evaluates an interaction without charging or mutating anything.</summary>
        /// <param name="request">The immutable request to inspect.</param>
        /// <returns>Explicit legality and rejection information.</returns>
        public DoorOpenValidation Validate(DoorOpenRequest request) =>
            DoorRules.Validate(Snapshot, request);

        /// <summary>Attempts the sole authoritative closed-to-open transition.</summary>
        /// <param name="request">The immutable request to validate and commit.</param>
        /// <returns>A resolved result on commit, or an invalid result with no mutation.</returns>
        public OpResult<DoorOpenedOutcome> Open(DoorOpenRequest request)
        {
            lock (gate)
            {
                DoorOpenValidation validation = Validate(request);
                if (!validation.IsValid)
                    return OpResult<DoorOpenedOutcome>.Invalid(validation.Reason);

                ValueTask<OpResult<DoorOpenedOutcome>> pending = dispatcher.Dispatch(
                    new OpenDoorOp(request)
                );
                if (!pending.IsCompleted)
                    throw new InvalidOperationException(
                        "Door world requests cannot contain asynchronous rules callbacks."
                    );
                return pending.GetAwaiter().GetResult();
            }
        }

        /// <summary>Registers a synchronous projection of committed door changes.</summary>
        /// <param name="observer">The projection observer owned by the floor runtime.</param>
        /// <returns>A token that removes the exact observer registration.</returns>
        public IDisposable RegisterObserver(IFactObserver<DoorOpenedFact> observer) =>
            dispatcher.RegisterFactObserver(
                observer ?? throw new ArgumentNullException(nameof(observer))
            );

        /// <summary>Captures open stable IDs in deterministic ordinal order.</summary>
        /// <returns>A detached stable-ID sequence for persistence.</returns>
        public IReadOnlyList<string> CaptureOpenDoorIds() =>
            Array.AsReadOnly(
                DoorRules
                    .GetWorldState(Snapshot)
                    .Doors.Where(door => door.IsOpen)
                    .Select(door => door.Id.Value)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
            );
    }

    /// <summary>Owns pure selectors and dispatcher composition for the Open Door feature.</summary>
    public static class DoorRules
    {
        internal static RuleStateKey<DoorWorldState> StateKey { get; } = new("door-world");

        internal static DoorWorldState GetWorldState(RulesSnapshot snapshot) =>
            snapshot.GetState(StateKey);

        internal static DoorState GetDoor(RulesSnapshot snapshot, DoorId door)
        {
            if (!TryGetDoor(snapshot, door, out DoorState state))
                throw new KeyNotFoundException($"Unknown door '{door}'.");
            return state;
        }

        /// <summary>Tries to read one door from the feature-owned state in an exact snapshot.</summary>
        /// <param name="snapshot">The authoritative snapshot to query.</param>
        /// <param name="door">The stable door identity.</param>
        /// <param name="state">The immutable door state when found; otherwise, the default.</param>
        /// <returns><see langword="true"/> when the floor state contains the door.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
        public static bool TryGetDoor(RulesSnapshot snapshot, DoorId door, out DoorState state)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            state = default;
            return snapshot.TryGetState(StateKey, out DoorWorldState world)
                && world.TryGet(door, out state);
        }

        /// <summary>Evaluates ordinary door legality from authoritative state.</summary>
        /// <param name="snapshot">The authoritative door snapshot.</param>
        /// <param name="request">The immutable door and actor facts.</param>
        /// <returns>Explicit legality without mutation.</returns>
        public static DoorOpenValidation Validate(RulesSnapshot snapshot, DoorOpenRequest request)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!TryGetDoor(snapshot, request.Door, out DoorState door))
                return new DoorOpenValidation(DoorOpenRejection.DoorDoesNotExist);
            return Validate(door, request);
        }

        internal static DoorOpenValidation Validate(DoorState door, DoorOpenRequest request)
        {
            if (!request.ActorIsPlayerCharacter)
                return new DoorOpenValidation(DoorOpenRejection.ActorIsNotPlayerCharacter);
            if (!request.ActorIsAlive)
                return new DoorOpenValidation(DoorOpenRejection.ActorIsNotAlive);
            if (door.IsOpen)
                return new DoorOpenValidation(DoorOpenRejection.DoorAlreadyOpen);
            long xDistance = Math.Abs((long)request.ActorCell.X - door.Cell.X);
            long zDistance = Math.Abs((long)request.ActorCell.Z - door.Cell.Z);
            if (request.ActorCell.Y != door.Cell.Y || xDistance + zDistance != 1)
                return new DoorOpenValidation(DoorOpenRejection.ActorIsNotAdjacent);
            return new DoorOpenValidation(DoorOpenRejection.None);
        }

        /// <summary>Adds encounter Interact validation and handling against one floor authority.</summary>
        /// <param name="builder">The encounter dispatcher builder.</param>
        /// <param name="doorWorld">The exact floor-lived authoritative door runtime.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseDoorInteractRules(
            this RuleDispatcherBuilder builder,
            DoorWorldRuntime doorWorld
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (doorWorld == null)
                throw new ArgumentNullException(nameof(doorWorld));
            return builder
                .RegisterActionValidator<DoorInteractActionOp>(
                    new DoorInteractActionValidator(doorWorld)
                )
                .RegisterHandler<DoorInteractActionOp, DoorOpenedOutcome>(
                    new DoorInteractActionHandler(doorWorld)
                );
        }

        internal static bool TryCreateEncounterRequest(
            RulesSnapshot snapshot,
            CreatureId actor,
            DoorId door,
            out DoorOpenRequest request,
            out DoorOpenValidation rejection
        )
        {
            request = default;
            rejection = default;
            EncounterState[] encounters = snapshot
                .Encounters.Select(pair => pair.Value)
                .Where(encounter =>
                    encounter.Phase == EncounterPhase.Active
                    && encounter.Roster.Any(entry => entry.Creature == actor)
                )
                .ToArray();
            if (
                encounters.Length != 1
                || !encounters[0].CurrentTurn.HasValue
                || encounters[0].CurrentTurn.Value.Actor != actor
            )
            {
                rejection = new DoorOpenValidation(DoorOpenRejection.ActorDoesNotOwnTurn);
                return false;
            }
            bool isPlayer =
                snapshot.Creatures.TryGet(actor, out CreatureState creature)
                && creature.Player == encounters[0].ProtagonistTeam;
            bool isAlive = snapshot.Health.TryGet(actor, out HealthState health) && health.IsLiving;
            if (!snapshot.Positions.TryGet(actor, out GridPosition actorCell))
            {
                rejection = new DoorOpenValidation(DoorOpenRejection.ActorIsNotAdjacent);
                return false;
            }
            request = new DoorOpenRequest(door, actorCell, isPlayer, isAlive);
            return true;
        }
    }

    internal sealed class OpenDoorHandler : IOpHandler<OpenDoorOp, DoorOpenedOutcome>
    {
        public async ValueTask<DoorOpenedOutcome> Handle(
            OpFrame<OpenDoorOp> frame,
            OpHandlerContext context
        )
        {
            OpResult<DoorOpenedOutcome> result = await context.Dispatch(
                new CommitDoorOpenOp(frame.Op.Request)
            );
            if (result is ResolvedOpResult<DoorOpenedOutcome> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<DoorOpenedOutcome> invalid)
                throw new InvalidOperationException(
                    "Validated door commit was rejected: " + invalid.Reason
                );
            throw new InvalidOperationException("The validated door commit did not resolve.");
        }
    }

    internal sealed class OpenDoorReducer : IOpReducer<CommitDoorOpenOp, DoorOpenedOutcome>
    {
        public ReductionResult<DoorOpenedOutcome> Reduce(
            ReductionContext<CommitDoorOpenOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !state.TryGetState(DoorRules.StateKey, out DoorWorldState world)
                || !world.TryGet(context.Op.Request.Door, out DoorState current)
            )
            {
                return ReductionResult<DoorOpenedOutcome>.Reject(
                    new DoorOpenValidation(DoorOpenRejection.DoorDoesNotExist).Reason
                );
            }

            DoorOpenValidation validation = DoorRules.Validate(current, context.Op.Request);
            if (!validation.IsValid)
                return ReductionResult<DoorOpenedOutcome>.Reject(validation.Reason);

            DoorState opened = current.Open();
            state.SetState(DoorRules.StateKey, world.WithDoor(opened));
            facts.Stage(new DoorOpenedFact(opened.Id, opened.Cell));
            return ReductionResult<DoorOpenedOutcome>.Accept(new DoorOpenedOutcome(opened.Id));
        }
    }

    internal sealed class DoorInteractActionValidator : IActionValidator<DoorInteractActionOp>
    {
        private readonly DoorWorldRuntime doorWorld;

        internal DoorInteractActionValidator(DoorWorldRuntime doorWorld) =>
            this.doorWorld = doorWorld;

        public ActionValidationResult Validate(
            OpFrame<DoorInteractActionOp> frame,
            RulesSnapshot snapshot
        )
        {
            if (
                !DoorRules.TryCreateEncounterRequest(
                    snapshot,
                    frame.Op.Actor,
                    frame.Op.Door,
                    out DoorOpenRequest request,
                    out DoorOpenValidation rejection
                )
            )
                return ActionValidationResult.Invalid(rejection.Reason);
            DoorOpenValidation validation = doorWorld.Validate(request);
            return validation.IsValid
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid(validation.Reason);
        }
    }

    internal sealed class DoorInteractActionHandler
        : IOpHandler<DoorInteractActionOp, DoorOpenedOutcome>
    {
        private readonly DoorWorldRuntime doorWorld;

        internal DoorInteractActionHandler(DoorWorldRuntime doorWorld) =>
            this.doorWorld = doorWorld;

        public ValueTask<DoorOpenedOutcome> Handle(
            OpFrame<DoorInteractActionOp> frame,
            OpHandlerContext context
        )
        {
            if (
                !DoorRules.TryCreateEncounterRequest(
                    context.Snapshot,
                    frame.Op.Actor,
                    frame.Op.Door,
                    out DoorOpenRequest request,
                    out DoorOpenValidation rejection
                )
            )
                throw new InvalidOperationException(rejection.Reason);

            OpResult<DoorOpenedOutcome> result = doorWorld.Open(request);
            if (result is ResolvedOpResult<DoorOpenedOutcome> resolved)
                return new ValueTask<DoorOpenedOutcome>(resolved.Value);
            if (result is InvalidOpResult<DoorOpenedOutcome> invalid)
                throw new InvalidOperationException(
                    "Door legality changed after the Interact lifecycle began: " + invalid.Reason
                );
            throw new InvalidOperationException("The door interaction did not resolve.");
        }
    }
}
