using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Provides pure, side-effect-free reads derived from one immutable rules snapshot.
    /// </summary>
    /// <remarks>
    /// Selectors are ordinary services rather than operations because they cannot roll, prompt,
    /// invoke middleware, or change state. Missing seed data is a composition error and therefore
    /// raises an exception instead of silently inventing a gameplay value.
    /// </remarks>
    public interface IRulesSelectors
    {
        /// <summary>
        /// Resolves the creature's base attack value and current attack-roll modifiers.
        /// </summary>
        ModifierCollection GetAttackModifiers(RulesSnapshot snapshot, CreatureId creature);

        /// <summary>
        /// Resolves the creature's selected base skill value and current skill-check modifiers.
        /// </summary>
        ModifierCollection GetSkillCheckModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            Skill skill
        );

        /// <summary>
        /// Resolves the creature's selected base save value and current saving-throw modifiers.
        /// </summary>
        ModifierCollection GetSavingThrowModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            SaveKind save
        );

        /// <summary>
        /// Gets current snapshot-owned candidates for one statistic without adding its base value.
        /// </summary>
        ModifierCollection GetCurrentModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            Statistic statistic
        );

        /// <summary>
        /// Attempts to get current snapshot-owned candidates without requiring a statistics slice.
        /// </summary>
        /// <remarks>
        /// Unity-backed combatants can keep base statistics at the adapter boundary while still
        /// allowing rules-native effects to contribute modifiers when a statistics slice exists.
        /// </remarks>
        bool TryGetCurrentModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            Statistic statistic,
            out ModifierCollection modifiers
        );

        /// <summary>
        /// Resolves Armor Class from the seeded base value and current Armor Class modifiers.
        /// </summary>
        int GetArmorClass(RulesSnapshot snapshot, CreatureId creature);

        /// <summary>
        /// Resolves a saving throw's defensive DC as 10 plus its current modifier total.
        /// </summary>
        /// <exception cref="OverflowException">
        /// The resolved modifier total is outside the range that can produce an integer DC.
        /// </exception>
        int GetSaveDifficultyClass(RulesSnapshot snapshot, CreatureId creature, SaveKind save);

        /// <summary>
        /// Gets the signed normal or agile penalty for a creature's next attack.
        /// </summary>
        int GetMultipleAttackPenalty(RulesSnapshot snapshot, CreatureId creature, bool isAgile);

        /// <summary>
        /// Determines whether two creatures belong to different seeded players or teams.
        /// </summary>
        bool IsEnemy(RulesSnapshot snapshot, CreatureId left, CreatureId right);

        /// <summary>
        /// Measures current horizontal grid distance using PF2e's alternating diagonal cost.
        /// </summary>
        GridDistance Distance(RulesSnapshot snapshot, CreatureId left, CreatureId right);
    }

    /// <summary>
    /// Implements the standard runtime selectors over committed state slices.
    /// </summary>
    public sealed class RulesSelectors : IRulesSelectors
    {
        private static readonly RuleSource BaseStatisticsSource = RuleSource.FromSlug(
            "base-statistics"
        );

        /// <inheritdoc/>
        public ModifierCollection GetAttackModifiers(RulesSnapshot snapshot, CreatureId creature)
        {
            CreatureStatisticsState statistics = RequireStatistics(snapshot, creature);
            return WithBase(statistics, Statistic.AttackRoll, statistics.AttackModifier);
        }

        /// <inheritdoc/>
        public ModifierCollection GetSkillCheckModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            Skill skill
        )
        {
            CreatureStatisticsState statistics = RequireStatistics(snapshot, creature);
            return WithBase(statistics, Statistic.SkillCheck, statistics.GetSkillModifier(skill));
        }

        /// <inheritdoc/>
        public ModifierCollection GetSavingThrowModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            SaveKind save
        )
        {
            CreatureStatisticsState statistics = RequireStatistics(snapshot, creature);
            return WithBase(statistics, StatisticFor(save), statistics.GetSaveModifier(save));
        }

        /// <inheritdoc/>
        public ModifierCollection GetCurrentModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            Statistic statistic
        )
        {
            CreatureStatisticsState statistics = RequireStatistics(snapshot, creature);
            return new ModifierCollection(statistic, statistics.Modifiers);
        }

        /// <inheritdoc/>
        public bool TryGetCurrentModifiers(
            RulesSnapshot snapshot,
            CreatureId creature,
            Statistic statistic,
            out ModifierCollection modifiers
        )
        {
            RequireSnapshot(snapshot);
            RequireCreatureId(creature, nameof(creature));
            if (snapshot.Statistics.TryGet(creature, out CreatureStatisticsState statistics))
            {
                modifiers = new ModifierCollection(statistic, statistics.Modifiers);
                return true;
            }
            modifiers = new ModifierCollection(statistic, Array.Empty<Modifier>());
            return false;
        }

        /// <inheritdoc/>
        public int GetArmorClass(RulesSnapshot snapshot, CreatureId creature)
        {
            CreatureStatisticsState statistics = RequireStatistics(snapshot, creature);
            return WithBase(statistics, Statistic.ArmorClass, statistics.ArmorClass).Total;
        }

        /// <inheritdoc/>
        public int GetSaveDifficultyClass(
            RulesSnapshot snapshot,
            CreatureId creature,
            SaveKind save
        ) => checked(10 + GetSavingThrowModifiers(snapshot, creature, save).Total);

        /// <inheritdoc/>
        public int GetMultipleAttackPenalty(
            RulesSnapshot snapshot,
            CreatureId creature,
            bool isAgile
        )
        {
            RequireSnapshot(snapshot);
            RequireCreatureId(creature, nameof(creature));
            if (
                !snapshot.MultipleAttackPenalty.TryGet(
                    creature,
                    out MultipleAttackPenaltyState state
                )
            )
            {
                throw new KeyNotFoundException(
                    $"Creature {creature} has no seeded multiple attack penalty state."
                );
            }
            return MultipleAttackPenaltyResolver.Resolve(state.AttackCount, isAgile);
        }

        /// <inheritdoc/>
        public bool IsEnemy(RulesSnapshot snapshot, CreatureId left, CreatureId right)
        {
            RequireSnapshot(snapshot);
            CreatureState leftState = RequireCreature(snapshot, left);
            CreatureState rightState = RequireCreature(snapshot, right);
            return leftState.Player != rightState.Player;
        }

        /// <inheritdoc/>
        public GridDistance Distance(RulesSnapshot snapshot, CreatureId left, CreatureId right)
        {
            RequireSnapshot(snapshot);
            GridPosition leftPosition = RequirePosition(snapshot, left);
            GridPosition rightPosition = RequirePosition(snapshot, right);
            int deltaX = Math.Abs(leftPosition.X - rightPosition.X);
            int deltaZ = Math.Abs(leftPosition.Z - rightPosition.Z);
            int diagonals = Math.Min(deltaX, deltaZ);
            int straight = Math.Max(deltaX, deltaZ) - diagonals;
            int diagonalFeet = (diagonals / 2) * 15 + (diagonals % 2) * 5;
            return new GridDistance(diagonalFeet + straight * 5);
        }

        private static ModifierCollection WithBase(
            CreatureStatisticsState statistics,
            Statistic statistic,
            int baseValue
        ) =>
            new ModifierCollection(
                statistic,
                new[] { Modifier.Untyped(baseValue, BaseStatisticsSource, statistic) }.Concat(
                    statistics.Modifiers
                )
            );

        private static Statistic StatisticFor(SaveKind save)
        {
            switch (save)
            {
                case SaveKind.Fortitude:
                    return Statistic.FortitudeSave;
                case SaveKind.Reflex:
                    return Statistic.ReflexSave;
                case SaveKind.Will:
                    return Statistic.WillSave;
                default:
                    throw new ArgumentOutOfRangeException(nameof(save));
            }
        }

        private static CreatureStatisticsState RequireStatistics(
            RulesSnapshot snapshot,
            CreatureId creature
        )
        {
            RequireSnapshot(snapshot);
            RequireCreatureId(creature, nameof(creature));
            if (!snapshot.Statistics.TryGet(creature, out CreatureStatisticsState statistics))
            {
                throw new KeyNotFoundException(
                    $"Creature {creature} has no seeded statistics state."
                );
            }
            return statistics;
        }

        private static CreatureState RequireCreature(RulesSnapshot snapshot, CreatureId creature)
        {
            RequireCreatureId(creature, nameof(creature));
            if (!snapshot.Creatures.TryGet(creature, out CreatureState state))
                throw new KeyNotFoundException(
                    $"Creature {creature} is not in the rules snapshot."
                );
            return state;
        }

        private static GridPosition RequirePosition(RulesSnapshot snapshot, CreatureId creature)
        {
            RequireCreatureId(creature, nameof(creature));
            if (!snapshot.Positions.TryGet(creature, out GridPosition position))
                throw new KeyNotFoundException($"Creature {creature} has no seeded grid position.");
            return position;
        }

        private static void RequireSnapshot(RulesSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
        }

        private static void RequireCreatureId(CreatureId creature, string parameterName)
        {
            if (creature.IsEmpty)
                throw new ArgumentException("A creature ID is required.", parameterName);
        }
    }
}
