using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules
{
    public enum Pf2eModifierType
    {
        Untyped,
        Circumstance,
        Item,
        Status
    }

    public enum Pf2eStatistic
    {
        AttackRoll,
        ArmorClass,
        FortitudeSave,
        ReflexSave,
        WillSave,
        SkillCheck,
        Initiative,
        DifficultyClass
    }

    public static class Pf2eRuleReferences
    {
        public const string ModifierStacking = "https://2e.aonprd.com/Rules.aspx?ID=2278";
        public const string MultipleAttackPenalty = "https://2e.aonprd.com/Rules.aspx?ID=2288";
        public const string RangePenalty = "https://2e.aonprd.com/Rules.aspx?ID=2288";
        public const string Cover = "https://2e.aonprd.com/Rules.aspx?ID=2372";
        public const string OffGuard = "https://2e.aonprd.com/Conditions.aspx?ID=58";
    }

    public readonly struct Pf2eModifier
    {
        public int Value { get; }
        public Pf2eModifierType Type { get; }
        public string Source { get; }
        public Pf2eStatistic TargetStatistic { get; }
        public string RulesReference { get; }

        public Pf2eModifier(int value, Pf2eModifierType type, string source, Pf2eStatistic targetStatistic, string rulesReference = null)
        {
            Value = value;
            Type = type;
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            TargetStatistic = targetStatistic;
            RulesReference = rulesReference;
        }
    }

    public readonly struct Pf2eModifierResolution
    {
        public int Total { get; }
        public IReadOnlyList<Pf2eModifier> AppliedModifiers { get; }
        public IReadOnlyList<Pf2eModifier> SuppressedModifiers { get; }

        public Pf2eModifierResolution(int total, IReadOnlyList<Pf2eModifier> appliedModifiers, IReadOnlyList<Pf2eModifier> suppressedModifiers)
        {
            Total = total;
            AppliedModifiers = appliedModifiers ?? Array.Empty<Pf2eModifier>();
            SuppressedModifiers = suppressedModifiers ?? Array.Empty<Pf2eModifier>();
        }
    }

    public static class Pf2eModifierResolver
    {
        /// <summary>
        /// PF2e typed modifier stacking: untyped modifiers stack; item, status, and
        /// circumstance modifiers apply only the best bonus and worst penalty of each type.
        /// Source: https://2e.aonprd.com/Rules.aspx?ID=2278
        /// </summary>
        public static Pf2eModifierResolution Resolve(IEnumerable<Pf2eModifier> modifiers, Pf2eStatistic statistic)
        {
            List<Pf2eModifier> relevant = modifiers?
                .Where(modifier => modifier.TargetStatistic == statistic && modifier.Value != 0)
                .ToList() ?? new List<Pf2eModifier>();
            List<Pf2eModifier> applied = new();
            List<Pf2eModifier> suppressed = new();

            foreach (Pf2eModifier modifier in relevant.Where(modifier => modifier.Type == Pf2eModifierType.Untyped))
                applied.Add(modifier);

            foreach (Pf2eModifierType type in new[] { Pf2eModifierType.Circumstance, Pf2eModifierType.Item, Pf2eModifierType.Status })
            {
                ResolveTypedBonuses(relevant, type, applied, suppressed);
                ResolveTypedPenalties(relevant, type, applied, suppressed);
            }

            return new Pf2eModifierResolution(applied.Sum(modifier => modifier.Value), applied, suppressed);
        }

        private static void ResolveTypedBonuses(List<Pf2eModifier> relevant, Pf2eModifierType type, List<Pf2eModifier> applied, List<Pf2eModifier> suppressed)
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

        private static void ResolveTypedPenalties(List<Pf2eModifier> relevant, Pf2eModifierType type, List<Pf2eModifier> applied, List<Pf2eModifier> suppressed)
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
