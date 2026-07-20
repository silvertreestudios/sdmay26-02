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
                throw new ArgumentException(
                    "Creature traits cannot contain an empty trait.",
                    nameof(traits)
                );
            this.traits = Array.AsReadOnly(copied);
        }

        public bool Equals(CreatureState other) =>
            other != null
            && Id == other.Id
            && Player == other.Player
            && traits.SequenceEqual(other.traits);

        public override bool Equals(object obj) => obj is CreatureState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Id, Player);
    }

    /// <summary>
    /// Stores one creature's immutable current, maximum, temporary, and temporary-source health
    /// state for authoritative rules snapshots.
    /// </summary>
    public readonly struct HealthState : IEquatable<HealthState>
    {
        private readonly RuleSource[] temporaryHitPointImmunities;

        /// <summary>Gets current Hit Points.</summary>
        public int Current { get; }

        /// <summary>Gets maximum Hit Points.</summary>
        public int Maximum { get; }

        /// <summary>Gets temporary Hit Points.</summary>
        public int Temporary { get; }

        /// <summary>Gets the rule source that owns the current temporary-HP pool.</summary>
        public RuleSource TemporarySource { get; }

        /// <summary>
        /// Gets the rule sources that cannot currently grant temporary Hit Points.
        /// </summary>
        public IReadOnlyList<RuleSource> TemporaryHitPointImmunities =>
            Array.AsReadOnly(temporaryHitPointImmunities ?? Array.Empty<RuleSource>());

        /// <summary>
        /// Initializes one creature's complete authoritative health state.
        /// </summary>
        /// <param name="current">The current Hit Points, from zero through <paramref name="maximum"/>.</param>
        /// <param name="maximum">The creature's maximum Hit Points.</param>
        /// <param name="temporary">The current temporary Hit Points.</param>
        /// <param name="temporarySource">
        /// The source that owns <paramref name="temporary"/>, or the empty source for imported
        /// temporary Hit Points whose original source is unavailable.
        /// </param>
        public HealthState(
            int current,
            int maximum,
            int temporary = 0,
            RuleSource temporarySource = default
        )
            : this(current, maximum, temporary, temporarySource, Array.Empty<RuleSource>()) { }

        /// <summary>
        /// Initializes authoritative health with source-specific temporary-HP immunities.
        /// </summary>
        /// <param name="current">The current Hit Points, from zero through <paramref name="maximum"/>.</param>
        /// <param name="maximum">The creature's maximum Hit Points.</param>
        /// <param name="temporary">The current temporary Hit Points.</param>
        /// <param name="temporarySource">The source that owns <paramref name="temporary"/>.</param>
        /// <param name="temporaryHitPointImmunities">Sources blocked from granting temporary Hit Points.</param>
        public HealthState(
            int current,
            int maximum,
            int temporary,
            RuleSource temporarySource,
            IEnumerable<RuleSource> temporaryHitPointImmunities
        )
        {
            if (maximum < 0)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (current < 0 || current > maximum)
                throw new ArgumentOutOfRangeException(nameof(current));
            if (temporary < 0)
                throw new ArgumentOutOfRangeException(nameof(temporary));
            if (temporary == 0 && !temporarySource.IsEmpty)
                throw new ArgumentException(
                    "Health without temporary Hit Points cannot retain a temporary-HP source.",
                    nameof(temporarySource)
                );

            if (temporaryHitPointImmunities == null)
                throw new ArgumentNullException(nameof(temporaryHitPointImmunities));
            RuleSource[] copiedImmunities = temporaryHitPointImmunities.Distinct().ToArray();
            if (copiedImmunities.Any(source => source.IsEmpty))
                throw new ArgumentException(
                    "Temporary Hit Point immunities cannot contain an empty rule source.",
                    nameof(temporaryHitPointImmunities)
                );

            Current = current;
            Maximum = maximum;
            Temporary = temporary;
            TemporarySource = temporarySource;
            this.temporaryHitPointImmunities = copiedImmunities;
        }

        public bool Equals(HealthState other) =>
            Current == other.Current
            && Maximum == other.Maximum
            && Temporary == other.Temporary
            && TemporarySource == other.TemporarySource
            && (temporaryHitPointImmunities ?? Array.Empty<RuleSource>()).SequenceEqual(
                other.temporaryHitPointImmunities ?? Array.Empty<RuleSource>()
            );

        public override bool Equals(object obj) => obj is HealthState other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Current, Maximum, Temporary, TemporarySource);

        /// <summary>
        /// Checks whether a source is blocked from granting temporary Hit Points.
        /// </summary>
        /// <param name="source">The non-empty source to inspect.</param>
        /// <returns><see langword="true"/> when the immunity is present.</returns>
        public bool HasTemporaryHitPointImmunity(RuleSource source) =>
            !source.IsEmpty
            && (temporaryHitPointImmunities ?? Array.Empty<RuleSource>()).Contains(source);

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
            ActionsRemaining == other.ActionsRemaining
            && ReactionAvailable == other.ReactionAvailable;

        public override bool Equals(object obj) => obj is ActionEconomyState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ActionsRemaining, ReactionAvailable);

        public static bool operator ==(ActionEconomyState left, ActionEconomyState right) =>
            left.Equals(right);

        public static bool operator !=(ActionEconomyState left, ActionEconomyState right) =>
            !left.Equals(right);
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

        public override bool Equals(object obj) =>
            obj is MultipleAttackPenaltyState other && Equals(other);

        public override int GetHashCode() => AttackCount;

        public static bool operator ==(
            MultipleAttackPenaltyState left,
            MultipleAttackPenaltyState right
        ) => left.Equals(right);

        public static bool operator !=(
            MultipleAttackPenaltyState left,
            MultipleAttackPenaltyState right
        ) => !left.Equals(right);
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
            RuleSource source
        )
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (id.IsEmpty)
                throw new ArgumentException("A condition ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A rule definition ID is required.",
                    nameof(definitionId)
                );
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
            other != null
            && Id == other.Id
            && DefinitionId == other.DefinitionId
            && Owner == other.Owner
            && Value == other.Value
            && Source == other.Source;

        public override bool Equals(object obj) => obj is ConditionState other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Id, DefinitionId, Owner, Value, Source);
    }

    public sealed class EquipmentState : IEquatable<EquipmentState>
    {
        public ItemId Id { get; }
        public ItemDefinitionId DefinitionId { get; }
        public CreatureId Holder { get; }
        public bool IsWielded { get; }

        public EquipmentState(
            ItemId id,
            ItemDefinitionId definitionId,
            CreatureId holder,
            bool isWielded
        )
        {
            if (id.IsEmpty)
                throw new ArgumentException("An item ID is required.", nameof(id));
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "An item definition ID is required.",
                    nameof(definitionId)
                );
            if (holder.IsEmpty)
                throw new ArgumentException("A holder creature ID is required.", nameof(holder));
            Id = id;
            DefinitionId = definitionId;
            Holder = holder;
            IsWielded = isWielded;
        }

        public bool Equals(EquipmentState other) =>
            other != null
            && Id == other.Id
            && DefinitionId == other.DefinitionId
            && Holder == other.Holder
            && IsWielded == other.IsWielded;

        public override bool Equals(object obj) => obj is EquipmentState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Id, DefinitionId, Holder, IsWielded);
    }
}
