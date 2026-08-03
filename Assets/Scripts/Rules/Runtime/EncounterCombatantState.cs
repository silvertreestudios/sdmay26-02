using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Provides the complete initial rules state for one encounter participant.</summary>
    public sealed class CombatantRulesState : IEquatable<CombatantRulesState>
    {
        private static readonly IEqualityComparer<ActiveEffectRegistration> ActiveEffectReceiptComparer =
            StructuralActiveEffectRegistrationComparer.Instance;

        /// <summary>Creates one immutable participant registration.</summary>
        public CombatantRulesState(
            CreatureState creature,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            CreatureStatisticsState statistics,
            PreparedCreatureInputs preparedInputs,
            IReadOnlyList<SpellSlotState> spellSlots,
            IReadOnlyList<ActiveRuleBinding> ruleBindings
        )
            : this(
                creature,
                health,
                position,
                landSpeed,
                statistics,
                preparedInputs,
                spellSlots,
                ruleBindings,
                Array.Empty<ActiveEffectRegistration>()
            ) { }

        /// <summary>
        /// Creates one immutable participant registration with active effects adopted atomically
        /// alongside the participant's base state.
        /// </summary>
        /// <param name="creature">The participant's stable identity and controlling side.</param>
        /// <param name="health">The participant's health at registration time.</param>
        /// <param name="position">The participant's grid position at registration time.</param>
        /// <param name="landSpeed">The participant's authoritative land Speed.</param>
        /// <param name="statistics">The participant's immutable base statistics.</param>
        /// <param name="preparedInputs">The participant's immutable prepared rules inputs.</param>
        /// <param name="spellSlots">The participant-owned initial spell-slot pools.</param>
        /// <param name="ruleBindings">The participant-owned initial rule bindings.</param>
        /// <param name="activeEffects">
        /// The prepared active effects and bindings adopted with the participant.
        /// </param>
        public CombatantRulesState(
            CreatureState creature,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            CreatureStatisticsState statistics,
            PreparedCreatureInputs preparedInputs,
            IReadOnlyList<SpellSlotState> spellSlots,
            IReadOnlyList<ActiveRuleBinding> ruleBindings,
            IReadOnlyList<ActiveEffectRegistration> activeEffects
        )
        {
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            PreparedInputs =
                preparedInputs ?? throw new ArgumentNullException(nameof(preparedInputs));
            Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            if (Statistics.Creature != creature.Id)
                throw new ArgumentException(
                    "The statistics state must be owned by the combatant.",
                    nameof(statistics)
                );
            SpellSlots = CopyOwned(spellSlots, creature.Id);
            RuleBindings = CopyOwned(ruleBindings, creature.Id);
            ActiveEffects = CopyOwned(activeEffects, creature.Id, RuleBindings);
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

        /// <summary>Gets immutable base check values and snapshot-owned modifier inputs.</summary>
        public CreatureStatisticsState Statistics { get; }

        /// <summary>Gets immutable build-time inputs owned by the rules store.</summary>
        public PreparedCreatureInputs PreparedInputs { get; }

        /// <summary>Gets participant-owned initial spell-slot pools.</summary>
        public IReadOnlyList<SpellSlotState> SpellSlots { get; }

        /// <summary>Gets participant-owned rule bindings activated by registration.</summary>
        public IReadOnlyList<ActiveRuleBinding> RuleBindings { get; }

        /// <summary>Gets prepared active effects adopted atomically with participant registration.</summary>
        public IReadOnlyList<ActiveEffectRegistration> ActiveEffects { get; }

        /// <inheritdoc/>
        public bool Equals(CombatantRulesState other) =>
            other != null
            && Creature.Equals(other.Creature)
            && Health.Equals(other.Health)
            && Position.Equals(other.Position)
            && LandSpeed.Equals(other.LandSpeed)
            && Statistics.Equals(other.Statistics)
            && PreparedInputs.Equals(other.PreparedInputs)
            && SpellSlots.SequenceEqual(other.SpellSlots)
            && RuleBindings.SequenceEqual(other.RuleBindings)
            && ActiveEffects.SequenceEqual(other.ActiveEffects, ActiveEffectReceiptComparer);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is CombatantRulesState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(Creature);
            hash.Add(Health);
            hash.Add(Position);
            hash.Add(LandSpeed);
            hash.Add(Statistics);
            hash.Add(PreparedInputs);
            foreach (SpellSlotState slot in SpellSlots)
                hash.Add(slot);
            foreach (ActiveRuleBinding binding in RuleBindings)
                hash.Add(binding);
            foreach (ActiveEffectRegistration registration in ActiveEffects)
                hash.Add(registration, ActiveEffectReceiptComparer);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Compares active-effect registrations structurally only when they form an immutable
        /// reinforcement receipt; the public registration type retains reference equality.
        /// </summary>
        internal sealed class StructuralActiveEffectRegistrationComparer
            : IEqualityComparer<ActiveEffectRegistration>
        {
            internal static StructuralActiveEffectRegistrationComparer Instance { get; } = new();

            /// <inheritdoc/>
            public bool Equals(ActiveEffectRegistration left, ActiveEffectRegistration right)
            {
                if (ReferenceEquals(left, right))
                    return true;
                if (left == null || right == null)
                    return false;
                return ActiveEffectInstanceExactEquality.Equals(left.Effect, right.Effect)
                    && object.Equals(left.Binding, right.Binding)
                    && object.Equals(left.Timing, right.Timing);
            }

            /// <inheritdoc/>
            public int GetHashCode(ActiveEffectRegistration value) =>
                value == null
                    ? 0
                    : HashCode.Combine(
                        ActiveEffectInstanceExactEquality.GetHashCode(value.Effect),
                        value.Binding,
                        value.Timing
                    );
        }

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

        private static IReadOnlyList<ActiveEffectRegistration> CopyOwned(
            IReadOnlyList<ActiveEffectRegistration> values,
            CreatureId owner,
            IReadOnlyList<ActiveRuleBinding> baseBindings
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            ActiveEffectRegistration[] copied = values.ToArray();
            if (copied.Any(value => value == null || value.Binding.Owner != owner))
                throw new ArgumentException(
                    "Every active-effect binding must be owned by the combatant."
                );
            if (copied.Select(value => value.Effect.Id).Distinct().Count() != copied.Length)
                throw new ArgumentException("Active-effect IDs must be unique.");
            if (copied.Select(value => value.Binding.Id).Distinct().Count() != copied.Length)
                throw new ArgumentException("Active-effect binding IDs must be unique.");
            HashSet<BindingId> bindingIds = new HashSet<BindingId>(
                baseBindings.Select(value => value.Id)
            );
            if (copied.Any(value => !bindingIds.Add(value.Binding.Id)))
                throw new ArgumentException("Base and active-effect binding IDs must be unique.");
            return Array.AsReadOnly(copied);
        }
    }
}
