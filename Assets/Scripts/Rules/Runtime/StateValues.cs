using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    public sealed class CreatureState : IEquatable<CreatureState>
    {
        private readonly IReadOnlyList<Trait> traits;

        public CreatureId Id { get; }
        public PlayerId Player { get; }
        public IReadOnlyList<Trait> Traits => traits;

        public CreatureState(CreatureId id, PlayerId player, IEnumerable<Trait> traits = null)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A creature ID is required.", nameof(id));
            if (player.IsEmpty)
                throw new ArgumentException("A player ID is required.", nameof(player));

            Id = id;
            Player = player;
            Trait[] copied = (traits ?? Array.Empty<Trait>()).Distinct().ToArray();
            if (copied.Any(trait => trait.IsEmpty))
                throw new ArgumentException("Creature traits cannot contain an empty trait.", nameof(traits));
            this.traits = Array.AsReadOnly(copied);
        }

        public bool Equals(CreatureState other) =>
            other != null && Id == other.Id && Player == other.Player && traits.SequenceEqual(other.traits);
        public override bool Equals(object obj) => obj is CreatureState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, Player);
    }

    public readonly struct HealthState : IEquatable<HealthState>
    {
        public int Current { get; }
        public int Maximum { get; }
        public int Temporary { get; }

        public HealthState(int current, int maximum, int temporary = 0)
        {
            if (maximum < 0)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (current < 0 || current > maximum)
                throw new ArgumentOutOfRangeException(nameof(current));
            if (temporary < 0)
                throw new ArgumentOutOfRangeException(nameof(temporary));

            Current = current;
            Maximum = maximum;
            Temporary = temporary;
        }

        public bool Equals(HealthState other) =>
            Current == other.Current && Maximum == other.Maximum && Temporary == other.Temporary;
        public override bool Equals(object obj) => obj is HealthState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Current, Maximum, Temporary);
        public static bool operator ==(HealthState left, HealthState right) => left.Equals(right);
        public static bool operator !=(HealthState left, HealthState right) => !left.Equals(right);
    }

    public readonly struct ActionEconomyState : IEquatable<ActionEconomyState>
    {
        public int ActionsRemaining { get; }
        public bool ReactionAvailable { get; }

        public ActionEconomyState(int actionsRemaining, bool reactionAvailable)
        {
            if (actionsRemaining < 0)
                throw new ArgumentOutOfRangeException(nameof(actionsRemaining));
            ActionsRemaining = actionsRemaining;
            ReactionAvailable = reactionAvailable;
        }

        public bool Equals(ActionEconomyState other) =>
            ActionsRemaining == other.ActionsRemaining && ReactionAvailable == other.ReactionAvailable;
        public override bool Equals(object obj) => obj is ActionEconomyState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ActionsRemaining, ReactionAvailable);
        public static bool operator ==(ActionEconomyState left, ActionEconomyState right) => left.Equals(right);
        public static bool operator !=(ActionEconomyState left, ActionEconomyState right) => !left.Equals(right);
    }

    public readonly struct MultipleAttackPenaltyState : IEquatable<MultipleAttackPenaltyState>
    {
        public int AttackCount { get; }

        public MultipleAttackPenaltyState(int attackCount)
        {
            if (attackCount < 0)
                throw new ArgumentOutOfRangeException(nameof(attackCount));
            AttackCount = attackCount;
        }

        public bool Equals(MultipleAttackPenaltyState other) => AttackCount == other.AttackCount;
        public override bool Equals(object obj) => obj is MultipleAttackPenaltyState other && Equals(other);
        public override int GetHashCode() => AttackCount;
        public static bool operator ==(MultipleAttackPenaltyState left, MultipleAttackPenaltyState right) => left.Equals(right);
        public static bool operator !=(MultipleAttackPenaltyState left, MultipleAttackPenaltyState right) => !left.Equals(right);
    }

    public sealed class ConditionState : IEquatable<ConditionState>
    {
        public ConditionId Id { get; }
        public RuleDefinitionId DefinitionId { get; }
        public CreatureId Owner { get; }
        public int Value { get; }
        public RuleSource Source { get; }

        public ConditionState(
            ConditionId id,
            RuleDefinitionId definitionId,
            CreatureId owner,
            int value,
            RuleSource source)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (id.IsEmpty)
                throw new ArgumentException("A condition ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(definitionId));
            if (owner.IsEmpty)
                throw new ArgumentException("An owner creature ID is required.", nameof(owner));
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            Id = id;
            DefinitionId = definitionId;
            Owner = owner;
            Value = value;
            Source = source;
        }

        public bool Equals(ConditionState other) =>
            other != null && Id == other.Id && DefinitionId == other.DefinitionId &&
            Owner == other.Owner && Value == other.Value && Source == other.Source;
        public override bool Equals(object obj) => obj is ConditionState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, DefinitionId, Owner, Value, Source);
    }

    public sealed class EquipmentState : IEquatable<EquipmentState>
    {
        public ItemId Id { get; }
        public ItemDefinitionId DefinitionId { get; }
        public CreatureId Holder { get; }
        public bool IsWielded { get; }

        public EquipmentState(ItemId id, ItemDefinitionId definitionId, CreatureId holder, bool isWielded)
        {
            if (id.IsEmpty)
                throw new ArgumentException("An item ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException("An item definition ID is required.", nameof(definitionId));
            if (holder.IsEmpty)
                throw new ArgumentException("A holder creature ID is required.", nameof(holder));
            Id = id;
            DefinitionId = definitionId;
            Holder = holder;
            IsWielded = isWielded;
        }

        public bool Equals(EquipmentState other) =>
            other != null && Id == other.Id && DefinitionId == other.DefinitionId &&
            Holder == other.Holder && IsWielded == other.IsWielded;
        public override bool Equals(object obj) => obj is EquipmentState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, DefinitionId, Holder, IsWielded);
    }

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
