namespace Game.Rules.Runtime
{
    public sealed class RulesSnapshot
    {
        public long Version { get; }
        public StateSliceSnapshot<CreatureId, CreatureState> Creatures { get; }

        /// <summary>Gets immutable build-time inputs keyed by their creature owner.</summary>
        public StateSliceSnapshot<CreatureId, PreparedCreatureInputs> PreparedInputs { get; }

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
        public StateSliceSnapshot<ConditionId, ConditionState> Conditions { get; }
        public StateSliceSnapshot<ItemId, EquipmentState> Equipment { get; }

        /// <summary>
        /// Gets immutable typed effect instances, including expired instances awaiting removal.
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
            PreparedInputs = new StateSliceSnapshot<CreatureId, PreparedCreatureInputs>(
                data.PreparedInputs
            );
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
            Conditions = new StateSliceSnapshot<ConditionId, ConditionState>(data.Conditions);
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
        }
    }
}
