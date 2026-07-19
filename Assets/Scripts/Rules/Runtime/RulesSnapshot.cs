namespace Game.Rules.Runtime
{
    public sealed class RulesSnapshot
    {
        public long Version { get; }
        public StateSliceSnapshot<CreatureId, CreatureState> Creatures { get; }

        /// <summary>
        /// Gets base check values and snapshot-owned modifier inputs keyed by creature.
        /// </summary>
        public StateSliceSnapshot<CreatureId, CreatureStatisticsState> Statistics { get; }
        public StateSliceSnapshot<CreatureId, HealthState> Health { get; }
        public StateSliceSnapshot<CreatureId, GridPosition> Positions { get; }
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
            Statistics = new StateSliceSnapshot<CreatureId, CreatureStatisticsState>(
                data.Statistics
            );
            Health = new StateSliceSnapshot<CreatureId, HealthState>(data.Health);
            Positions = new StateSliceSnapshot<CreatureId, GridPosition>(data.Positions);
            ActionEconomy = new StateSliceSnapshot<CreatureId, ActionEconomyState>(
                data.ActionEconomy
            );
            SpellSlots = new StateSliceSnapshot<SpellSlotPoolId, SpellSlotState>(data.SpellSlots);
            FocusPoints = new StateSliceSnapshot<CreatureId, FocusPointState>(data.FocusPoints);
            Ammunition = new StateSliceSnapshot<ItemId, AmmunitionState>(data.Ammunition);
            MultipleAttackPenalty = new StateSliceSnapshot<CreatureId, MultipleAttackPenaltyState>(
                data.MultipleAttackPenalty
            );
            Conditions = new StateSliceSnapshot<ConditionId, ConditionState>(data.Conditions);
            Equipment = new StateSliceSnapshot<ItemId, EquipmentState>(data.Equipment);
            ActiveEffects = new StateSliceSnapshot<ActiveEffectId, ActiveEffectState>(
                data.ActiveEffects
            );
            RuleBindings = new StateSliceSnapshot<BindingId, ActiveRuleBinding>(data.RuleBindings);
            Frequencies = new StateSliceSnapshot<BindingId, FrequencyState>(data.Frequencies);
        }
    }
}
