using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Rules.Runtime
{
    public sealed class StateSliceSnapshot<TKey, TValue> : IReadOnlyCollection<KeyValuePair<TKey, TValue>>
    {
        private readonly IReadOnlyDictionary<TKey, TValue> values;

        internal StateSliceSnapshot(Dictionary<TKey, TValue> values)
        {
            this.values = new ReadOnlyDictionary<TKey, TValue>(values);
        }

        public int Count => values.Count;
        public TValue this[TKey key] => values[key];
        public bool Contains(TKey key) => values.ContainsKey(key);
        public bool TryGet(TKey key, out TValue value) => values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class StateSliceDraft<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> original;
        private readonly Func<TKey, TValue, bool> isValidEntry;
        private Dictionary<TKey, TValue> writable;

        internal StateSliceDraft(
            Dictionary<TKey, TValue> original,
            Func<TKey, TValue, bool> isValidEntry = null)
        {
            this.original = original;
            this.isValidEntry = isValidEntry;
        }

        internal bool IsDirty
        {
            get
            {
                if (writable == null || writable.Count != original.Count)
                    return writable != null;

                foreach (KeyValuePair<TKey, TValue> pair in writable)
                {
                    if (!original.TryGetValue(pair.Key, out TValue originalValue) ||
                        !EqualityComparer<TValue>.Default.Equals(pair.Value, originalValue))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        private Dictionary<TKey, TValue> Current => writable ?? original;

        public int Count => Current.Count;
        public bool Contains(TKey key) => Current.ContainsKey(key);
        public bool TryGet(TKey key, out TValue value) => Current.TryGetValue(key, out value);

        public bool Set(TKey key, TValue value)
        {
            if (isValidEntry != null && !isValidEntry(key, value))
                throw new ArgumentException("The state value does not match its slice key.", nameof(value));

            if (Current.TryGetValue(key, out TValue existing) &&
                EqualityComparer<TValue>.Default.Equals(existing, value))
            {
                return false;
            }

            EnsureWritable();
            writable[key] = value;
            return true;
        }

        public bool Remove(TKey key)
        {
            if (!Current.ContainsKey(key))
                return false;

            EnsureWritable();
            return writable.Remove(key);
        }

        private void EnsureWritable()
        {
            if (writable == null)
                writable = new Dictionary<TKey, TValue>(original);
        }

        internal Dictionary<TKey, TValue> BuildCommittedValues()
        {
            // A reducer can retain the draft reference. Never let that reference
            // share a writable dictionary with committed state or old snapshots.
            return writable == null ? original : new Dictionary<TKey, TValue>(writable);
        }
    }

    public sealed class RulesStateSeed
    {
        internal Dictionary<CreatureId, CreatureState> Creatures { get; } = new Dictionary<CreatureId, CreatureState>();
        internal Dictionary<CreatureId, HealthState> Health { get; } = new Dictionary<CreatureId, HealthState>();
        internal Dictionary<CreatureId, GridPosition> Positions { get; } = new Dictionary<CreatureId, GridPosition>();
        internal Dictionary<CreatureId, ActionEconomyState> ActionEconomy { get; } = new Dictionary<CreatureId, ActionEconomyState>();
        internal Dictionary<CreatureId, MultipleAttackPenaltyState> MultipleAttackPenalty { get; } = new Dictionary<CreatureId, MultipleAttackPenaltyState>();
        internal Dictionary<ConditionId, ConditionState> Conditions { get; } = new Dictionary<ConditionId, ConditionState>();
        internal Dictionary<ItemId, EquipmentState> Equipment { get; } = new Dictionary<ItemId, EquipmentState>();
        internal Dictionary<ActiveEffectId, ActiveEffectState> ActiveEffects { get; } = new Dictionary<ActiveEffectId, ActiveEffectState>();
        internal Dictionary<BindingId, ActiveRuleBinding> RuleBindings { get; } = new Dictionary<BindingId, ActiveRuleBinding>();
        internal Dictionary<BindingId, FrequencyState> Frequencies { get; } = new Dictionary<BindingId, FrequencyState>();

        public RulesStateSeed SeedCreature(CreatureState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Creatures[value.Id] = value;
            return this;
        }

        public RulesStateSeed SeedHealth(CreatureId creature, HealthState value)
        {
            RequireCreatureId(creature, nameof(creature));
            Health[creature] = value;
            return this;
        }

        public RulesStateSeed SeedPosition(CreatureId creature, GridPosition value)
        {
            RequireCreatureId(creature, nameof(creature));
            Positions[creature] = value;
            return this;
        }

        public RulesStateSeed SeedActionEconomy(CreatureId creature, ActionEconomyState value)
        {
            RequireCreatureId(creature, nameof(creature));
            ActionEconomy[creature] = value;
            return this;
        }

        public RulesStateSeed SeedMultipleAttackPenalty(CreatureId creature, MultipleAttackPenaltyState value)
        {
            RequireCreatureId(creature, nameof(creature));
            MultipleAttackPenalty[creature] = value;
            return this;
        }

        public RulesStateSeed SeedCondition(ConditionState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Conditions[value.Id] = value;
            return this;
        }

        public RulesStateSeed SeedEquipment(EquipmentState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Equipment[value.Id] = value;
            return this;
        }

        public RulesStateSeed SeedActiveEffect(ActiveEffectState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            ActiveEffects[value.Id] = value;
            return this;
        }

        /// <summary>
        /// Seeds one active or disabled rule binding before the store begins resolving operations.
        /// </summary>
        /// <param name="value">The immutable binding to add or replace by ID.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public RulesStateSeed SeedRuleBinding(ActiveRuleBinding value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            RuleBindings[value.Id] = value;
            return this;
        }

        public RulesStateSeed SeedFrequency(BindingId binding, FrequencyState value)
        {
            if (binding.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(binding));
            Frequencies[binding] = value;
            return this;
        }

        private static void RequireCreatureId(CreatureId creature, string parameterName)
        {
            if (creature.IsEmpty)
                throw new ArgumentException("A creature ID is required.", parameterName);
        }
    }

    internal sealed class RulesStateData
    {
        public long Version { get; }
        public Dictionary<CreatureId, CreatureState> Creatures { get; }
        public Dictionary<CreatureId, HealthState> Health { get; }
        public Dictionary<CreatureId, GridPosition> Positions { get; }
        public Dictionary<CreatureId, ActionEconomyState> ActionEconomy { get; }
        public Dictionary<CreatureId, MultipleAttackPenaltyState> MultipleAttackPenalty { get; }
        public Dictionary<ConditionId, ConditionState> Conditions { get; }
        public Dictionary<ItemId, EquipmentState> Equipment { get; }
        public Dictionary<ActiveEffectId, ActiveEffectState> ActiveEffects { get; }
        public Dictionary<BindingId, ActiveRuleBinding> RuleBindings { get; }
        public Dictionary<BindingId, FrequencyState> Frequencies { get; }

        public RulesStateData(RulesStateSeed seed)
            : this(
                0,
                new Dictionary<CreatureId, CreatureState>(seed.Creatures),
                new Dictionary<CreatureId, HealthState>(seed.Health),
                new Dictionary<CreatureId, GridPosition>(seed.Positions),
                new Dictionary<CreatureId, ActionEconomyState>(seed.ActionEconomy),
                new Dictionary<CreatureId, MultipleAttackPenaltyState>(seed.MultipleAttackPenalty),
                new Dictionary<ConditionId, ConditionState>(seed.Conditions),
                new Dictionary<ItemId, EquipmentState>(seed.Equipment),
                new Dictionary<ActiveEffectId, ActiveEffectState>(seed.ActiveEffects),
                new Dictionary<BindingId, ActiveRuleBinding>(seed.RuleBindings),
                new Dictionary<BindingId, FrequencyState>(seed.Frequencies))
        {
        }

        public RulesStateData(
            long version,
            Dictionary<CreatureId, CreatureState> creatures,
            Dictionary<CreatureId, HealthState> health,
            Dictionary<CreatureId, GridPosition> positions,
            Dictionary<CreatureId, ActionEconomyState> actionEconomy,
            Dictionary<CreatureId, MultipleAttackPenaltyState> multipleAttackPenalty,
            Dictionary<ConditionId, ConditionState> conditions,
            Dictionary<ItemId, EquipmentState> equipment,
            Dictionary<ActiveEffectId, ActiveEffectState> activeEffects,
            Dictionary<BindingId, ActiveRuleBinding> ruleBindings,
            Dictionary<BindingId, FrequencyState> frequencies)
        {
            Version = version;
            Creatures = creatures;
            Health = health;
            Positions = positions;
            ActionEconomy = actionEconomy;
            MultipleAttackPenalty = multipleAttackPenalty;
            Conditions = conditions;
            Equipment = equipment;
            ActiveEffects = activeEffects;
            RuleBindings = ruleBindings;
            Frequencies = frequencies;
        }
    }

    public sealed class RulesSnapshot
    {
        public long Version { get; }
        public StateSliceSnapshot<CreatureId, CreatureState> Creatures { get; }
        public StateSliceSnapshot<CreatureId, HealthState> Health { get; }
        public StateSliceSnapshot<CreatureId, GridPosition> Positions { get; }
        public StateSliceSnapshot<CreatureId, ActionEconomyState> ActionEconomy { get; }
        public StateSliceSnapshot<CreatureId, MultipleAttackPenaltyState> MultipleAttackPenalty { get; }
        public StateSliceSnapshot<ConditionId, ConditionState> Conditions { get; }
        public StateSliceSnapshot<ItemId, EquipmentState> Equipment { get; }
        public StateSliceSnapshot<ActiveEffectId, ActiveEffectState> ActiveEffects { get; }
        /// <summary>
        /// Gets the active and explicitly disabled rule bindings in committed state.
        /// </summary>
        public StateSliceSnapshot<BindingId, ActiveRuleBinding> RuleBindings { get; }
        public StateSliceSnapshot<BindingId, FrequencyState> Frequencies { get; }

        internal RulesSnapshot(RulesStateData data)
        {
            Version = data.Version;
            Creatures = new StateSliceSnapshot<CreatureId, CreatureState>(data.Creatures);
            Health = new StateSliceSnapshot<CreatureId, HealthState>(data.Health);
            Positions = new StateSliceSnapshot<CreatureId, GridPosition>(data.Positions);
            ActionEconomy = new StateSliceSnapshot<CreatureId, ActionEconomyState>(data.ActionEconomy);
            MultipleAttackPenalty = new StateSliceSnapshot<CreatureId, MultipleAttackPenaltyState>(data.MultipleAttackPenalty);
            Conditions = new StateSliceSnapshot<ConditionId, ConditionState>(data.Conditions);
            Equipment = new StateSliceSnapshot<ItemId, EquipmentState>(data.Equipment);
            ActiveEffects = new StateSliceSnapshot<ActiveEffectId, ActiveEffectState>(data.ActiveEffects);
            RuleBindings = new StateSliceSnapshot<BindingId, ActiveRuleBinding>(data.RuleBindings);
            Frequencies = new StateSliceSnapshot<BindingId, FrequencyState>(data.Frequencies);
        }
    }

    public sealed class RulesStateDraft
    {
        public StateSliceDraft<CreatureId, CreatureState> Creatures { get; }
        public StateSliceDraft<CreatureId, HealthState> Health { get; }
        public StateSliceDraft<CreatureId, GridPosition> Positions { get; }
        public StateSliceDraft<CreatureId, ActionEconomyState> ActionEconomy { get; }
        public StateSliceDraft<CreatureId, MultipleAttackPenaltyState> MultipleAttackPenalty { get; }
        public StateSliceDraft<ConditionId, ConditionState> Conditions { get; }
        public StateSliceDraft<ItemId, EquipmentState> Equipment { get; }
        public StateSliceDraft<ActiveEffectId, ActiveEffectState> ActiveEffects { get; }
        /// <summary>
        /// Gets controlled write access to rule bindings for the current reducer transaction.
        /// </summary>
        public StateSliceDraft<BindingId, ActiveRuleBinding> RuleBindings { get; }
        public StateSliceDraft<BindingId, FrequencyState> Frequencies { get; }

        internal RulesStateDraft(RulesStateData data)
        {
            Creatures = new StateSliceDraft<CreatureId, CreatureState>(data.Creatures, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            Health = new StateSliceDraft<CreatureId, HealthState>(data.Health, (id, value) => !id.IsEmpty);
            Positions = new StateSliceDraft<CreatureId, GridPosition>(data.Positions, (id, value) => !id.IsEmpty);
            ActionEconomy = new StateSliceDraft<CreatureId, ActionEconomyState>(data.ActionEconomy, (id, value) => !id.IsEmpty);
            MultipleAttackPenalty = new StateSliceDraft<CreatureId, MultipleAttackPenaltyState>(data.MultipleAttackPenalty, (id, value) => !id.IsEmpty);
            Conditions = new StateSliceDraft<ConditionId, ConditionState>(data.Conditions, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            Equipment = new StateSliceDraft<ItemId, EquipmentState>(data.Equipment, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            ActiveEffects = new StateSliceDraft<ActiveEffectId, ActiveEffectState>(data.ActiveEffects, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            RuleBindings = new StateSliceDraft<BindingId, ActiveRuleBinding>(data.RuleBindings, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            Frequencies = new StateSliceDraft<BindingId, FrequencyState>(data.Frequencies, (id, value) => !id.IsEmpty);
        }

        internal bool IsDirty =>
            Creatures.IsDirty || Health.IsDirty || Positions.IsDirty || ActionEconomy.IsDirty ||
            MultipleAttackPenalty.IsDirty || Conditions.IsDirty || Equipment.IsDirty ||
            ActiveEffects.IsDirty || RuleBindings.IsDirty || Frequencies.IsDirty;

        internal RulesStateData Build(long version)
        {
            return new RulesStateData(
                version,
                Creatures.BuildCommittedValues(),
                Health.BuildCommittedValues(),
                Positions.BuildCommittedValues(),
                ActionEconomy.BuildCommittedValues(),
                MultipleAttackPenalty.BuildCommittedValues(),
                Conditions.BuildCommittedValues(),
                Equipment.BuildCommittedValues(),
                ActiveEffects.BuildCommittedValues(),
                RuleBindings.BuildCommittedValues(),
                Frequencies.BuildCommittedValues());
        }
    }

    /// <summary>
    /// Immutable committed state. Live Unity components remain authoritative until a later migration seeds a slice.
    /// </summary>
    public sealed class RulesState
    {
        private readonly RulesStateData data;

        public RulesSnapshot Snapshot { get; }
        internal long Version => data.Version;

        public RulesState(RulesStateSeed seed)
        {
            if (seed == null)
                throw new ArgumentNullException(nameof(seed));
            data = new RulesStateData(seed);
            Snapshot = new RulesSnapshot(data);
        }

        internal RulesState(RulesStateData data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            Snapshot = new RulesSnapshot(data);
        }

        internal RulesStateDraft CreateDraft() => new RulesStateDraft(data);
    }
}
