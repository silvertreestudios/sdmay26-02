using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Carries the remaining encounter-clock schedule for one active effect restored during
    /// combatant enrollment.
    /// </summary>
    /// <remarks>
    /// The containing effect and binding remain the authority for identity, source, duration,
    /// and ordering. This value preserves only the schedule fields that cannot be reconstructed
    /// from the original duration after time has advanced.
    /// </remarks>
    public readonly struct ActiveEffectTimingRestore : IEquatable<ActiveEffectTimingRestore>
    {
        /// <summary>Creates a saved timing schedule for an effect in the enrollment batch.</summary>
        /// <param name="effect">The effect whose schedule is being restored.</param>
        /// <param name="remainingBoundaries">The non-negative future source boundaries remaining.</param>
        /// <param name="expiresWithEncounter">Whether encounter closure expires the effect.</param>
        public ActiveEffectTimingRestore(
            ActiveEffectId effect,
            int remainingBoundaries,
            bool expiresWithEncounter
        )
        {
            if (effect.IsEmpty)
                throw new ArgumentException("An active effect ID is required.", nameof(effect));
            if (remainingBoundaries < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingBoundaries));
            Effect = effect;
            RemainingBoundaries = remainingBoundaries;
            ExpiresWithEncounter = expiresWithEncounter;
        }

        /// <summary>Gets the active effect whose schedule is restored.</summary>
        public ActiveEffectId Effect { get; }

        /// <summary>Gets the remaining future source boundaries.</summary>
        public int RemainingBoundaries { get; }

        /// <summary>Gets whether encounter closure expires the effect.</summary>
        public bool ExpiresWithEncounter { get; }

        /// <inheritdoc/>
        public bool Equals(ActiveEffectTimingRestore other) =>
            Effect == other.Effect
            && RemainingBoundaries == other.RemainingBoundaries
            && ExpiresWithEncounter == other.ExpiresWithEncounter;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is ActiveEffectTimingRestore other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Effect, RemainingBoundaries, ExpiresWithEncounter);
    }

    /// <summary>Provides the complete immutable rules registration for one combatant.</summary>
    public sealed class CombatantRulesState
    {
        /// <summary>Creates one complete combatant registration.</summary>
        /// <param name="creature">The combatant identity and controlling side.</param>
        /// <param name="statistics">The combatant's immutable base statistics.</param>
        /// <param name="health">The combatant's initial authoritative health.</param>
        /// <param name="position">The combatant's initial authoritative grid position.</param>
        /// <param name="landSpeed">The combatant's authoritative land Speed.</param>
        /// <param name="initiativeModifier">The modifier added to the enrollment initiative roll.</param>
        /// <param name="spellSlots">The combatant-owned initial spell-slot pools.</param>
        /// <param name="ruleBindings">The initial feature and restored-effect bindings.</param>
        /// <param name="equipment">The combatant-owned equipment states.</param>
        /// <param name="ammunition">The combatant-owned ammunition pools.</param>
        /// <param name="activeEffects">The active effects restored with the enrollment batch.</param>
        /// <param name="activeEffectTimings">
        /// Remaining encounter-clock schedules for effects whose original duration has advanced.
        /// </param>
        /// <param name="externalEffectReferences">
        /// Stable actor identities referenced by restored effects but not enrolled as combatants.
        /// </param>
        /// <exception cref="ArgumentNullException">A required reference or collection is null.</exception>
        /// <exception cref="ArgumentException">
        /// A collection contains an invalid owner, duplicate identity, or null entry.
        /// </exception>
        public CombatantRulesState(
            CreatureState creature,
            CreatureStatisticsState statistics,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            int initiativeModifier,
            IReadOnlyList<SpellSlotState> spellSlots,
            IReadOnlyList<ActiveRuleBinding> ruleBindings,
            IReadOnlyList<EquipmentState> equipment,
            IReadOnlyList<AmmunitionState> ammunition,
            IReadOnlyList<ActiveEffectInstance> activeEffects,
            IReadOnlyList<ActiveEffectTimingRestore> activeEffectTimings,
            IReadOnlyList<CreatureId> externalEffectReferences
        )
        {
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            if (statistics.Creature != creature.Id)
                throw new ArgumentException(
                    "The statistics state must describe the registered combatant.",
                    nameof(statistics)
                );
            ActiveEffects = CopyEffects(activeEffects);
            ActiveEffectTimings = CopyTimings(activeEffectTimings, ActiveEffects);
            ExternalEffectReferences = CopyExternalReferences(externalEffectReferences);
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

        /// <summary>Gets the participant's immutable base statistics.</summary>
        public CreatureStatisticsState Statistics { get; }

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

        /// <summary>Gets exact remaining schedules restored with active effects.</summary>
        public IReadOnlyList<ActiveEffectTimingRestore> ActiveEffectTimings { get; }

        /// <summary>
        /// Gets stable actor identities used by restored effects without a live encounter actor.
        /// </summary>
        public IReadOnlyList<CreatureId> ExternalEffectReferences { get; }

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

        private static IReadOnlyList<ActiveEffectTimingRestore> CopyTimings(
            IReadOnlyList<ActiveEffectTimingRestore> values,
            IReadOnlyList<ActiveEffectInstance> effects
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            HashSet<ActiveEffectId> effectIds = new(effects.Select(effect => effect.Id));
            if (values.Any(value => !effectIds.Contains(value.Effect)))
                throw new ArgumentException(
                    "Every restored timing must reference an active effect in the same combatant registration."
                );
            if (values.Select(value => value.Effect).Distinct().Count() != values.Count)
                throw new ArgumentException("Restored effect timing IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }

        private static IReadOnlyList<CreatureId> CopyExternalReferences(
            IReadOnlyList<CreatureId> values
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value.IsEmpty) || values.Distinct().Count() != values.Count)
                throw new ArgumentException(
                    "External effect actor references must be complete and unique."
                );
            return Array.AsReadOnly(values.ToArray());
        }
    }
}
