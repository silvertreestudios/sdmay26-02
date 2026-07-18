using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Stores one creature's immutable base check values and snapshot-owned modifier inputs.
    /// </summary>
    /// <remarks>
    /// This state is plain data supplied by an adapter. It does not discover Unity components or
    /// perform rolls. Active rules can add situational values through modifier-collection
    /// middleware without mutating or mirroring this base state.
    /// </remarks>
    public sealed class CreatureStatisticsState : IEquatable<CreatureStatisticsState>
    {
        private readonly IReadOnlyDictionary<Skill, int> skillModifiers;
        private readonly IReadOnlyList<Modifier> modifiers;

        /// <summary>
        /// Gets the creature whose statistics these values describe.
        /// </summary>
        public CreatureId Creature { get; }

        /// <summary>
        /// Gets the creature's base attack modifier before current rule modifiers.
        /// </summary>
        public int AttackModifier { get; }

        /// <summary>
        /// Gets the creature's base Armor Class before current rule modifiers.
        /// </summary>
        public int ArmorClass { get; }

        /// <summary>
        /// Gets the creature's base Fortitude saving throw modifier.
        /// </summary>
        public int FortitudeModifier { get; }

        /// <summary>
        /// Gets the creature's base Reflex saving throw modifier.
        /// </summary>
        public int ReflexModifier { get; }

        /// <summary>
        /// Gets the creature's base Will saving throw modifier.
        /// </summary>
        public int WillModifier { get; }

        /// <summary>
        /// Gets the explicit skill modifiers keyed by open skill identity.
        /// </summary>
        public IReadOnlyDictionary<Skill, int> SkillModifiers => skillModifiers;

        /// <summary>
        /// Gets the snapshot-owned normalized modifiers that currently affect this creature.
        /// </summary>
        public IReadOnlyList<Modifier> Modifiers => modifiers;

        /// <summary>
        /// Creates a complete immutable statistics state for one creature.
        /// </summary>
        /// <param name="creature">The stable creature identity.</param>
        /// <param name="attackModifier">The base attack-roll modifier.</param>
        /// <param name="armorClass">The non-negative base Armor Class.</param>
        /// <param name="fortitudeModifier">The base Fortitude modifier.</param>
        /// <param name="reflexModifier">The base Reflex modifier.</param>
        /// <param name="willModifier">The base Will modifier.</param>
        /// <param name="skillModifiers">Explicit skill modifiers; missing skills resolve to zero.</param>
        /// <param name="modifiers">Current normalized modifier inputs owned by the snapshot.</param>
        /// <exception cref="ArgumentException"><paramref name="creature"/> is empty.</exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="skillModifiers"/> or <paramref name="modifiers"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="armorClass"/> is negative.</exception>
        /// <exception cref="ArgumentException">A skill key is empty.</exception>
        public CreatureStatisticsState(
            CreatureId creature,
            int attackModifier,
            int armorClass,
            int fortitudeModifier,
            int reflexModifier,
            int willModifier,
            IReadOnlyDictionary<Skill, int> skillModifiers,
            IEnumerable<Modifier> modifiers)
        {
            if (creature.IsEmpty)
                throw new ArgumentException("A statistics state requires a creature ID.", nameof(creature));
            if (armorClass < 0)
                throw new ArgumentOutOfRangeException(nameof(armorClass));
            if (skillModifiers == null)
                throw new ArgumentNullException(nameof(skillModifiers));
            if (modifiers == null)
                throw new ArgumentNullException(nameof(modifiers));

            Dictionary<Skill, int> copiedSkills = new Dictionary<Skill, int>();
            foreach (KeyValuePair<Skill, int> pair in skillModifiers)
            {
                if (pair.Key.IsEmpty)
                    throw new ArgumentException("Skill modifiers cannot contain an empty skill.", nameof(skillModifiers));
                copiedSkills.Add(pair.Key, pair.Value);
            }

            Creature = creature;
            AttackModifier = attackModifier;
            ArmorClass = armorClass;
            FortitudeModifier = fortitudeModifier;
            ReflexModifier = reflexModifier;
            WillModifier = willModifier;
            this.skillModifiers = new ReadOnlyDictionary<Skill, int>(copiedSkills);
            this.modifiers = Array.AsReadOnly(modifiers.ToArray());
        }

        /// <summary>
        /// Gets a skill's modifier or zero when the statistics state does not define that skill.
        /// </summary>
        /// <param name="skill">The typed skill identity.</param>
        /// <returns>The base skill modifier.</returns>
        /// <exception cref="ArgumentException"><paramref name="skill"/> is empty.</exception>
        public int GetSkillModifier(Skill skill)
        {
            if (skill.IsEmpty)
                throw new ArgumentException("A skill is required.", nameof(skill));
            return skillModifiers.TryGetValue(skill, out int modifier) ? modifier : 0;
        }

        /// <summary>
        /// Gets the modifier for the requested saving throw.
        /// </summary>
        /// <param name="save">The saving throw identity.</param>
        /// <returns>The base saving throw modifier.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="save"/> is undefined.</exception>
        public int GetSaveModifier(SaveKind save)
        {
            switch (save)
            {
                case SaveKind.Fortitude:
                    return FortitudeModifier;
                case SaveKind.Reflex:
                    return ReflexModifier;
                case SaveKind.Will:
                    return WillModifier;
                default:
                    throw new ArgumentOutOfRangeException(nameof(save));
            }
        }

        /// <inheritdoc/>
        public bool Equals(CreatureStatisticsState other) =>
            other != null && Creature == other.Creature && AttackModifier == other.AttackModifier &&
            ArmorClass == other.ArmorClass && FortitudeModifier == other.FortitudeModifier &&
            ReflexModifier == other.ReflexModifier && WillModifier == other.WillModifier &&
            skillModifiers.OrderBy(pair => pair.Key.Slug, StringComparer.Ordinal).SequenceEqual(
                other.skillModifiers.OrderBy(pair => pair.Key.Slug, StringComparer.Ordinal)) &&
            modifiers.SequenceEqual(other.modifiers);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is CreatureStatisticsState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Creature, AttackModifier, ArmorClass, FortitudeModifier,
                ReflexModifier, WillModifier);
    }
}
