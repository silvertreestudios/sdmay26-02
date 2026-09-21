using System;
using System.Collections.Generic;
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
            return
                snapshot.Statistics.TryGet(target, out CreatureStatisticsState statistics)
                && statistics.ArmorClass > 0
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
            OneCreatureSpellAttackTarget oneCreature =
                attack.Target as OneCreatureSpellAttackTarget
                ?? throw new InvalidOperationException(
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
                throw new InvalidOperationException(
                    "The spell target became invalid after validation."
                );
            if (!snapshot.Statistics.TryGet(target, out CreatureStatisticsState statistics))
                throw new InvalidOperationException(
                    $"Creature '{target.Value}' has no enrolled statistics."
                );
            return new SpellAttackResolutionData(
                // The rules slice owns the untyped base AC. Cover remains a typed candidate so it
                // participates correctly in circumstance-modifier stacking during resolution.
                Math.Max(1, statistics.ArmorClass),
                targeting.CoverAcBonus == 0
                    ? Array.Empty<Modifier>()
                    : new[]
                    {
                        new Modifier(
                            targeting.CoverAcBonus,
                            ModifierType.Circumstance,
                            RuleSource.FromSlug("cover"),
                            Statistic.ArmorClass
                        ),
                    },
                UnityAttackDataAdapter.CaptureModifiers(attacker),
                UnityAttackDataAdapter.CaptureWeaknesses(defender),
                UnityAttackDataAdapter.CaptureResistances(defender)
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
