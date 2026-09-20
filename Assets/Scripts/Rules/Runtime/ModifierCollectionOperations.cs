using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Resolves rules-owned adjustments to an already captured Armor Class.</summary>
    public sealed class AdjustArmorClassOp : IRuleOp<ModifierCollection>
    {
        /// <summary>Creates a nested Armor Class adjustment request.</summary>
        /// <param name="target">The creature whose Armor Class is being resolved.</param>
        /// <param name="baseArmorClass">The positive captured base Armor Class.</param>
        public AdjustArmorClassOp(CreatureId target, int baseArmorClass)
            : this(target, baseArmorClass, Array.Empty<Modifier>()) { }

        /// <summary>Creates a request with captured Armor Class modifier candidates.</summary>
        /// <param name="target">The creature whose Armor Class is being resolved.</param>
        /// <param name="baseArmorClass">The positive captured base Armor Class.</param>
        /// <param name="initialModifiers">Typed candidates already applying to the defense.</param>
        public AdjustArmorClassOp(
            CreatureId target,
            int baseArmorClass,
            IEnumerable<Modifier> initialModifiers
        )
        {
            if (target.IsEmpty)
                throw new ArgumentException("An Armor Class target is required.", nameof(target));
            if (baseArmorClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(baseArmorClass));
            if (initialModifiers == null)
                throw new ArgumentNullException(nameof(initialModifiers));
            Target = target;
            BaseArmorClass = baseArmorClass;
            InitialModifiers = Array.AsReadOnly(initialModifiers.ToArray());
        }

        /// <summary>Gets the defended creature.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the captured Armor Class before rules-owned active effects.</summary>
        public int BaseArmorClass { get; }

        /// <summary>Gets typed Armor Class candidates captured before active-effect middleware.</summary>
        public IReadOnlyList<Modifier> InitialModifiers { get; }
    }

    /// <summary>Collects active-effect damage dice for a weapon or unarmed Strike.</summary>
    public sealed class CollectStrikeDamageDiceOp : IRuleOp<IReadOnlyList<TypedDamageDice>>
    {
        /// <summary>Creates a nested Strike damage contribution request.</summary>
        public CollectStrikeDamageDiceOp(CreatureId attacker, CreatureId target)
        {
            if (attacker.IsEmpty || target.IsEmpty)
                throw new ArgumentException("Strike damage collection requires both creatures.");
            Attacker = attacker;
            Target = target;
        }

        /// <summary>Gets the striking creature.</summary>
        public CreatureId Attacker { get; }

        /// <summary>Gets the struck creature.</summary>
        public CreatureId Target { get; }
    }

    /// <summary>
    /// Collects attack-roll modifiers through active middleware before an attack roll is made.
    /// </summary>
    /// <remarks>
    /// The operation is read-only but interceptable. Its handler seeds current rules-state
    /// candidates without adding a feature-specific attack base. Active bindings can inspect the
    /// operation ancestry to distinguish Strike, spell, and future attack workflows.
    /// </remarks>
    public sealed class CollectAttackModifiersOp : IRuleOp<ModifierCollection>
    {
        /// <summary>Gets the attacking creature.</summary>
        public CreatureId Attacker { get; }

        /// <summary>Gets the target creature.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the ancestor operation responsible for the attack calculation.</summary>
        public CheckSource Source { get; }

        /// <summary>Creates a nested attack-modifier collection request.</summary>
        /// <param name="attacker">The attacking creature.</param>
        /// <param name="target">The target creature.</param>
        /// <param name="source">The ancestor operation responsible for the calculation.</param>
        /// <exception cref="ArgumentException">
        /// Any required creature or source identity is empty.
        /// </exception>
        public CollectAttackModifiersOp(CreatureId attacker, CreatureId target, CheckSource source)
        {
            if (attacker.IsEmpty)
                throw new ArgumentException("An attacker is required.", nameof(attacker));
            if (target.IsEmpty)
                throw new ArgumentException("A target is required.", nameof(target));
            if (source.IsEmpty)
                throw new ArgumentException(
                    "Modifier collection requires trusted source provenance.",
                    nameof(source)
                );

            Attacker = attacker;
            Target = target;
            Source = source;
        }
    }

    /// <summary>
    /// Collects skill-check modifiers through active middleware before the d20 is rolled.
    /// </summary>
    /// <remarks>
    /// The actor, open skill identity, and trusted source let effects contribute only when their
    /// eligibility rules apply, including effects that target a particular Lore skill.
    /// Middleware contributes by returning a resolved collection with added candidates; the
    /// enclosing check requires modifier collection to resolve before it can roll.
    /// </remarks>
    public sealed class CollectSkillCheckModifiersOp : IRuleOp<ModifierCollection>
    {
        /// <summary>Gets the creature attempting the skill check.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the skill whose modifiers are collected.</summary>
        public Skill Skill { get; }

        /// <summary>Gets the ancestor operation responsible for the skill check.</summary>
        public CheckSource Source { get; }

        /// <summary>Creates a nested skill-check modifier collection request.</summary>
        /// <param name="actor">The creature attempting the skill check.</param>
        /// <param name="skill">The open skill identity being checked.</param>
        /// <param name="source">The ancestor operation responsible for the check.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="actor"/>, <paramref name="skill"/>, or <paramref name="source"/> is empty.
        /// </exception>
        public CollectSkillCheckModifiersOp(CreatureId actor, Skill skill, CheckSource source)
        {
            if (actor.IsEmpty)
                throw new ArgumentException(
                    "A skill modifier collection requires an actor.",
                    nameof(actor)
                );
            if (skill.IsEmpty)
                throw new ArgumentException(
                    "A skill modifier collection requires a skill.",
                    nameof(skill)
                );
            if (source.IsEmpty)
                throw new ArgumentException(
                    "Modifier collection requires trusted source provenance.",
                    nameof(source)
                );

            Actor = actor;
            Skill = skill;
            Source = source;
        }
    }

    /// <summary>
    /// Collects saving-throw modifiers through active middleware before the d20 is rolled.
    /// </summary>
    /// <remarks>
    /// Middleware contributes by returning a resolved collection with added candidates; the
    /// enclosing save requires modifier collection to resolve before it can roll.
    /// </remarks>
    public sealed class CollectSavingThrowModifiersOp : IRuleOp<ModifierCollection>
    {
        /// <summary>Gets the creature attempting the saving throw.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the saving throw whose modifiers are collected.</summary>
        public SaveKind Save { get; }

        /// <summary>Gets the ancestor operation responsible for the saving throw.</summary>
        public CheckSource Source { get; }

        /// <summary>Creates a nested saving-throw modifier collection request.</summary>
        /// <param name="actor">The creature attempting the saving throw.</param>
        /// <param name="save">The Fortitude, Reflex, or Will saving throw.</param>
        /// <param name="source">The ancestor operation responsible for the save.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="actor"/> or <paramref name="source"/> is empty.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="save"/> is undefined.</exception>
        public CollectSavingThrowModifiersOp(CreatureId actor, SaveKind save, CheckSource source)
        {
            if (actor.IsEmpty)
                throw new ArgumentException(
                    "A saving throw modifier collection requires an actor.",
                    nameof(actor)
                );
            if (!Enum.IsDefined(typeof(SaveKind), save))
                throw new ArgumentOutOfRangeException(nameof(save));
            if (source.IsEmpty)
                throw new ArgumentException(
                    "Modifier collection requires trusted source provenance.",
                    nameof(source)
                );

            Actor = actor;
            Save = save;
            Source = source;
        }
    }
}
