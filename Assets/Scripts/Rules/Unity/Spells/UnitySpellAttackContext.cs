using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Attack;
using GridPrivate;
using GridPublic;

namespace Game.Rules.Unity.Spells
{
    /// <summary>
    /// Revalidates spell targets against the live grid and extracts current Unity combat values.
    /// </summary>
    public sealed class UnitySpellAttackContext : ISpellAttackResolutionDataProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private Tile[,] tiles;

        /// <summary>Creates one encounter-owned generic spell-attack adapter.</summary>
        /// <param name="creatures">Stable rules-to-Unity creature mappings.</param>
        /// <param name="tiles">The current initialized combat grid.</param>
        public UnitySpellAttackContext(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Tile[,] tiles
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        }

        /// <summary>Replaces the live grid boundary after topology changes.</summary>
        /// <param name="replacement">The current initialized combat grid.</param>
        public void ReplaceTiles(Tile[,] replacement) =>
            tiles = replacement ?? throw new ArgumentNullException(nameof(replacement));

        /// <inheritdoc/>
        public ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellAttackDefinition attack,
            CreatureId target
        )
        {
            if (
                !creatures.TryGetValue(actor, out CreatureComponent attacker)
                || !creatures.TryGetValue(target, out CreatureComponent defender)
                || attacker == null
                || defender == null
            )
                return ActionValidationResult.Invalid(
                    "The selected spell-attack creature is unavailable."
                );
            if (attack.Target is not OneCreatureSpellAttackTarget oneCreature)
                return ActionValidationResult.Invalid(
                    "The spell attack target structure is unsupported."
                );
            StrikeTargetResult targeting = StrikeTargeting.Evaluate(
                attacker.gameObject,
                defender.gameObject,
                tiles,
                new StrikeTargetRequest
                {
                    IsRanged = true,
                    FixedRangeFeet = oneCreature.RangeFeet,
                    RequiresLineOfEffect = true,
                }
            );
            if (targeting == null)
                return ActionValidationResult.Invalid(
                    "The spell target is out of range or has no line of effect."
                );
            return defender.ResolveArmorClass().Total > 0
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid(
                    "The spell target's Armor Class must be positive."
                );
        }

        /// <inheritdoc/>
        public SpellAttackResolutionData Capture(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellAttackDefinition attack,
            CreatureId target
        )
        {
            CreatureComponent attacker = RequireCreature(actor);
            CreatureComponent defender = RequireCreature(target);
            return new SpellAttackResolutionData(
                Math.Max(1, defender.ResolveArmorClass().Total),
                UnityAttackModifierAdapter.Capture(attacker),
                (defender.weaknesses ?? new List<DamageValue>()).Select(
                    value => new SpellAttackDefenseAdjustment(
                        value.DamageType,
                        Math.Max(0, value.DamageAmount)
                    )
                ),
                (defender.resistances ?? new List<DamageValue>()).Select(
                    value => new SpellAttackDefenseAdjustment(
                        value.DamageType,
                        Math.Max(0, value.DamageAmount)
                    )
                )
            );
        }

        private CreatureComponent RequireCreature(CreatureId id)
        {
            if (!creatures.TryGetValue(id, out CreatureComponent creature) || creature == null)
                throw new InvalidOperationException($"Creature '{id.Value}' is unavailable.");
            return creature;
        }
    }
}
