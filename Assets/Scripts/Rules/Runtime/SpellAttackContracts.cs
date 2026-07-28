using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Describes the structural target required by one spell attack.</summary>
    public abstract class SpellAttackTarget
    {
        private protected SpellAttackTarget() { }
    }

    /// <summary>Requires exactly one creature within a fixed range measured in feet.</summary>
    public sealed class OneCreatureSpellAttackTarget : SpellAttackTarget
    {
        /// <summary>Creates a one-creature target requirement.</summary>
        /// <param name="rangeFeet">The positive maximum range in feet.</param>
        public OneCreatureSpellAttackTarget(int rangeFeet)
        {
            if (rangeFeet <= 0)
                throw new ArgumentOutOfRangeException(nameof(rangeFeet));
            RangeFeet = rangeFeet;
        }

        /// <summary>Gets the maximum target range in feet.</summary>
        public int RangeFeet { get; }
    }

    /// <summary>Defines one immediate typed damage roll made by a spell attack.</summary>
    public sealed class SpellAttackDamageComponent
    {
        /// <summary>Creates an immutable dice-based damage component.</summary>
        /// <param name="dice">The positive number of dice.</param>
        /// <param name="sides">The positive number of sides on each die.</param>
        /// <param name="damageType">The non-empty damage type slug.</param>
        public SpellAttackDamageComponent(int dice, int sides, string damageType)
        {
            if (dice <= 0)
                throw new ArgumentOutOfRangeException(nameof(dice));
            if (sides <= 0)
                throw new ArgumentOutOfRangeException(nameof(sides));
            if (string.IsNullOrWhiteSpace(damageType))
                throw new ArgumentException("A damage type is required.", nameof(damageType));
            Dice = dice;
            Sides = sides;
            DamageType = damageType.Trim();
        }

        /// <summary>Gets the number of dice rolled.</summary>
        public int Dice { get; }

        /// <summary>Gets the number of sides on each die.</summary>
        public int Sides { get; }

        /// <summary>Gets the typed damage slug.</summary>
        public string DamageType { get; }
    }

    /// <summary>Defines one AC spell attack entirely from validated spell data.</summary>
    public sealed class SpellAttackDefinition
    {
        private readonly IReadOnlyList<SpellAttackDamageComponent> damage;

        /// <summary>Creates a spell attack against Armor Class.</summary>
        /// <param name="target">The structural target requirement.</param>
        /// <param name="damage">Immediate typed dice damage resolved on a hit.</param>
        public SpellAttackDefinition(
            SpellAttackTarget target,
            IEnumerable<SpellAttackDamageComponent> damage
        )
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (damage == null)
                throw new ArgumentNullException(nameof(damage));
            SpellAttackDamageComponent[] copied = damage.ToArray();
            if (copied.Length == 0 || copied.Any(component => component == null))
                throw new ArgumentException(
                    "A spell attack requires non-null damage components.",
                    nameof(damage)
                );
            this.damage = Array.AsReadOnly(copied);
        }

        /// <summary>Gets the structural target requirement.</summary>
        public SpellAttackTarget Target { get; }

        /// <summary>Gets every immediate typed damage component.</summary>
        public IReadOnlyList<SpellAttackDamageComponent> Damage => damage;
    }

    /// <summary>Describes one typed weakness or resistance captured for spell damage.</summary>
    public sealed class SpellAttackDefenseAdjustment
    {
        /// <summary>Creates a non-negative typed defense adjustment.</summary>
        /// <param name="damageType">The non-empty damage type slug.</param>
        /// <param name="amount">The non-negative weakness or resistance amount.</param>
        public SpellAttackDefenseAdjustment(string damageType, int amount)
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

    /// <summary>Contains frozen Unity-bound inputs needed to resolve one legal spell attack.</summary>
    public sealed class SpellAttackResolutionData
    {
        private readonly IReadOnlyList<Modifier> attackModifiers;
        private readonly IReadOnlyList<SpellAttackDefenseAdjustment> weaknesses;
        private readonly IReadOnlyList<SpellAttackDefenseAdjustment> resistances;

        /// <summary>Creates immutable resolution data captured after validation.</summary>
        /// <param name="armorClass">The positive current target Armor Class.</param>
        /// <param name="attackModifiers">
        /// Current attack candidates excluding the spell modifier and MAP.
        /// </param>
        /// <param name="weaknesses">Current typed target weaknesses.</param>
        /// <param name="resistances">Current typed target resistances.</param>
        public SpellAttackResolutionData(
            int armorClass,
            IEnumerable<Modifier> attackModifiers,
            IEnumerable<SpellAttackDefenseAdjustment> weaknesses,
            IEnumerable<SpellAttackDefenseAdjustment> resistances
        )
        {
            if (armorClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(armorClass));
            ArmorClass = armorClass;
            if (attackModifiers == null)
                throw new ArgumentNullException(nameof(attackModifiers));
            Modifier[] copiedModifiers = attackModifiers.ToArray();
            if (copiedModifiers.Any(modifier => modifier.IsEmpty))
                throw new ArgumentException(
                    "Attack modifiers cannot contain empty values.",
                    nameof(attackModifiers)
                );
            this.attackModifiers = Array.AsReadOnly(copiedModifiers);
            this.weaknesses = Copy(weaknesses, nameof(weaknesses));
            this.resistances = Copy(resistances, nameof(resistances));
        }

        /// <summary>Gets the target's current Armor Class.</summary>
        public int ArmorClass { get; }

        /// <summary>Gets current attack modifiers excluding spell proficiency and MAP.</summary>
        public IReadOnlyList<Modifier> AttackModifiers => attackModifiers;

        /// <summary>Gets current target weaknesses.</summary>
        public IReadOnlyList<SpellAttackDefenseAdjustment> Weaknesses => weaknesses;

        /// <summary>Gets current target resistances.</summary>
        public IReadOnlyList<SpellAttackDefenseAdjustment> Resistances => resistances;

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
            where T : class
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);
            T[] copied = values.ToArray();
            if (copied.Any(value => value == null))
                throw new ArgumentException("Values cannot contain null.", parameterName);
            return new ReadOnlyCollection<T>(copied);
        }
    }

    /// <summary>
    /// Revalidates Unity-bound spell targeting before costs and captures current combat inputs.
    /// </summary>
    public interface ISpellAttackResolutionDataProvider
    {
        /// <summary>Checks current registration, geometry, line of effect, and defensive data.</summary>
        ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellAttackDefinition attack,
            CreatureId target
        );

        /// <summary>Captures current AC, modifiers, weaknesses, and resistances for a legal target.</summary>
        SpellAttackResolutionData Capture(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellAttackDefinition attack,
            CreatureId target
        );
    }

    /// <summary>Contains one final typed spell-damage amount after defenses.</summary>
    public sealed class SpellAttackDamagePart
    {
        internal SpellAttackDamagePart(string damageType, int amount)
        {
            DamageType = damageType;
            Amount = amount;
        }

        /// <summary>Gets the damage type.</summary>
        public string DamageType { get; }

        /// <summary>Gets the final non-negative amount.</summary>
        public int Amount { get; }
    }

    /// <summary>Contains one deterministic spell attack, degree, and damage result.</summary>
    public sealed class SpellAttackResolution
    {
        private readonly IReadOnlyList<SpellAttackDamagePart> damage;

        internal SpellAttackResolution(
            SpellReference spell,
            CreatureId actor,
            CreatureId target,
            CheckOutcome attackCheck,
            int multipleAttackPenalty,
            IEnumerable<SpellAttackDamagePart> damage
        )
        {
            Spell = spell;
            Actor = actor;
            Target = target;
            AttackCheck = attackCheck ?? throw new ArgumentNullException(nameof(attackCheck));
            MultipleAttackPenalty = multipleAttackPenalty;
            this.damage = new ReadOnlyCollection<SpellAttackDamagePart>(
                (damage ?? throw new ArgumentNullException(nameof(damage))).ToArray()
            );
            FinalDamage = this.damage.Sum(part => part.Amount);
        }

        /// <summary>Gets the exact spell and rank that made the attack.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the caster.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the selected target.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the complete interceptable attack-check result.</summary>
        public CheckOutcome AttackCheck { get; }

        /// <summary>Gets the MAP contribution included in the check.</summary>
        public int MultipleAttackPenalty { get; }

        /// <summary>Gets final damage grouped by type after defenses.</summary>
        public IReadOnlyList<SpellAttackDamagePart> Damage => damage;

        /// <summary>Gets final damage submitted to authoritative health.</summary>
        public int FinalDamage { get; }

        /// <summary>Gets whether the attack hit.</summary>
        public bool Hit =>
            AttackCheck.Degree == DegreeOfSuccess.Success
            || AttackCheck.Degree == DegreeOfSuccess.CriticalSuccess;
    }
}
