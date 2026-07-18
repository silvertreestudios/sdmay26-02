using System;

namespace Game.Rules.Runtime
{
    public sealed class ActiveEffectState : IEquatable<ActiveEffectState>
    {
        public ActiveEffectId Id { get; }
        public RuleDefinitionId DefinitionId { get; }
        public CreatureId SourceCreature { get; }
        public RuleSource Source { get; }

        public ActiveEffectState(
            ActiveEffectId id,
            RuleDefinitionId definitionId,
            CreatureId sourceCreature,
            RuleSource source)
        {
            if (id.IsEmpty)
                throw new ArgumentException("An active effect ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(definitionId));
            if (sourceCreature.IsEmpty)
                throw new ArgumentException("A source creature ID is required.", nameof(sourceCreature));
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            Id = id;
            DefinitionId = definitionId;
            SourceCreature = sourceCreature;
            Source = source;
        }

        public bool Equals(ActiveEffectState other) =>
            other != null && Id == other.Id && DefinitionId == other.DefinitionId &&
            SourceCreature == other.SourceCreature && Source == other.Source;
        public override bool Equals(object obj) => obj is ActiveEffectState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, DefinitionId, SourceCreature, Source);
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
            bool isEnabled = true)
        {
            if (creationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(creationOrder));
            if (id.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(definitionId));
            if (owner.IsEmpty)
                throw new ArgumentException("An owner creature ID is required.", nameof(owner));
            if (effectId.HasValue && effectId.Value.IsEmpty)
                throw new ArgumentException("An effect ID cannot be empty when supplied.", nameof(effectId));
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

        /// <inheritdoc/>
        public bool Equals(ActiveRuleBinding other) =>
            other != null && Id == other.Id && DefinitionId == other.DefinitionId &&
            Owner == other.Owner && EffectId == other.EffectId && Source == other.Source &&
            CreationOrder == other.CreationOrder && IsEnabled == other.IsEnabled;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActiveRuleBinding other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Id, DefinitionId, Owner, EffectId, Source, CreationOrder, IsEnabled);
    }

    public readonly struct FrequencyState : IEquatable<FrequencyState>
    {
        public int Round { get; }
        public int Uses { get; }

        public FrequencyState(int round, int uses)
        {
            if (round < 0)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (uses < 0)
                throw new ArgumentOutOfRangeException(nameof(uses));
            Round = round;
            Uses = uses;
        }

        public bool Equals(FrequencyState other) => Round == other.Round && Uses == other.Uses;
        public override bool Equals(object obj) => obj is FrequencyState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Round, Uses);
        public static bool operator ==(FrequencyState left, FrequencyState right) => left.Equals(right);
        public static bool operator !=(FrequencyState left, FrequencyState right) => !left.Equals(right);
    }
}
