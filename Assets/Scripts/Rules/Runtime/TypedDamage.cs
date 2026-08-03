using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Describes one immutable typed dice contribution to damage.</summary>
    public sealed class TypedDamageDice
    {
        /// <summary>Creates a typed dice contribution with presentation provenance.</summary>
        /// <param name="dice">The positive dice expression to roll.</param>
        /// <param name="damageType">The non-empty damage type slug.</param>
        /// <param name="source">The non-empty rule or item source label.</param>
        public TypedDamageDice(DiceExpression dice, string damageType, string source)
        {
            if (dice.IsEmpty)
                throw new ArgumentException("Damage dice are required.", nameof(dice));
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("A damage type is required.", nameof(damageType));
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A damage source is required.", nameof(source));
            Dice = dice;
            DamageType = damageType.Trim();
            Source = source.Trim();
        }

        /// <summary>Gets the dice expression rolled for this contribution.</summary>
        public DiceExpression Dice { get; }

        /// <summary>Gets the damage type.</summary>
        public string DamageType { get; }

        /// <summary>Gets the rule or item source label.</summary>
        public string Source { get; }
    }

    /// <summary>Describes one immutable typed flat contribution to damage.</summary>
    public sealed class TypedFlatDamage
    {
        /// <summary>Creates a flat damage contribution with presentation provenance.</summary>
        /// <param name="amount">The signed damage amount.</param>
        /// <param name="damageType">The non-empty damage type slug.</param>
        /// <param name="source">The non-empty rule or item source label.</param>
        public TypedFlatDamage(int amount, string damageType, string source)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("A damage type is required.", nameof(damageType));
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A damage source is required.", nameof(source));
            Amount = amount;
            DamageType = damageType.Trim();
            Source = source.Trim();
        }

        /// <summary>Gets the signed damage amount.</summary>
        public int Amount { get; }

        /// <summary>Gets the damage type.</summary>
        public string DamageType { get; }

        /// <summary>Gets the rule or item source label.</summary>
        public string Source { get; }
    }

    /// <summary>Describes one typed weakness or resistance.</summary>
    public sealed class TypedDefenseAdjustment
    {
        /// <summary>Creates a non-negative typed defense adjustment.</summary>
        /// <param name="damageType">The non-empty damage type slug.</param>
        /// <param name="amount">The non-negative adjustment amount.</param>
        public TypedDefenseAdjustment(string damageType, int amount)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("A damage type is required.", nameof(damageType));
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            DamageType = damageType.Trim();
            Amount = amount;
        }

        /// <summary>Gets the damage type matched case-insensitively.</summary>
        public string DamageType { get; }

        /// <summary>Gets the non-negative adjustment amount.</summary>
        public int Amount { get; }
    }

    /// <summary>Describes one exact damage-type immunity.</summary>
    public sealed class TypedDamageImmunity
    {
        /// <summary>Creates a normalized damage-type immunity.</summary>
        /// <param name="damageType">The non-empty damage type slug.</param>
        public TypedDamageImmunity(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("A damage type is required.", nameof(damageType));
            DamageType = damageType.Trim();
        }

        /// <summary>Gets the damage type matched case-insensitively.</summary>
        public string DamageType { get; }
    }

    /// <summary>Contains one final typed damage amount and its contributing sources.</summary>
    public sealed class TypedDamagePart
    {
        private readonly IReadOnlyList<string> sources;

        internal TypedDamagePart(string damageType, int amount, IEnumerable<string> sources)
        {
            DamageType = damageType;
            Amount = amount;
            this.sources = new ReadOnlyCollection<string>(sources.Distinct().ToArray());
        }

        /// <summary>Gets the damage type.</summary>
        public string DamageType { get; }

        /// <summary>Gets the final non-negative amount.</summary>
        public int Amount { get; }

        /// <summary>Gets the rule and item labels that contributed to this damage type.</summary>
        public IReadOnlyList<string> Sources => sources;
    }

    /// <summary>
    /// Resolves deterministic typed damage shared by weapon Strikes and spell attacks.
    /// </summary>
    internal static class TypedDamageResolver
    {
        public static IReadOnlyList<TypedDamagePart> Resolve(
            IEnumerable<TypedDamageDice> dice,
            IEnumerable<TypedFlatDamage> flatDamage,
            IEnumerable<TypedDamageDice> criticalOnlyDice,
            DegreeOfSuccess degree,
            IEnumerable<TypedDamageImmunity> immunities,
            IEnumerable<TypedDefenseAdjustment> weaknesses,
            IEnumerable<TypedDefenseAdjustment> resistances,
            IRollService rolls
        )
        {
            if (dice == null)
                throw new ArgumentNullException(nameof(dice));
            if (flatDamage == null)
                throw new ArgumentNullException(nameof(flatDamage));
            if (criticalOnlyDice == null)
                throw new ArgumentNullException(nameof(criticalOnlyDice));
            if (immunities == null)
                throw new ArgumentNullException(nameof(immunities));
            if (weaknesses == null)
                throw new ArgumentNullException(nameof(weaknesses));
            if (resistances == null)
                throw new ArgumentNullException(nameof(resistances));
            if (rolls == null)
                throw new ArgumentNullException(nameof(rolls));
            if (degree != DegreeOfSuccess.Success && degree != DegreeOfSuccess.CriticalSuccess)
                return Array.Empty<TypedDamagePart>();

            return ScaleAndDefend(
                Roll(dice, flatDamage, rolls),
                Roll(
                    degree == DegreeOfSuccess.CriticalSuccess
                        ? criticalOnlyDice
                        : Array.Empty<TypedDamageDice>(),
                    Array.Empty<TypedFlatDamage>(),
                    rolls
                ),
                degree == DegreeOfSuccess.CriticalSuccess ? 2 : 1,
                1,
                immunities,
                weaknesses,
                resistances
            );
        }

        internal static IReadOnlyList<TypedDamagePart> ResolveBasicSave(
            IEnumerable<TypedDamageDice> dice,
            DegreeOfSuccess degree,
            IEnumerable<TypedDamageImmunity> immunities,
            IEnumerable<TypedDefenseAdjustment> weaknesses,
            IEnumerable<TypedDefenseAdjustment> resistances,
            IRollService rolls
        )
        {
            return ResolveBasicSave(
                Roll(dice, Array.Empty<TypedFlatDamage>(), rolls),
                degree,
                immunities,
                weaknesses,
                resistances
            );
        }

        internal static IReadOnlyList<TypedDamagePart> Roll(
            IEnumerable<TypedDamageDice> dice,
            IEnumerable<TypedFlatDamage> flatDamage,
            IRollService rolls
        )
        {
            if (dice == null)
                throw new ArgumentNullException(nameof(dice));
            if (flatDamage == null)
                throw new ArgumentNullException(nameof(flatDamage));
            if (rolls == null)
                throw new ArgumentNullException(nameof(rolls));

            Dictionary<string, DamageGroup> groups = new(StringComparer.OrdinalIgnoreCase);
            foreach (TypedDamageDice component in dice)
                Add(
                    groups,
                    component.DamageType,
                    rolls.Roll(component.Dice).Total,
                    component.Source
                );
            foreach (TypedFlatDamage component in flatDamage)
                Add(groups, component.DamageType, component.Amount, component.Source);
            return ToParts(groups);
        }

        internal static IReadOnlyList<TypedDamagePart> ResolveBasicSave(
            IReadOnlyList<TypedDamagePart> rolled,
            DegreeOfSuccess degree,
            IEnumerable<TypedDamageImmunity> immunities,
            IEnumerable<TypedDefenseAdjustment> weaknesses,
            IEnumerable<TypedDefenseAdjustment> resistances
        )
        {
            if (rolled == null)
                throw new ArgumentNullException(nameof(rolled));
            if (degree == DegreeOfSuccess.CriticalSuccess)
                return Array.Empty<TypedDamagePart>();
            return ScaleAndDefend(
                rolled,
                Array.Empty<TypedDamagePart>(),
                degree == DegreeOfSuccess.CriticalFailure ? 2 : 1,
                degree == DegreeOfSuccess.Success ? 2 : 1,
                immunities,
                weaknesses,
                resistances
            );
        }

        private static IReadOnlyList<TypedDamagePart> ScaleAndDefend(
            IEnumerable<TypedDamagePart> baseDamage,
            IEnumerable<TypedDamagePart> postScaleDamage,
            int multiplier,
            int divisor,
            IEnumerable<TypedDamageImmunity> immunities,
            IEnumerable<TypedDefenseAdjustment> weaknesses,
            IEnumerable<TypedDefenseAdjustment> resistances
        )
        {
            if (baseDamage == null)
                throw new ArgumentNullException(nameof(baseDamage));
            if (postScaleDamage == null)
                throw new ArgumentNullException(nameof(postScaleDamage));
            if (immunities == null)
                throw new ArgumentNullException(nameof(immunities));
            if (weaknesses == null)
                throw new ArgumentNullException(nameof(weaknesses));
            if (resistances == null)
                throw new ArgumentNullException(nameof(resistances));

            Dictionary<string, DamageGroup> groups = new(StringComparer.OrdinalIgnoreCase);
            foreach (TypedDamagePart component in baseDamage)
                Add(groups, component);

            foreach (DamageGroup group in groups.Values)
                group.Amount = checked(group.Amount * multiplier) / divisor;
            foreach (TypedDamagePart component in postScaleDamage)
                Add(groups, component);

            foreach (DamageGroup group in groups.Values)
            {
                if (
                    immunities.Any(value =>
                        string.Equals(
                            value.DamageType,
                            group.DamageType,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    group.Amount = 0;
                    continue;
                }
                TypedDefenseAdjustment weakness = weaknesses.FirstOrDefault(value =>
                    string.Equals(
                        value.DamageType,
                        group.DamageType,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                TypedDefenseAdjustment resistance = resistances.FirstOrDefault(value =>
                    string.Equals(
                        value.DamageType,
                        group.DamageType,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                if (weakness != null)
                    group.Amount = checked(group.Amount + weakness.Amount);
                if (resistance != null)
                    group.Amount -= resistance.Amount;
                group.Amount = Math.Max(0, group.Amount);
            }

            return ToParts(groups);
        }

        private static IReadOnlyList<TypedDamagePart> ToParts(
            IReadOnlyDictionary<string, DamageGroup> groups
        ) =>
            groups
                .Values.Select(group => new TypedDamagePart(
                    group.DamageType,
                    group.Amount,
                    group.Sources
                ))
                .ToArray();

        private static void Add(
            IDictionary<string, DamageGroup> groups,
            string damageType,
            int amount,
            string source
        )
        {
            if (!groups.TryGetValue(damageType, out DamageGroup group))
            {
                group = new DamageGroup(damageType);
                groups.Add(damageType, group);
            }
            group.Amount = checked(group.Amount + amount);
            group.Sources.Add(source);
        }

        private static void Add(IDictionary<string, DamageGroup> groups, TypedDamagePart component)
        {
            if (!groups.TryGetValue(component.DamageType, out DamageGroup group))
            {
                group = new DamageGroup(component.DamageType);
                groups.Add(component.DamageType, group);
            }
            group.Amount = checked(group.Amount + component.Amount);
            group.Sources.AddRange(component.Sources);
        }

        private sealed class DamageGroup
        {
            public DamageGroup(string damageType) => DamageType = damageType;

            public string DamageType { get; }
            public int Amount { get; set; }
            public List<string> Sources { get; } = new();
        }
    }
}
