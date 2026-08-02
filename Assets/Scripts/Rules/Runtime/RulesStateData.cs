using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    internal sealed class RulesStateData
    {
        public long Version { get; }
        public Dictionary<CreatureId, CreatureState> Creatures { get; }
        public Dictionary<CreatureId, PreparedCreatureInputs> PreparedInputs { get; }
        public Dictionary<CreatureId, CreatureStatisticsState> Statistics { get; }
        public Dictionary<CreatureId, HealthState> Health { get; }
        internal Dictionary<CreatureId, long> TemporaryHitPointRevisionTombstones { get; }
        public Dictionary<CreatureId, GridPosition> Positions { get; }
        public Dictionary<CreatureId, GridDistance> LandSpeeds { get; }
        public Dictionary<CreatureId, MovementBudgetState> MovementBudgets { get; }
        public Dictionary<CreatureId, ActionEconomyState> ActionEconomy { get; }
        public Dictionary<SpellSlotPoolId, SpellSlotState> SpellSlots { get; }
        public Dictionary<CreatureId, FocusPointState> FocusPoints { get; }
        public Dictionary<ItemId, AmmunitionState> Ammunition { get; }
        public Dictionary<CreatureId, MultipleAttackPenaltyState> MultipleAttackPenalty { get; }
        public Dictionary<ItemId, EquipmentState> Equipment { get; }
        public Dictionary<ActiveEffectId, ActiveEffectInstance> ActiveEffects { get; }
        public Dictionary<BindingId, ActiveRuleBinding> RuleBindings { get; }
        public Dictionary<BindingId, long> StatelessRuleBindingGenerations { get; }
        public Dictionary<BindingId, FrequencyState> Frequencies { get; }
        public Dictionary<EncounterId, EncounterState> Encounters { get; }
        public Dictionary<ActiveEffectId, ActiveEffectTimingState> ActiveEffectTimings { get; }

        public RulesStateData(RulesStateSeed seed)
            : this(
                0,
                new Dictionary<CreatureId, CreatureState>(seed.Creatures),
                new Dictionary<CreatureId, PreparedCreatureInputs>(seed.PreparedInputs),
                new Dictionary<CreatureId, CreatureStatisticsState>(seed.Statistics),
                new Dictionary<CreatureId, HealthState>(seed.Health),
                new Dictionary<CreatureId, long>(),
                new Dictionary<CreatureId, GridPosition>(seed.Positions),
                new Dictionary<CreatureId, GridDistance>(seed.LandSpeeds),
                new Dictionary<CreatureId, MovementBudgetState>(seed.MovementBudgets),
                new Dictionary<CreatureId, ActionEconomyState>(seed.ActionEconomy),
                new Dictionary<SpellSlotPoolId, SpellSlotState>(seed.SpellSlots),
                new Dictionary<CreatureId, FocusPointState>(seed.FocusPoints),
                new Dictionary<ItemId, AmmunitionState>(seed.Ammunition),
                new Dictionary<CreatureId, MultipleAttackPenaltyState>(seed.MultipleAttackPenalty),
                new Dictionary<ItemId, EquipmentState>(seed.Equipment),
                new Dictionary<ActiveEffectId, ActiveEffectInstance>(seed.ActiveEffects),
                new Dictionary<BindingId, ActiveRuleBinding>(seed.RuleBindings),
                new Dictionary<BindingId, long>(seed.StatelessRuleBindingGenerations),
                new Dictionary<BindingId, FrequencyState>(seed.Frequencies),
                new Dictionary<EncounterId, EncounterState>(seed.Encounters),
                new Dictionary<ActiveEffectId, ActiveEffectTimingState>(seed.ActiveEffectTimings)
            ) { }

        public RulesStateData(
            long version,
            Dictionary<CreatureId, CreatureState> creatures,
            Dictionary<CreatureId, PreparedCreatureInputs> preparedInputs,
            Dictionary<CreatureId, CreatureStatisticsState> statistics,
            Dictionary<CreatureId, HealthState> health,
            Dictionary<CreatureId, long> temporaryHitPointRevisionTombstones,
            Dictionary<CreatureId, GridPosition> positions,
            Dictionary<CreatureId, GridDistance> landSpeeds,
            Dictionary<CreatureId, MovementBudgetState> movementBudgets,
            Dictionary<CreatureId, ActionEconomyState> actionEconomy,
            Dictionary<SpellSlotPoolId, SpellSlotState> spellSlots,
            Dictionary<CreatureId, FocusPointState> focusPoints,
            Dictionary<ItemId, AmmunitionState> ammunition,
            Dictionary<CreatureId, MultipleAttackPenaltyState> multipleAttackPenalty,
            Dictionary<ItemId, EquipmentState> equipment,
            Dictionary<ActiveEffectId, ActiveEffectInstance> activeEffects,
            Dictionary<BindingId, ActiveRuleBinding> ruleBindings,
            Dictionary<BindingId, long> statelessRuleBindingGenerations,
            Dictionary<BindingId, FrequencyState> frequencies,
            Dictionary<EncounterId, EncounterState> encounters,
            Dictionary<ActiveEffectId, ActiveEffectTimingState> activeEffectTimings
        )
        {
            Version = version;
            Creatures = creatures;
            PreparedInputs = preparedInputs;
            Statistics = statistics;
            Health = health;
            TemporaryHitPointRevisionTombstones = temporaryHitPointRevisionTombstones;
            Positions = positions;
            LandSpeeds = landSpeeds;
            MovementBudgets = movementBudgets;
            ActionEconomy = actionEconomy;
            SpellSlots = spellSlots;
            FocusPoints = focusPoints;
            Ammunition = ammunition;
            MultipleAttackPenalty = multipleAttackPenalty;
            Equipment = equipment;
            ActiveEffects = activeEffects;
            RuleBindings = ruleBindings;
            StatelessRuleBindingGenerations = statelessRuleBindingGenerations;
            Frequencies = frequencies;
            Encounters = encounters;
            ActiveEffectTimings = activeEffectTimings;
            ConditionImmunityValidation.ValidateStateInvariant(
                Creatures,
                PreparedInputs,
                ActiveEffects,
                RuleBindings
            );
        }
    }
}
