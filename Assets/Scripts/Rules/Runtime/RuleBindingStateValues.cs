using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Marks an immutable state value owned by one active-effect instance.
    /// </summary>
    /// <remarks>
    /// Implementations must be immutable because committed snapshots retain and share the value.
    /// The first committed value establishes the instance's exact state type. Later optimistic
    /// replacements must use that same concrete type.
    /// </remarks>
    public interface IEffectState { }

    /// <summary>
    /// Identifies how active-effect duration metadata is expressed before encounter timing resolves it.
    /// </summary>
    public enum EffectDurationKind
    {
        /// <summary>The effect remains until an explicit rules operation expires or removes it.</summary>
        Indefinite,

        /// <summary>The effect lasts for the current encounter.</summary>
        Encounter,

        /// <summary>The effect lasts for a positive number of encounter rounds.</summary>
        Rounds,

        /// <summary>The effect lasts for a positive number of minutes.</summary>
        Minutes,
    }

    /// <summary>
    /// Stores definition-declared duration metadata without owning encounter-clock behavior.
    /// </summary>
    /// <remarks>
    /// This value remains on the authoritative <see cref="ActiveEffectInstance"/> and deliberately
    /// does not track remaining time. The active-effect timing slice stores only encounter ownership
    /// and remaining boundaries; reducers derive duration semantics and source identity from the
    /// effect, then derive binding identity and ordering from its associated binding.
    /// </remarks>
    public readonly struct EffectDuration : IEquatable<EffectDuration>
    {
        private EffectDuration(EffectDurationKind kind, int amount)
        {
            Kind = kind;
            Amount = amount;
        }

        /// <summary>Gets the duration category.</summary>
        public EffectDurationKind Kind { get; }

        /// <summary>
        /// Gets the positive round or minute count, or zero for category-only durations.
        /// </summary>
        public int Amount { get; }

        /// <summary>Gets a duration that requires an explicit rules decision to end.</summary>
        public static EffectDuration Indefinite => default;

        /// <summary>Gets a duration that ends with the current encounter.</summary>
        public static EffectDuration Encounter =>
            new EffectDuration(EffectDurationKind.Encounter, 0);

        /// <summary>Gets the common one-minute duration.</summary>
        public static EffectDuration OneMinute => Minutes(1);

        /// <summary>Creates duration metadata for a positive number of encounter rounds.</summary>
        /// <param name="amount">The positive number of rounds.</param>
        /// <returns>The validated duration metadata.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
        public static EffectDuration Rounds(int amount) =>
            Counted(EffectDurationKind.Rounds, amount);

        /// <summary>Creates duration metadata for a positive number of minutes.</summary>
        /// <param name="amount">The positive number of minutes.</param>
        /// <returns>The validated duration metadata.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is not positive.</exception>
        public static EffectDuration Minutes(int amount) =>
            Counted(EffectDurationKind.Minutes, amount);

        /// <inheritdoc/>
        public bool Equals(EffectDuration other) => Kind == other.Kind && Amount == other.Amount;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EffectDuration other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine((int)Kind, Amount);

        /// <summary>Compares two duration metadata values.</summary>
        public static bool operator ==(EffectDuration left, EffectDuration right) =>
            left.Equals(right);

        /// <summary>Compares two duration metadata values.</summary>
        public static bool operator !=(EffectDuration left, EffectDuration right) =>
            !left.Equals(right);

        private static EffectDuration Counted(EffectDurationKind kind, int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    "A duration must be positive."
                );
            return new EffectDuration(kind, amount);
        }
    }

    /// <summary>
    /// Provides the optimistic-concurrency token for one active effect's lifecycle state.
    /// </summary>
    public readonly struct EffectStateVersion : IEquatable<EffectStateVersion>
    {
        /// <summary>Initializes a non-negative effect-state version.</summary>
        /// <param name="value">The non-negative version number.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
        public EffectStateVersion(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        /// <summary>Gets the numeric version.</summary>
        public long Value { get; }

        /// <summary>Gets the initial version assigned at creation.</summary>
        public static EffectStateVersion Initial => default;

        /// <summary>Creates the next optimistic-concurrency version.</summary>
        /// <returns>The next version.</returns>
        /// <exception cref="OverflowException">The current version cannot be incremented.</exception>
        public EffectStateVersion Next() => new EffectStateVersion(checked(Value + 1));

        /// <inheritdoc/>
        public bool Equals(EffectStateVersion other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EffectStateVersion other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Compares two effect-state versions.</summary>
        public static bool operator ==(EffectStateVersion left, EffectStateVersion right) =>
            left.Equals(right);

        /// <summary>Compares two effect-state versions.</summary>
        public static bool operator !=(EffectStateVersion left, EffectStateVersion right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Stores one immutable, typed active-effect instance in authoritative rules state.
    /// </summary>
    public sealed class ActiveEffectInstance : IEquatable<ActiveEffectInstance>
    {
        /// <summary>Gets the stable effect identity.</summary>
        public ActiveEffectId Id { get; }

        /// <summary>Gets the static definition that contributes this effect's rule extensions.</summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>Gets the creature that originated the effect.</summary>
        public CreatureId SourceCreature { get; }

        /// <summary>Gets the stable rules provenance for the effect.</summary>
        public RuleSource Source { get; }

        /// <summary>Gets the definition-declared duration metadata.</summary>
        public EffectDuration Duration { get; }

        /// <summary>Gets the optimistic-concurrency token for the current lifecycle state.</summary>
        public EffectStateVersion EffectStateVersion { get; }

        /// <summary>Gets the immutable instance-owned state value.</summary>
        public IEffectState State { get; }

        /// <summary>Initializes one immutable typed active-effect instance.</summary>
        /// <param name="id">The stable effect identity.</param>
        /// <param name="definitionId">The static definition backing the effect.</param>
        /// <param name="sourceCreature">The creature that originated the effect.</param>
        /// <param name="source">The stable rules provenance.</param>
        /// <param name="duration">The duration metadata interpreted by encounter timing.</param>
        /// <param name="state">The immutable state value whose exact type this instance preserves.</param>
        /// <param name="effectStateVersion">The current optimistic-concurrency token.</param>
        /// <exception cref="ArgumentException">A required ID or source is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
        public ActiveEffectInstance(
            ActiveEffectId id,
            RuleDefinitionId definitionId,
            CreatureId sourceCreature,
            RuleSource source,
            EffectDuration duration,
            IEffectState state,
            EffectStateVersion effectStateVersion = default
        )
        {
            if (id.IsEmpty)
                throw new ArgumentException("An active effect ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A rule definition ID is required.",
                    nameof(definitionId)
                );
            if (sourceCreature.IsEmpty)
                throw new ArgumentException(
                    "A source creature ID is required.",
                    nameof(sourceCreature)
                );
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            Id = id;
            DefinitionId = definitionId;
            SourceCreature = sourceCreature;
            Source = source;
            Duration = duration;
            State = state;
            EffectStateVersion = effectStateVersion;
        }

        /// <summary>
        /// Reads the state as the exact type established by this effect instance.
        /// </summary>
        /// <typeparam name="TState">The expected exact effect-state type.</typeparam>
        /// <returns>The immutable typed state held by this snapshot value.</returns>
        /// <exception cref="InvalidOperationException">The stored state has a different concrete type.</exception>
        public TState GetState<TState>()
            where TState : IEffectState
        {
            if (State.GetType() != typeof(TState))
            {
                throw new InvalidOperationException(
                    $"Effect {Id.Value} contains {State.GetType().Name}, not {typeof(TState).Name}."
                );
            }
            return (TState)State;
        }

        internal ActiveEffectInstance WithState(
            IEffectState state,
            EffectStateVersion effectStateVersion
        ) =>
            new ActiveEffectInstance(
                Id,
                DefinitionId,
                SourceCreature,
                Source,
                Duration,
                state,
                effectStateVersion
            );

        /// <inheritdoc/>
        public bool Equals(ActiveEffectInstance other) =>
            other != null
            && Id == other.Id
            && DefinitionId == other.DefinitionId
            && SourceCreature == other.SourceCreature
            && Source == other.Source
            && Duration == other.Duration
            && EffectStateVersion == other.EffectStateVersion
            && Equals(State, other.State);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is ActiveEffectInstance other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                Id,
                DefinitionId,
                SourceCreature,
                Source,
                Duration,
                EffectStateVersion,
                State
            );
    }

    /// <summary>
    /// Connects one active rule instance in committed state to a static <see cref="RuleDefinition"/>.
    /// </summary>
    /// <remarks>
    /// Bindings carry identity and provenance only. Mutable feature state belongs in an
    /// authoritative rules-state slice, such as the active effect identified by
    /// <see cref="EffectId"/>. Disabling or removing a binding changes runtime participation
    /// without rebuilding the static <see cref="RuleRegistry"/>.
    /// </remarks>
    public sealed class ActiveRuleBinding : IEquatable<ActiveRuleBinding>
    {
        /// <summary>
        /// Gets the stable identity used for deterministic ordering and authorization.
        /// </summary>
        public BindingId Id { get; }

        /// <summary>
        /// Gets the static rule definition that contributes this binding's extensions.
        /// </summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>
        /// Gets the creature that owns or is authorized to use this rule instance.
        /// </summary>
        public CreatureId Owner { get; }

        /// <summary>
        /// Gets the associated active effect, or <see langword="null"/> for rules without one.
        /// </summary>
        public ActiveEffectId? EffectId { get; }

        /// <summary>
        /// Gets the stable feat, spell, condition, item, or system source for this instance.
        /// </summary>
        public RuleSource Source { get; }

        /// <summary>
        /// Gets the monotonic order assigned when the binding was created.
        /// </summary>
        public long CreationOrder { get; }

        /// <summary>
        /// Gets whether the binding can be selected for middleware and Fact listeners when an
        /// operation frame begins. A selected binding is checked against live state again before
        /// its extension is invoked.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        /// Initializes an immutable active rule binding.
        /// </summary>
        /// <param name="id">The unique binding identity.</param>
        /// <param name="definitionId">The static definition providing extension registrations.</param>
        /// <param name="owner">The creature that owns or is authorized by the rule.</param>
        /// <param name="effectId">The associated active effect, when the rule has instance state.</param>
        /// <param name="source">The stable source stamped into later authorized work.</param>
        /// <param name="creationOrder">A non-negative monotonic creation order.</param>
        /// <param name="isEnabled">Whether the binding should currently participate.</param>
        public ActiveRuleBinding(
            BindingId id,
            RuleDefinitionId definitionId,
            CreatureId owner,
            ActiveEffectId? effectId,
            RuleSource source,
            long creationOrder,
            bool isEnabled = true
        )
        {
            if (creationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(creationOrder));
            if (id.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A rule definition ID is required.",
                    nameof(definitionId)
                );
            if (owner.IsEmpty)
                throw new ArgumentException("An owner creature ID is required.", nameof(owner));
            if (effectId.HasValue && effectId.Value.IsEmpty)
                throw new ArgumentException(
                    "An effect ID cannot be empty when supplied.",
                    nameof(effectId)
                );
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            Id = id;
            DefinitionId = definitionId;
            Owner = owner;
            EffectId = effectId;
            Source = source;
            CreationOrder = creationOrder;
            IsEnabled = isEnabled;
        }

        internal ActiveRuleBinding WithEnabled(bool isEnabled) =>
            new ActiveRuleBinding(
                Id,
                DefinitionId,
                Owner,
                EffectId,
                Source,
                CreationOrder,
                isEnabled
            );

        /// <inheritdoc/>
        public bool Equals(ActiveRuleBinding other) =>
            other != null
            && Id == other.Id
            && DefinitionId == other.DefinitionId
            && Owner == other.Owner
            && EffectId == other.EffectId
            && Source == other.Source
            && CreationOrder == other.CreationOrder
            && IsEnabled == other.IsEnabled;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActiveRuleBinding other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Id, DefinitionId, Owner, EffectId, Source, CreationOrder, IsEnabled);
    }

    public readonly struct FrequencyState : IEquatable<FrequencyState>
    {
        /// <summary>Gets the encounter that owns this use marker.</summary>
        public EncounterId Encounter { get; }
        public int Round { get; }
        public int Uses { get; }

        /// <summary>Creates an encounter-qualified immutable frequency marker.</summary>
        public FrequencyState(EncounterId encounter, int round, int uses)
        {
            if (encounter.IsEmpty)
                throw new ArgumentException("An encounter ID is required.", nameof(encounter));
            if (round < 0)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (uses < 0)
                throw new ArgumentOutOfRangeException(nameof(uses));
            Encounter = encounter;
            Round = round;
            Uses = uses;
        }

        public bool Equals(FrequencyState other) =>
            Encounter == other.Encounter && Round == other.Round && Uses == other.Uses;

        public override bool Equals(object obj) => obj is FrequencyState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Encounter, Round, Uses);

        public static bool operator ==(FrequencyState left, FrequencyState right) =>
            left.Equals(right);

        public static bool operator !=(FrequencyState left, FrequencyState right) =>
            !left.Equals(right);
    }
}
