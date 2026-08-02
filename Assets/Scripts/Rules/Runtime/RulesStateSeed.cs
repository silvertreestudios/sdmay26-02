using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    public sealed class RulesStateSeed
    {
        internal Dictionary<CreatureId, CreatureState> Creatures { get; } =
            new Dictionary<CreatureId, CreatureState>();
        internal Dictionary<CreatureId, PreparedCreatureInputs> PreparedInputs { get; } =
            new Dictionary<CreatureId, PreparedCreatureInputs>();
        internal Dictionary<CreatureId, CreatureStatisticsState> Statistics { get; } =
            new Dictionary<CreatureId, CreatureStatisticsState>();
        internal Dictionary<CreatureId, HealthState> Health { get; } =
            new Dictionary<CreatureId, HealthState>();
        internal Dictionary<CreatureId, GridPosition> Positions { get; } =
            new Dictionary<CreatureId, GridPosition>();
        internal Dictionary<CreatureId, GridDistance> LandSpeeds { get; } =
            new Dictionary<CreatureId, GridDistance>();
        internal Dictionary<CreatureId, MovementBudgetState> MovementBudgets { get; } =
            new Dictionary<CreatureId, MovementBudgetState>();
        internal Dictionary<CreatureId, ActionEconomyState> ActionEconomy { get; } =
            new Dictionary<CreatureId, ActionEconomyState>();
        internal Dictionary<SpellSlotPoolId, SpellSlotState> SpellSlots { get; } =
            new Dictionary<SpellSlotPoolId, SpellSlotState>();
        internal Dictionary<CreatureId, FocusPointState> FocusPoints { get; } =
            new Dictionary<CreatureId, FocusPointState>();
        internal Dictionary<ItemId, AmmunitionState> Ammunition { get; } =
            new Dictionary<ItemId, AmmunitionState>();
        internal Dictionary<CreatureId, MultipleAttackPenaltyState> MultipleAttackPenalty { get; } =
            new Dictionary<CreatureId, MultipleAttackPenaltyState>();
        internal Dictionary<ItemId, EquipmentState> Equipment { get; } =
            new Dictionary<ItemId, EquipmentState>();
        internal Dictionary<ActiveEffectId, ActiveEffectInstance> ActiveEffects { get; } =
            new Dictionary<ActiveEffectId, ActiveEffectInstance>();
        internal Dictionary<BindingId, ActiveRuleBinding> RuleBindings { get; } =
            new Dictionary<BindingId, ActiveRuleBinding>();
        internal Dictionary<BindingId, long> StatelessRuleBindingGenerations { get; } =
            new Dictionary<BindingId, long>();
        internal Dictionary<BindingId, FrequencyState> Frequencies { get; } =
            new Dictionary<BindingId, FrequencyState>();
        internal Dictionary<EncounterId, EncounterState> Encounters { get; } =
            new Dictionary<EncounterId, EncounterState>();
        internal Dictionary<ActiveEffectId, ActiveEffectTimingState> ActiveEffectTimings { get; } =
            new Dictionary<ActiveEffectId, ActiveEffectTimingState>();

        /// <summary>Seeds an authoritative encounter state for deterministic fixtures.</summary>
        /// <param name="value">The complete immutable encounter state.</param>
        /// <returns>This seed so deterministic fixture composition can continue.</returns>
        public RulesStateSeed SeedEncounter(EncounterState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Encounters[value.Id] = value;
            return this;
        }

        /// <summary>Seeds an active-effect timing schedule for deterministic fixtures.</summary>
        /// <param name="value">The complete effect timing schedule.</param>
        /// <returns>This seed so deterministic fixture composition can continue.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is <see langword="null"/>.
        /// </exception>
        public RulesStateSeed SeedActiveEffectTiming(ActiveEffectTimingState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            ActiveEffectTimings[value.Effect] = value;
            return this;
        }

        /// <summary>
        /// Adds one active-effect timing schedule without replacing an existing schedule.
        /// </summary>
        /// <param name="value">The complete effect timing schedule to add by effect identity.</param>
        /// <returns>This seed so strict initial-state composition can continue fluently.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A timing schedule with the same effect identity was already added.
        /// </exception>
        public RulesStateSeed AddUniqueActiveEffectTiming(ActiveEffectTimingState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (!ActiveEffectTimings.TryAdd(value.Effect, value))
                throw new InvalidOperationException(
                    $"Active-effect timing {value.Effect.Value} is already seeded."
                );
            return this;
        }

        public RulesStateSeed SeedCreature(CreatureState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Creatures[value.Id] = value;
            return this;
        }

        /// <summary>Seeds immutable compiled inputs for one creature.</summary>
        public RulesStateSeed SeedPreparedInputs(CreatureId creature, PreparedCreatureInputs value)
        {
            RequireCreatureId(creature, nameof(creature));
            PreparedInputs[creature] = value ?? throw new ArgumentNullException(nameof(value));
            return this;
        }

        /// <summary>
        /// Seeds one creature's base check values and snapshot-owned modifier inputs.
        /// </summary>
        /// <param name="value">The complete immutable statistics state.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public RulesStateSeed SeedStatistics(CreatureStatisticsState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Statistics[value.Creature] = value;
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

        /// <summary>Seeds one creature's authoritative land Speed.</summary>
        /// <param name="creature">The creature that owns the Speed.</param>
        /// <param name="value">The non-negative land Speed used by movement actions.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        public RulesStateSeed SeedLandSpeed(CreatureId creature, GridDistance value)
        {
            RequireCreatureId(creature, nameof(creature));
            LandSpeeds[creature] = value;
            return this;
        }

        /// <summary>
        /// Seeds one creature's current movement allowance and turn-persistent diagonal phase.
        /// </summary>
        /// <param name="creature">The creature that owns the movement budget.</param>
        /// <param name="value">The immutable movement budget state.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        public RulesStateSeed SeedMovementBudget(CreatureId creature, MovementBudgetState value)
        {
            RequireCreatureId(creature, nameof(creature));
            if (value.Owner != creature)
                throw new ArgumentException(
                    "A movement budget must be keyed by its owning creature.",
                    nameof(value)
                );
            MovementBudgets[creature] = value;
            return this;
        }

        public RulesStateSeed SeedActionEconomy(CreatureId creature, ActionEconomyState value)
        {
            RequireCreatureId(creature, nameof(creature));
            ActionEconomy[creature] = value;
            return this;
        }

        /// <summary>
        /// Seeds one authoritative spell-slot pool before the store begins resolving operations.
        /// </summary>
        /// <param name="value">The immutable pool state to add or replace by ID.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        public RulesStateSeed SeedSpellSlot(SpellSlotState value)
        {
            if (value.Id.IsEmpty)
                throw new ArgumentException("A spell-slot pool ID is required.", nameof(value));
            SpellSlots[value.Id] = value;
            return this;
        }

        /// <summary>
        /// Seeds one creature's authoritative Focus Point pool.
        /// </summary>
        /// <param name="creature">The creature that owns the pool.</param>
        /// <param name="value">The immutable Focus Point state.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        public RulesStateSeed SeedFocusPoints(CreatureId creature, FocusPointState value)
        {
            RequireCreatureId(creature, nameof(creature));
            FocusPoints[creature] = value;
            return this;
        }

        /// <summary>
        /// Seeds one authoritative ammunition pool before resolution begins.
        /// </summary>
        /// <param name="value">The immutable ammunition state to add or replace by item ID.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        public RulesStateSeed SeedAmmunition(AmmunitionState value)
        {
            if (value.Item.IsEmpty)
                throw new ArgumentException("An ammunition item ID is required.", nameof(value));
            Ammunition[value.Item] = value;
            return this;
        }

        public RulesStateSeed SeedMultipleAttackPenalty(
            CreatureId creature,
            MultipleAttackPenaltyState value
        )
        {
            RequireCreatureId(creature, nameof(creature));
            MultipleAttackPenalty[creature] = value;
            return this;
        }

        public RulesStateSeed SeedEquipment(EquipmentState value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            Equipment[value.Id] = value;
            return this;
        }

        /// <summary>
        /// Seeds one immutable active-effect instance before the store begins resolving operations.
        /// </summary>
        /// <param name="value">The complete effect instance to add or replace by ID.</param>
        /// <returns>This seed so initial state can be composed fluently.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public RulesStateSeed SeedActiveEffect(ActiveEffectInstance value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            ActiveEffects[value.Id] = value;
            return this;
        }

        /// <summary>
        /// Adds one active-effect instance without replacing an existing effect.
        /// </summary>
        /// <param name="value">The complete effect instance to add by ID.</param>
        /// <returns>This seed so strict initial-state composition can continue fluently.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// An active effect with the same identity was already added.
        /// </exception>
        public RulesStateSeed AddUniqueActiveEffect(ActiveEffectInstance value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (!ActiveEffects.TryAdd(value.Id, value))
                throw new InvalidOperationException(
                    $"Active effect {value.Id.Value} is already seeded."
                );
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
            if (!value.EffectId.HasValue)
            {
                if (
                    StatelessRuleBindingGenerations.TryGetValue(
                        value.Id,
                        out long existingGeneration
                    )
                    && value.CreationOrder < existingGeneration
                )
                {
                    throw new ArgumentException(
                        $"Stateless binding {value.Id.Value} generation {value.CreationOrder} is older than seeded generation {existingGeneration}.",
                        nameof(value)
                    );
                }
                StatelessRuleBindingGenerations[value.Id] = value.CreationOrder;
            }
            RuleBindings[value.Id] = value;
            return this;
        }

        /// <summary>
        /// Seeds the latest committed generation for a stateless binding ID without making that
        /// binding participate.
        /// </summary>
        /// <param name="binding">The stable stateless binding ID whose history is restored.</param>
        /// <param name="generation">The non-negative highest committed creation order.</param>
        /// <returns>This seed so deterministic state restoration can continue.</returns>
        public RulesStateSeed SeedStatelessRuleBindingGeneration(BindingId binding, long generation)
        {
            if (binding.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(binding));
            if (generation < 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (
                StatelessRuleBindingGenerations.TryGetValue(binding, out long existingGeneration)
                && generation < existingGeneration
            )
            {
                throw new ArgumentException(
                    $"Stateless binding {binding.Value} generation history cannot move backward.",
                    nameof(generation)
                );
            }
            if (
                RuleBindings.TryGetValue(binding, out ActiveRuleBinding active)
                && !active.EffectId.HasValue
                && generation != active.CreationOrder
            )
            {
                throw new ArgumentException(
                    $"Stateless binding {binding.Value} history must match its active generation.",
                    nameof(generation)
                );
            }
            StatelessRuleBindingGenerations[binding] = generation;
            return this;
        }

        /// <summary>
        /// Adds one active or disabled rule binding without replacing an existing binding.
        /// </summary>
        /// <param name="value">The immutable binding to add by ID.</param>
        /// <returns>This seed so strict initial-state composition can continue fluently.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A rule binding with the same identity was already added.
        /// </exception>
        public RulesStateSeed AddUniqueRuleBinding(ActiveRuleBinding value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (!RuleBindings.TryAdd(value.Id, value))
                throw new InvalidOperationException(
                    $"Rule binding {value.Id.Value} is already seeded."
                );
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
}
