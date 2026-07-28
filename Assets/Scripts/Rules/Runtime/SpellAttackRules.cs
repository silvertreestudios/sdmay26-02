using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Requests one already-begun spell attack through the typed rules runtime.</summary>
    /// <remarks>
    /// This operation is nested-only. A validated <see cref="CastSpellActionOp"/> supplies its
    /// catalog-owned definition after the shared action lifecycle commits costs.
    /// </remarks>
    public sealed class ResolveSpellAttackOp : IRuleOp<SpellAttackResolution>
    {
        /// <summary>Creates one nested spell-attack request.</summary>
        public ResolveSpellAttackOp(CreatureId actor, SpellReference spell, CreatureId target)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A spell attack actor is required.", nameof(actor));
            if (target.IsEmpty)
                throw new ArgumentException("A spell attack target is required.", nameof(target));
            Actor = actor;
            Spell = spell;
            Target = target;
        }

        /// <summary>Gets the caster.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the exact spell and rank.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the selected target.</summary>
        public CreatureId Target { get; }
    }

    internal sealed class ResolveSpellAttackHandler
        : IOpHandler<ResolveSpellAttackOp, SpellAttackResolution>
    {
        private readonly ISpellActionCatalog catalog;
        private readonly ISpellAttackResolutionDataProvider resolutionData;
        private readonly IRulesSelectors selectors;

        public ResolveSpellAttackHandler(
            ISpellActionCatalog catalog,
            ISpellAttackResolutionDataProvider resolutionData,
            IRulesSelectors selectors
        )
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.resolutionData =
                resolutionData ?? throw new ArgumentNullException(nameof(resolutionData));
            this.selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));
        }

        public ValueTask<SpellAttackResolution> Handle(
            OpFrame<ResolveSpellAttackOp> frame,
            OpHandlerContext context
        )
        {
            ResolveSpellAttackOp operation = frame.Op;
            if (!catalog.TryGetSpell(operation.Spell, out SpellDefinition definition))
                throw new InvalidOperationException("The spell-attack definition is unavailable.");
            if (
                definition.Attacks.Count != 1
                || definition.Attacks[0].Target is not OneCreatureSpellAttackTarget
            )
                throw new InvalidOperationException(
                    "The spell does not contain one supported spell attack."
                );
            SpellAttackDefinition attack = definition.Attacks[0];
            SpellAttackResolutionData data = resolutionData.Capture(
                context.Snapshot,
                operation.Actor,
                attack,
                operation.Target
            );
            int priorAttacks = context.Snapshot.MultipleAttackPenalty.TryGet(
                operation.Actor,
                out MultipleAttackPenaltyState map
            )
                ? map.AttackCount
                : 0;
            int mapPenalty = MultipleAttackPenaltyResolver.Resolve(priorAttacks, false);
            List<Modifier> initialModifiers = new()
            {
                Modifier.Untyped(
                    catalog.GetSpellBook(operation.Actor).SpellAttackModifier,
                    RuleSource.FromSlug(operation.Spell.Spell.Value),
                    Statistic.AttackRoll
                ),
                Modifier.Untyped(
                    mapPenalty,
                    RuleSource.FromSlug("multiple-attack-penalty"),
                    Statistic.AttackRoll
                ),
            };
            if (
                selectors.TryGetCurrentModifiers(
                    context.Snapshot,
                    operation.Actor,
                    Statistic.AttackRoll,
                    out ModifierCollection snapshotModifiers
                )
            )
                initialModifiers.AddRange(snapshotModifiers.Candidates);
            initialModifiers.AddRange(data.AttackModifiers);
            ModifierCollection attackModifiers = new(Statistic.AttackRoll, initialModifiers);
            RollResult attackRoll = context.Rolls.Roll(DiceExpressions.D20);
            int attackTotal = checked(attackRoll.Total + attackModifiers.Total);
            DegreeOfSuccess degree = DegreeOfSuccessResolver.Resolve(
                attackRoll.Values[0],
                attackTotal,
                data.ArmorClass
            );
            IReadOnlyList<TypedDamagePart> damage = degree
                is DegreeOfSuccess.Success
                    or DegreeOfSuccess.CriticalSuccess
                ? TypedDamageResolver.Resolve(
                    attack.Damage,
                    Array.Empty<TypedFlatDamage>(),
                    Array.Empty<TypedDamageDice>(),
                    degree,
                    data.Weaknesses,
                    data.Resistances,
                    context.Rolls
                )
                : Array.Empty<TypedDamagePart>();
            return new ValueTask<SpellAttackResolution>(
                new SpellAttackResolution(
                    operation.Spell,
                    operation.Actor,
                    operation.Target,
                    attackRoll,
                    attackModifiers.Total,
                    data.ArmorClass,
                    degree,
                    mapPenalty,
                    damage
                )
            );
        }
    }

    internal sealed class UnsupportedSpellAttackResolutionDataProvider
        : ISpellAttackResolutionDataProvider
    {
        public static UnsupportedSpellAttackResolutionDataProvider Instance { get; } = new();

        private UnsupportedSpellAttackResolutionDataProvider() { }

        public ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellAttackDefinition attack,
            CreatureId target
        ) => ActionValidationResult.Invalid("Spell attack resolution is not configured.");

        public SpellAttackResolutionData Capture(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellAttackDefinition attack,
            CreatureId target
        ) => throw new InvalidOperationException("Spell attack resolution is not configured.");
    }
}
