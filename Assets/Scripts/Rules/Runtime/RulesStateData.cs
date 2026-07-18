using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    internal sealed class RulesStateData
    {
        public long Version { get; }
        public Dictionary<CreatureId, CreatureState> Creatures { get; }
        public Dictionary<CreatureId, HealthState> Health { get; }
        public Dictionary<CreatureId, GridPosition> Positions { get; }
        public Dictionary<CreatureId, ActionEconomyState> ActionEconomy { get; }
        public Dictionary<SpellSlotPoolId, SpellSlotState> SpellSlots { get; }
        public Dictionary<CreatureId, FocusPointState> FocusPoints { get; }
        public Dictionary<ItemId, AmmunitionState> Ammunition { get; }
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
                new Dictionary<SpellSlotPoolId, SpellSlotState>(seed.SpellSlots),
                new Dictionary<CreatureId, FocusPointState>(seed.FocusPoints),
                new Dictionary<ItemId, AmmunitionState>(seed.Ammunition),
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
            Dictionary<SpellSlotPoolId, SpellSlotState> spellSlots,
            Dictionary<CreatureId, FocusPointState> focusPoints,
            Dictionary<ItemId, AmmunitionState> ammunition,
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
            SpellSlots = spellSlots;
            FocusPoints = focusPoints;
            Ammunition = ammunition;
            MultipleAttackPenalty = multipleAttackPenalty;
            Conditions = conditions;
            Equipment = equipment;
            ActiveEffects = activeEffects;
            RuleBindings = ruleBindings;
            Frequencies = frequencies;
        }
    }
}
