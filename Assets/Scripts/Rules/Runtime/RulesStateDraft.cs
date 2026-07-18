using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Rules.Runtime
{
    public sealed class RulesStateDraft
    {
        public StateSliceDraft<CreatureId, CreatureState> Creatures { get; }
        public StateSliceDraft<CreatureId, HealthState> Health { get; }
        public StateSliceDraft<CreatureId, GridPosition> Positions { get; }
        public StateSliceDraft<CreatureId, ActionEconomyState> ActionEconomy { get; }
        /// <summary>
        /// Gets transaction-scoped write access to spell-slot pools.
        /// </summary>
        public StateSliceDraft<SpellSlotPoolId, SpellSlotState> SpellSlots { get; }
        /// <summary>
        /// Gets transaction-scoped write access to Focus Point pools.
        /// </summary>
        public StateSliceDraft<CreatureId, FocusPointState> FocusPoints { get; }
        /// <summary>
        /// Gets transaction-scoped write access to ammunition pools.
        /// </summary>
        public StateSliceDraft<ItemId, AmmunitionState> Ammunition { get; }
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
            SpellSlots = new StateSliceDraft<SpellSlotPoolId, SpellSlotState>(
                data.SpellSlots,
                (id, value) => !id.IsEmpty && id == value.Id);
            FocusPoints = new StateSliceDraft<CreatureId, FocusPointState>(
                data.FocusPoints,
                (id, value) => !id.IsEmpty);
            Ammunition = new StateSliceDraft<ItemId, AmmunitionState>(
                data.Ammunition,
                (id, value) => !id.IsEmpty && id == value.Item);
            MultipleAttackPenalty = new StateSliceDraft<CreatureId, MultipleAttackPenaltyState>(data.MultipleAttackPenalty, (id, value) => !id.IsEmpty);
            Conditions = new StateSliceDraft<ConditionId, ConditionState>(data.Conditions, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            Equipment = new StateSliceDraft<ItemId, EquipmentState>(data.Equipment, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            ActiveEffects = new StateSliceDraft<ActiveEffectId, ActiveEffectState>(data.ActiveEffects, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            RuleBindings = new StateSliceDraft<BindingId, ActiveRuleBinding>(data.RuleBindings, (id, value) => !id.IsEmpty && value != null && id == value.Id);
            Frequencies = new StateSliceDraft<BindingId, FrequencyState>(data.Frequencies, (id, value) => !id.IsEmpty);
        }

        internal bool IsDirty =>
            Creatures.IsDirty || Health.IsDirty || Positions.IsDirty || ActionEconomy.IsDirty ||
            SpellSlots.IsDirty || FocusPoints.IsDirty || Ammunition.IsDirty ||
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
                SpellSlots.BuildCommittedValues(),
                FocusPoints.BuildCommittedValues(),
                Ammunition.BuildCommittedValues(),
                MultipleAttackPenalty.BuildCommittedValues(),
                Conditions.BuildCommittedValues(),
                Equipment.BuildCommittedValues(),
                ActiveEffects.BuildCommittedValues(),
                RuleBindings.BuildCommittedValues(),
                Frequencies.BuildCommittedValues());
        }
    }
}
