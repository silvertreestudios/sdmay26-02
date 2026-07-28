using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Attack;

namespace Game.Rules.Unity.Spells
{
    /// <summary>Projects resolved generic spell attacks through shared attack presentation.</summary>
    public sealed class UnitySpellAttackPresentationObserver
        : IResolvedOpObserver<ResolveSpellAttackOp, SpellAttackResolution>
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly ISpellDefinitionCatalog catalog;

        /// <summary>Creates an encounter-owned spell-attack presenter.</summary>
        /// <param name="creatures">Stable rules-to-Unity creature mappings.</param>
        /// <param name="catalog">Definitions used for player-facing spell names.</param>
        public UnitySpellAttackPresentationObserver(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            ISpellDefinitionCatalog catalog
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <inheritdoc/>
        public ValueTask OnOperationResolved(
            ResolveSpellAttackOp operation,
            SpellAttackResolution result,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !catalog.TryGetSpell(
                    operation.Spell,
                    out Game.Rules.Runtime.SpellDefinition definition
                )
                || !creatures.TryGetValue(operation.Actor, out CreatureComponent attacker)
                || attacker == null
            )
                return default;
            if (
                !creatures.TryGetValue(result.Target, out CreatureComponent target)
                || target == null
            )
                return default;
            UnityAttackResultPresentation.Present(
                attacker.gameObject,
                target.gameObject,
                definition.DisplayName,
                new UnityAttackResult(
                    result.AttackRoll,
                    result.AttackModifier,
                    result.ArmorClass,
                    result.Degree,
                    ToDamage(result),
                    result.FinalDamage,
                    result.MultipleAttackPenalty,
                    0,
                    0
                )
            );
            return default;
        }

        private static IEnumerable<UnityAttackDamagePart> ToDamage(SpellAttackResolution resolution)
        {
            foreach (TypedDamagePart part in resolution.Damage)
                yield return new UnityAttackDamagePart(part.DamageType, part.Amount);
        }
    }
}
