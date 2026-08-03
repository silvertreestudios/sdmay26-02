using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Provides the complete initial rules state for one encounter participant.</summary>
    public sealed class CombatantRulesState
    {
        /// <summary>Creates one immutable participant registration.</summary>
        public CombatantRulesState(
            CreatureState creature,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            PreparedCreatureInputs preparedInputs,
            IReadOnlyList<SpellSlotState> spellSlots,
            IReadOnlyList<ActiveRuleBinding> ruleBindings
        )
        {
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            PreparedInputs =
                preparedInputs ?? throw new ArgumentNullException(nameof(preparedInputs));
            SpellSlots = CopyOwned(spellSlots, creature.Id);
            RuleBindings = CopyOwned(ruleBindings, creature.Id);
            Health = health;
            Position = position;
            LandSpeed = landSpeed;
        }

        /// <summary>Gets the participant's stable identity and controlling side.</summary>
        public CreatureState Creature { get; }

        /// <summary>Gets the participant's health at registration time.</summary>
        public HealthState Health { get; }

        /// <summary>Gets the participant's grid position at registration time.</summary>
        public GridPosition Position { get; }

        /// <summary>Gets the participant's authoritative land Speed.</summary>
        public GridDistance LandSpeed { get; }

        /// <summary>Gets immutable build-time inputs owned by the rules store.</summary>
        public PreparedCreatureInputs PreparedInputs { get; }

        /// <summary>Gets participant-owned initial spell-slot pools.</summary>
        public IReadOnlyList<SpellSlotState> SpellSlots { get; }

        /// <summary>Gets participant-owned rule bindings activated by registration.</summary>
        public IReadOnlyList<ActiveRuleBinding> RuleBindings { get; }

        private static IReadOnlyList<SpellSlotState> CopyOwned(
            IReadOnlyList<SpellSlotState> values,
            CreatureId owner
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value.Owner != owner))
                throw new ArgumentException(
                    "Every spell-slot pool must be owned by the combatant."
                );
            if (values.Select(value => value.Id).Distinct().Count() != values.Count)
                throw new ArgumentException("Spell-slot pool IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }

        private static IReadOnlyList<ActiveRuleBinding> CopyOwned(
            IReadOnlyList<ActiveRuleBinding> values,
            CreatureId owner
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value == null || value.Owner != owner))
                throw new ArgumentException("Every rule binding must be owned by the combatant.");
            if (values.Select(value => value.Id).Distinct().Count() != values.Count)
                throw new ArgumentException("Rule binding IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }
    }
}
