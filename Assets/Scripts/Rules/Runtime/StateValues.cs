using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            Id = id;
            Player = player;
            Trait[] copied = (traits ?? Array.Empty<Trait>()).Distinct().ToArray();
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
        public long Version { get; }
        public RuleValueMap Values { get; }

        public ActiveEffectState(
            ActiveEffectId id,
            RuleDefinitionId definitionId,
            CreatureId sourceCreature,
            RuleSource source,
            long version,
            RuleValueMap values = null)
        {
            if (version < 0)
                throw new ArgumentOutOfRangeException(nameof(version));
            Id = id;
            DefinitionId = definitionId;
            SourceCreature = sourceCreature;
            Source = source;
            Version = version;
            Values = values == null ? RuleValueMap.Empty : new RuleValueMap(values);
        }

        public bool Equals(ActiveEffectState other) =>
            other != null && Id == other.Id && DefinitionId == other.DefinitionId &&
            SourceCreature == other.SourceCreature && Source == other.Source &&
            Version == other.Version && Values.Equals(other.Values);
        public override bool Equals(object obj) => obj is ActiveEffectState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, DefinitionId, SourceCreature, Source, Version);
    }

    public sealed class RuleBindingState : IEquatable<RuleBindingState>
    {
        public BindingId Id { get; }
        public RuleDefinitionId DefinitionId { get; }
        public CreatureId Owner { get; }
        public ActiveEffectId? EffectId { get; }
        public RuleSource Source { get; }
        public long CreationOrder { get; }

        public RuleBindingState(
            BindingId id,
            RuleDefinitionId definitionId,
            CreatureId owner,
            ActiveEffectId? effectId,
            RuleSource source,
            long creationOrder)
        {
            if (creationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(creationOrder));
            Id = id;
            DefinitionId = definitionId;
            Owner = owner;
            EffectId = effectId;
            Source = source;
            CreationOrder = creationOrder;
        }

        public bool Equals(RuleBindingState other) =>
            other != null && Id == other.Id && DefinitionId == other.DefinitionId &&
            Owner == other.Owner && EffectId == other.EffectId && Source == other.Source &&
            CreationOrder == other.CreationOrder;
        public override bool Equals(object obj) => obj is RuleBindingState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, DefinitionId, Owner, EffectId, Source, CreationOrder);
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
