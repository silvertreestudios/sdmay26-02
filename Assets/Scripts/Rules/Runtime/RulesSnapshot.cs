using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    public sealed class RulesSnapshot
    {
        private readonly StateSliceSnapshot<RuleStateSlot, object> stateValues;

        public long Version { get; }
        public StateSliceSnapshot<CreatureId, CreatureState> Creatures { get; }

        /// <summary>
        /// Gets base check values and snapshot-owned modifier inputs keyed by creature.
        /// </summary>
        public StateSliceSnapshot<CreatureId, CreatureStatisticsState> Statistics { get; }
        public StateSliceSnapshot<CreatureId, HealthState> Health { get; }
        public StateSliceSnapshot<CreatureId, GridPosition> Positions { get; }

        /// <summary>Gets authoritative land Speeds keyed by creature.</summary>
        public StateSliceSnapshot<CreatureId, GridDistance> LandSpeeds { get; }

        /// <summary>
        /// Gets authoritative movement allowances and turn-persistent diagonal phases by creature.
        /// </summary>
        public StateSliceSnapshot<CreatureId, MovementBudgetState> MovementBudgets { get; }
        public StateSliceSnapshot<CreatureId, ActionEconomyState> ActionEconomy { get; }

        /// <summary>
        /// Gets the authoritative spell-slot pools keyed by stable pool identity.
        /// </summary>
        public StateSliceSnapshot<SpellSlotPoolId, SpellSlotState> SpellSlots { get; }

        /// <summary>
        /// Gets authoritative Focus Point pools keyed by their owning creature.
        /// </summary>
        public StateSliceSnapshot<CreatureId, FocusPointState> FocusPoints { get; }

        /// <summary>
        /// Gets authoritative ammunition pools keyed by stable item identity.
        /// </summary>
        public StateSliceSnapshot<ItemId, AmmunitionState> Ammunition { get; }
        public StateSliceSnapshot<
            CreatureId,
            MultipleAttackPenaltyState
        > MultipleAttackPenalty { get; }
        public StateSliceSnapshot<ItemId, EquipmentState> Equipment { get; }

        /// <summary>
        /// Gets immutable typed active effects. Expiration removes the instance from this slice.
        /// </summary>
        public StateSliceSnapshot<ActiveEffectId, ActiveEffectInstance> ActiveEffects { get; }

        /// <summary>
        /// Gets the active and explicitly disabled rule bindings in committed state.
        /// </summary>
        public StateSliceSnapshot<BindingId, ActiveRuleBinding> RuleBindings { get; }
        public StateSliceSnapshot<BindingId, FrequencyState> Frequencies { get; }

        /// <summary>Gets authoritative encounter clocks keyed by encounter identity.</summary>
        public StateSliceSnapshot<EncounterId, EncounterState> Encounters { get; }

        /// <summary>Gets finite active-effect schedules keyed by effect identity.</summary>
        public StateSliceSnapshot<
            ActiveEffectId,
            ActiveEffectTimingState
        > ActiveEffectTimings { get; }

        internal RulesSnapshot(RulesStateData data)
        {
            Version = data.Version;
            Creatures = new StateSliceSnapshot<CreatureId, CreatureState>(data.Creatures);
            Statistics = new StateSliceSnapshot<CreatureId, CreatureStatisticsState>(
                data.Statistics
            );
            Health = new StateSliceSnapshot<CreatureId, HealthState>(data.Health);
            Positions = new StateSliceSnapshot<CreatureId, GridPosition>(data.Positions);
            LandSpeeds = new StateSliceSnapshot<CreatureId, GridDistance>(data.LandSpeeds);
            MovementBudgets = new StateSliceSnapshot<CreatureId, MovementBudgetState>(
                data.MovementBudgets
            );
            ActionEconomy = new StateSliceSnapshot<CreatureId, ActionEconomyState>(
                data.ActionEconomy
            );
            SpellSlots = new StateSliceSnapshot<SpellSlotPoolId, SpellSlotState>(data.SpellSlots);
            FocusPoints = new StateSliceSnapshot<CreatureId, FocusPointState>(data.FocusPoints);
            Ammunition = new StateSliceSnapshot<ItemId, AmmunitionState>(data.Ammunition);
            MultipleAttackPenalty = new StateSliceSnapshot<CreatureId, MultipleAttackPenaltyState>(
                data.MultipleAttackPenalty
            );
            Equipment = new StateSliceSnapshot<ItemId, EquipmentState>(data.Equipment);
            ActiveEffects = new StateSliceSnapshot<ActiveEffectId, ActiveEffectInstance>(
                data.ActiveEffects
            );
            RuleBindings = new StateSliceSnapshot<BindingId, ActiveRuleBinding>(data.RuleBindings);
            Frequencies = new StateSliceSnapshot<BindingId, FrequencyState>(data.Frequencies);
            Encounters = new StateSliceSnapshot<EncounterId, EncounterState>(data.Encounters);
            ActiveEffectTimings = new StateSliceSnapshot<ActiveEffectId, ActiveEffectTimingState>(
                data.ActiveEffectTimings
            );
            stateValues = new StateSliceSnapshot<RuleStateSlot, object>(data.StateValues);
        }

        /// <summary>Gets an immutable feature-owned value by its stable typed key.</summary>
        /// <typeparam name="TState">The immutable state type declared by the key.</typeparam>
        /// <param name="key">The owning feature's stable state key.</param>
        /// <returns>The committed value in this exact snapshot.</returns>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        /// <exception cref="KeyNotFoundException">No value is registered for the key.</exception>
        public TState GetState<TState>(RuleStateKey<TState> key)
            where TState : class
        {
            RequireStateKey(key);
            if (!stateValues.TryGet(key.Slot, out object value))
                throw new KeyNotFoundException($"No rules state is registered for '{key}'.");
            return (TState)value;
        }

        /// <summary>Tries to get an immutable feature-owned value from this exact snapshot.</summary>
        /// <typeparam name="TState">The immutable state type declared by the key.</typeparam>
        /// <param name="key">The owning feature's stable state key.</param>
        /// <param name="value">The committed value when registered; otherwise, the default.</param>
        /// <returns><see langword="true"/> when the key is registered.</returns>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        public bool TryGetState<TState>(RuleStateKey<TState> key, out TState value)
            where TState : class
        {
            RequireStateKey(key);
            if (stateValues.TryGet(key.Slot, out object stored))
            {
                value = (TState)stored;
                return true;
            }

            value = default;
            return false;
        }

        private static void RequireStateKey<TState>(RuleStateKey<TState> key)
            where TState : class
        {
            if (key.IsEmpty)
                throw new ArgumentException("A rule-state key is required.", nameof(key));
        }
    }
}
