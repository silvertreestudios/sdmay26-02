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
    public sealed class UnitySpellAttackContext
        : ISpellAttackResolutionDataProvider,
            ISpellSaveTargetingProvider
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
                UnityAttackDataAdapter.CaptureModifiers(attacker),
                UnityAttackDataAdapter.CaptureWeaknesses(defender),
                UnityAttackDataAdapter.CaptureResistances(defender)
            );
        }

        /// <inheritdoc/>
        public ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellSaveDefinition save,
            SpellAreaPlacement placement,
            IReadOnlyList<CreatureId> selectedCreatures
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            if (selectedCreatures == null)
                throw new ArgumentNullException(nameof(selectedCreatures));
            if (!snapshot.Positions.TryGet(actor, out GridPosition actorPosition))
                return ActionValidationResult.Invalid(
                    "The caster has no authoritative grid position."
                );
            if (!creatures.TryGetValue(actor, out CreatureComponent caster) || caster == null)
                return ActionValidationResult.Invalid("The area caster is unavailable.");
            if (placement.Shape != save.Target.Shape)
                return ActionValidationResult.Invalid(
                    "The selected area shape does not match the spell definition."
                );
            if (save.Target.Shape != SpellAreaShape.Burst && placement.OriginCell != actorPosition)
                return ActionValidationResult.Invalid(
                    "The selected area origin does not match the caster's current position."
                );

            AreaTargetResult evaluated = AreaTargeting.Evaluate(
                new AreaTargetSource(ToVector(actorPosition)),
                tiles,
                UnitySpellAreaAdapter.ToGridRequest(save.Target),
                UnitySpellAreaAdapter.ToGridPlacement(placement)
            );
            if (evaluated == null || !evaluated.IsLegal)
                return ActionValidationResult.Invalid(
                    "The selected area placement is outside its authored geometry or range."
                );

            Dictionary<CreatureComponent, CreatureId> reverse = creatures
                .Where(pair => pair.Value != null)
                .ToDictionary(pair => pair.Value, pair => pair.Key);
            List<CreatureId> affected = new();
            foreach (AreaAffectedCreature candidate in evaluated.Creatures)
            {
                CreatureComponent creature = candidate.Creature?.GetComponent<CreatureComponent>();
                if (
                    !candidate.IsAffected
                    || creature == null
                    || !reverse.TryGetValue(creature, out CreatureId id)
                )
                    continue;
                if (
                    !snapshot.Positions.TryGet(id, out GridPosition currentPosition)
                    || ToVector(currentPosition) != candidate.Cell
                )
                    return ActionValidationResult.Invalid(
                        "Area targeting is out of sync with authoritative creature positions."
                    );
                affected.Add(id);
            }
            CreatureId[] exact = affected
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            return selectedCreatures.SequenceEqual(exact)
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid(
                    "The selected spell-save creatures are not the exact currently affected area set."
                );
        }

        private static UnityEngine.Vector3Int ToVector(GridPosition value) =>
            new(value.X, value.Y, value.Z);

        private CreatureComponent RequireCreature(CreatureId id)
        {
            if (!creatures.TryGetValue(id, out CreatureComponent creature) || creature == null)
                throw new InvalidOperationException($"Creature '{id.Value}' is unavailable.");
            return creature;
        }
    }

    /// <summary>
    /// Converts explicitly between Unity grid area contracts and the ordinal-independent rules
    /// contracts at their single shared boundary.
    /// </summary>
    internal static class UnitySpellAreaAdapter
    {
        internal static AreaShape ToGridShape(SpellAreaShape shape) =>
            shape switch
            {
                SpellAreaShape.Cone => AreaShape.Cone,
                SpellAreaShape.Burst => AreaShape.Burst,
                SpellAreaShape.Emanation => AreaShape.Emanation,
                SpellAreaShape.Line => AreaShape.Line,
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };

        internal static SpellAreaShape ToRulesShape(AreaShape shape) =>
            shape switch
            {
                AreaShape.Burst => SpellAreaShape.Burst,
                AreaShape.Cone => SpellAreaShape.Cone,
                AreaShape.Emanation => SpellAreaShape.Emanation,
                AreaShape.Line => SpellAreaShape.Line,
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };

        internal static AreaDirection ToGridDirection(SpellAreaDirection direction) =>
            direction switch
            {
                SpellAreaDirection.East => AreaDirection.East,
                SpellAreaDirection.NorthEast => AreaDirection.NorthEast,
                SpellAreaDirection.North => AreaDirection.North,
                SpellAreaDirection.NorthWest => AreaDirection.NorthWest,
                SpellAreaDirection.West => AreaDirection.West,
                SpellAreaDirection.SouthWest => AreaDirection.SouthWest,
                SpellAreaDirection.South => AreaDirection.South,
                SpellAreaDirection.SouthEast => AreaDirection.SouthEast,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };

        internal static SpellAreaDirection ToRulesDirection(AreaDirection direction) =>
            direction switch
            {
                AreaDirection.East => SpellAreaDirection.East,
                AreaDirection.NorthEast => SpellAreaDirection.NorthEast,
                AreaDirection.North => SpellAreaDirection.North,
                AreaDirection.NorthWest => SpellAreaDirection.NorthWest,
                AreaDirection.West => SpellAreaDirection.West,
                AreaDirection.SouthWest => SpellAreaDirection.SouthWest,
                AreaDirection.South => SpellAreaDirection.South,
                AreaDirection.SouthEast => SpellAreaDirection.SouthEast,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };

        internal static AreaTargetRequest ToGridRequest(SpellAreaTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            return new AreaTargetRequest
            {
                Shape = ToGridShape(target.Shape),
                SizeFeet = target.SizeFeet,
                RangeFeet = target.RangeFeet,
                RequiresLineOfEffect = true,
            };
        }

        internal static AreaPlacement ToGridPlacement(SpellAreaPlacement placement) =>
            new()
            {
                Shape = ToGridShape(placement.Shape),
                OriginCell = new UnityEngine.Vector3Int(
                    placement.OriginCell.X,
                    placement.OriginCell.Y,
                    placement.OriginCell.Z
                ),
                OriginCorner = new UnityEngine.Vector2Int(
                    placement.OriginCornerX,
                    placement.OriginCornerZ
                ),
                Direction = ToGridDirection(placement.Direction),
            };

        internal static SpellAreaPlacement ToRulesPlacement(AreaPlacement placement)
        {
            if (placement == null)
                throw new ArgumentNullException(nameof(placement));
            return new SpellAreaPlacement(
                ToRulesShape(placement.Shape),
                new GridPosition(
                    placement.OriginCell.x,
                    placement.OriginCell.y,
                    placement.OriginCell.z
                ),
                placement.OriginCorner.x,
                placement.OriginCorner.y,
                ToRulesDirection(placement.Direction)
            );
        }
    }
}
