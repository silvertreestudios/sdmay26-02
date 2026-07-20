using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules
{
    /// <summary>
    /// PF2e modifier categories that determine whether bonuses and penalties stack.
    /// </summary>
    public enum Pf2eModifierType
    {
        Untyped,
        Circumstance,
        Item,
        Status,
    }

    /// <summary>
    /// Roll and DC targets currently supported by the shared PF2e modifier resolver.
    /// </summary>
    public enum Pf2eStatistic
    {
        AttackRoll,
        ArmorClass,
        FortitudeSave,
        ReflexSave,
        WillSave,
        SkillCheck,
        Initiative,
        DifficultyClass,
    }

    /// <summary>
    /// A single PF2e modifier emitted by a rule source, item, condition, or roll context.
    /// Keep source-specific behavior in providers and pass only normalized modifiers to the resolver.
    /// </summary>
    public readonly struct Pf2eModifier
    {
        /// <summary>
        /// Signed numeric value applied when this modifier is not suppressed.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// PF2e stacking category for this modifier.
        /// </summary>
        public Pf2eModifierType Type { get; }

        /// <summary>
        /// Short source label used in logs, tests, and modifier audits.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Statistic this modifier is allowed to affect.
        /// </summary>
        public Pf2eStatistic TargetStatistic { get; }

        /// <summary>
        /// Creates a modifier for one target statistic, preserving a readable source name for logs and audits.
        /// </summary>
        /// <param name="value">The signed modifier value.</param>
        /// <param name="type">The PF2e stacking category for this modifier.</param>
        /// <param name="source">A short source label used for diagnostics and combat logs.</param>
        /// <param name="targetStatistic">The statistic this modifier can affect.</param>
        public Pf2eModifier(
            int value,
            Pf2eModifierType type,
            string source,
            Pf2eStatistic targetStatistic
        )
        {
            Value = value;
            Type = type;
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            TargetStatistic = targetStatistic;
        }
    }

    /// <summary>
    /// Result of resolving a statistic's modifiers, including which typed modifiers were suppressed by PF2e stacking.
    /// </summary>
    public readonly struct Pf2eModifierResolution
    {
        /// <summary>
        /// Final total after typed stacking rules are applied.
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// Modifiers that contributed to the final total.
        /// </summary>
        public IReadOnlyList<Pf2eModifier> AppliedModifiers { get; }

        /// <summary>
        /// Modifiers ignored because another same-type bonus or penalty was stronger.
        /// </summary>
        public IReadOnlyList<Pf2eModifier> SuppressedModifiers { get; }

        /// <summary>
        /// Creates a resolved modifier result with immutable applied and suppressed modifier lists.
        /// </summary>
        /// <param name="total">The final modifier total after stacking rules are applied.</param>
        /// <param name="appliedModifiers">Modifiers that contributed to the total.</param>
        /// <param name="suppressedModifiers">Modifiers ignored because another modifier of the same type was stronger.</param>
        public Pf2eModifierResolution(
            int total,
            IReadOnlyList<Pf2eModifier> appliedModifiers,
            IReadOnlyList<Pf2eModifier> suppressedModifiers
        )
        {
            Total = total;
            AppliedModifiers = appliedModifiers ?? Array.Empty<Pf2eModifier>();
            SuppressedModifiers = suppressedModifiers ?? Array.Empty<Pf2eModifier>();
        }
    }

    /// <summary>
    /// Applies PF2e typed modifier stacking to already-collected modifiers for a single statistic.
    /// </summary>
    public static class Pf2eModifierResolver
    {
        /// <summary>
        /// PF2e typed modifier stacking: untyped modifiers stack; item, status, and
        /// circumstance modifiers apply only the best bonus and worst penalty of each type.
        /// Source: https://2e.aonprd.com/Rules.aspx?ID=2278
        /// </summary>
        /// <param name="modifiers">Candidate modifiers from creature providers and the immediate roll context.</param>
        /// <param name="statistic">The statistic being resolved; modifiers for other statistics are ignored.</param>
        /// <returns>The resolved total with applied and suppressed modifier details.</returns>
        public static Pf2eModifierResolution Resolve(
            IEnumerable<Pf2eModifier> modifiers,
            Pf2eStatistic statistic
        )
        {
            List<Pf2eModifier> relevant =
                modifiers
                    ?.Where(modifier =>
                        modifier.TargetStatistic == statistic && modifier.Value != 0
                    )
                    .ToList()
                ?? new List<Pf2eModifier>();
            List<Pf2eModifier> applied = new();
            List<Pf2eModifier> suppressed = new();

            foreach (
                Pf2eModifier modifier in relevant.Where(modifier =>
                    modifier.Type == Pf2eModifierType.Untyped
                )
            )
                applied.Add(modifier);

            foreach (
                Pf2eModifierType type in new[]
                {
                    Pf2eModifierType.Circumstance,
                    Pf2eModifierType.Item,
                    Pf2eModifierType.Status,
                }
            )
            {
                ResolveTypedBonuses(relevant, type, applied, suppressed);
                ResolveTypedPenalties(relevant, type, applied, suppressed);
            }

            return new Pf2eModifierResolution(
                applied.Sum(modifier => modifier.Value),
                applied,
                suppressed
            );
        }

        private static void ResolveTypedBonuses(
            List<Pf2eModifier> relevant,
            Pf2eModifierType type,
            List<Pf2eModifier> applied,
            List<Pf2eModifier> suppressed
        )
        {
            List<Pf2eModifier> bonuses = relevant
                .Where(modifier => modifier.Type == type && modifier.Value > 0)
                .OrderByDescending(modifier => modifier.Value)
                .ToList();

            if (bonuses.Count == 0)
                return;

            applied.Add(bonuses[0]);
            for (int i = 1; i < bonuses.Count; i++)
                suppressed.Add(bonuses[i]);
        }

        private static void ResolveTypedPenalties(
            List<Pf2eModifier> relevant,
            Pf2eModifierType type,
            List<Pf2eModifier> applied,
            List<Pf2eModifier> suppressed
        )
        {
            List<Pf2eModifier> penalties = relevant
                .Where(modifier => modifier.Type == type && modifier.Value < 0)
                .OrderBy(modifier => modifier.Value)
                .ToList();

            if (penalties.Count == 0)
                return;

            applied.Add(penalties[0]);
            for (int i = 1; i < penalties.Count; i++)
                suppressed.Add(penalties[i]);
        }
    }
}
