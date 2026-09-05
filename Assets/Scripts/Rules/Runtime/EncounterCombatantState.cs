using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Provides the complete immutable rules registration for one combatant.</summary>
    public sealed class CombatantRulesState
    {
        /// <summary>Creates one complete combatant registration.</summary>
        /// <param name="creature">The combatant identity and controlling side.</param>
        /// <param name="health">The combatant's initial authoritative health.</param>
        /// <param name="position">The combatant's initial authoritative grid position.</param>
        /// <param name="landSpeed">The combatant's authoritative land Speed.</param>
        /// <param name="initiativeModifier">The modifier added to the enrollment initiative roll.</param>
        /// <param name="spellSlots">The combatant-owned initial spell-slot pools.</param>
        /// <param name="ruleBindings">The initial feature and restored-effect bindings.</param>
        /// <param name="equipment">The combatant-owned equipment states.</param>
        /// <param name="ammunition">The combatant-owned ammunition pools.</param>
        /// <param name="activeEffects">The active effects restored with the enrollment batch.</param>
        /// <exception cref="ArgumentNullException">A required reference or collection is null.</exception>
        /// <exception cref="ArgumentException">
        /// A collection contains an invalid owner, duplicate identity, or null entry.
        /// </exception>
        public CombatantRulesState(
            CreatureState creature,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            int initiativeModifier,
            IReadOnlyList<SpellSlotState> spellSlots,
            IReadOnlyList<ActiveRuleBinding> ruleBindings,
            IReadOnlyList<EquipmentState> equipment,
            IReadOnlyList<AmmunitionState> ammunition,
            IReadOnlyList<ActiveEffectInstance> activeEffects
        )
        {
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            ActiveEffects = CopyEffects(activeEffects);
            SpellSlots = CopyOwned(spellSlots, creature.Id);
            RuleBindings = CopyBindings(ruleBindings, creature.Id, ActiveEffects);
            Equipment = CopyOwned(equipment, creature.Id);
            Ammunition = CopyOwned(ammunition, creature.Id);
            Health = health;
            Position = position;
            LandSpeed = landSpeed;
            InitiativeModifier = initiativeModifier;
        }

        /// <summary>Gets the participant's stable identity and controlling side.</summary>
        public CreatureState Creature { get; }

        /// <summary>Gets the participant's health at registration time.</summary>
        public HealthState Health { get; }

        /// <summary>Gets the participant's grid position at registration time.</summary>
        public GridPosition Position { get; }

        /// <summary>Gets the participant's authoritative land Speed.</summary>
        public GridDistance LandSpeed { get; }

        /// <summary>Gets the initiative modifier captured before enrollment.</summary>
        public int InitiativeModifier { get; }

        /// <summary>Gets participant-owned initial spell-slot pools.</summary>
        public IReadOnlyList<SpellSlotState> SpellSlots { get; }

        /// <summary>Gets participant-owned rule bindings activated by registration.</summary>
        public IReadOnlyList<ActiveRuleBinding> RuleBindings { get; }

        /// <summary>Gets participant-owned equipment state.</summary>
        public IReadOnlyList<EquipmentState> Equipment { get; }

        /// <summary>Gets participant-owned ammunition pools.</summary>
        public IReadOnlyList<AmmunitionState> Ammunition { get; }

        /// <summary>Gets active effects restored with this combatant batch.</summary>
        public IReadOnlyList<ActiveEffectInstance> ActiveEffects { get; }

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

        private static IReadOnlyList<ActiveRuleBinding> CopyBindings(
            IReadOnlyList<ActiveRuleBinding> values,
            CreatureId owner,
            IReadOnlyList<ActiveEffectInstance> effects
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            HashSet<ActiveEffectId> effectIds = new HashSet<ActiveEffectId>(
                effects.Select(effect => effect.Id)
            );
            if (
                values.Any(value =>
                    value == null
                    || (
                        value.Owner != owner
                        && (!value.EffectId.HasValue || !effectIds.Contains(value.EffectId.Value))
                    )
                )
            )
                throw new ArgumentException(
                    "Every rule binding must be owned by the combatant or one of its restored effects."
                );
            if (values.Select(value => value.Id).Distinct().Count() != values.Count)
                throw new ArgumentException("Rule binding IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }

        private static IReadOnlyList<EquipmentState> CopyOwned(
            IReadOnlyList<EquipmentState> values,
            CreatureId owner
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value == null || value.Holder != owner))
                throw new ArgumentException("Every equipment item must be held by the combatant.");
            if (values.Select(value => value.Id).Distinct().Count() != values.Count)
                throw new ArgumentException("Equipment IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }

        private static IReadOnlyList<AmmunitionState> CopyOwned(
            IReadOnlyList<AmmunitionState> values,
            CreatureId owner
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value.Owner != owner))
                throw new ArgumentException(
                    "Every ammunition pool must be owned by the combatant."
                );
            if (values.Select(value => value.Item).Distinct().Count() != values.Count)
                throw new ArgumentException("Ammunition pool IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }

        private static IReadOnlyList<ActiveEffectInstance> CopyEffects(
            IReadOnlyList<ActiveEffectInstance> values
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value == null))
                throw new ArgumentException("Active effects cannot contain null entries.");
            if (values.Select(value => value.Id).Distinct().Count() != values.Count)
                throw new ArgumentException("Active effect IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }
    }
}
