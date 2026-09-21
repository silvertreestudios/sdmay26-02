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
            ISpellTargetingDataProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly ICombatantFriendshipProvider friendshipProvider;
        private Tile[,] tiles;

        /// <summary>Creates one encounter-owned generic spell-attack adapter.</summary>
        /// <param name="creatures">Stable rules-to-Unity creature mappings.</param>
        /// <param name="tiles">The current initialized combat grid.</param>
        /// <param name="friendshipProvider">The encounter's ordered friendship relationships.</param>
        public UnitySpellAttackContext(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Tile[,] tiles,
            ICombatantFriendshipProvider friendshipProvider
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
            this.friendshipProvider =
                friendshipProvider ?? throw new ArgumentNullException(nameof(friendshipProvider));
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
        public ActionValidationResult ValidateSelection(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellSelectionProfile profile,
            SpellCastSelection selection
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (selection == null)
                throw new ArgumentNullException(nameof(selection));
            if (!creatures.TryGetValue(actor, out CreatureComponent caster) || caster == null)
                return ActionValidationResult.Invalid("The spell caster is unavailable.");

            if (profile.Kind == SpellSelectionKind.None)
                return selection.Creatures.Count == 0
                    ? ActionValidationResult.Valid
                    : ActionValidationResult.Invalid("The spell does not select a target.");
            if (
                profile.Kind == SpellSelectionKind.SingleCreature
                || profile.Kind == SpellSelectionKind.ExactCreatureCount
            )
                return ValidateCreatureTargets(snapshot, actor, caster, profile, selection);
            if (
                profile.Kind != SpellSelectionKind.Cone
                && profile.Kind != SpellSelectionKind.Emanation
            )
                return ActionValidationResult.Invalid("The spell selection shape is unsupported.");
            if (profile.Kind == SpellSelectionKind.Cone && !selection.HasAreaDirection)
                return ActionValidationResult.Invalid(
                    "A directed spell area requires its chosen direction."
                );

            AreaTargetResult current = AreaTargeting.Evaluate(
                caster.gameObject,
                tiles,
                new AreaTargetRequest
                {
                    Shape =
                        profile.Kind == SpellSelectionKind.Cone
                            ? AreaShape.Cone
                            : AreaShape.Emanation,
                    SizeFeet = profile.AreaFeet,
                    IncludeCenter = profile.IncludeCaster,
                    RequiresLineOfEffect = true,
                },
                new AreaPlacement
                {
                    Shape =
                        profile.Kind == SpellSelectionKind.Cone
                            ? AreaShape.Cone
                            : AreaShape.Emanation,
                    OriginCell = UnityEngine.Vector3Int.RoundToInt(caster.transform.position),
                    Direction =
                        profile.Kind == SpellSelectionKind.Cone
                            ? ToUnityDirection(selection.AreaDirection)
                            : AreaDirection.East,
                }
            );
            if (current == null)
                return ActionValidationResult.Invalid("The selected spell area is unavailable.");

            HashSet<CreatureId> expected = new();
            foreach (
                CreatureComponent target in current
                    .Creatures.Where(value => value.IsAffected)
                    .Select(value => value.Creature.GetComponent<CreatureComponent>())
                    .Where(value => value != null)
            )
            {
                KeyValuePair<CreatureId, CreatureComponent> registered = creatures.FirstOrDefault(
                    pair => pair.Value == target
                );
                if (
                    registered.Key.IsEmpty
                    || !snapshot.Health.TryGet(registered.Key, out HealthState health)
                    || !health.IsLiving
                    || (profile.FriendlyOnly && !AreFriendly(snapshot, actor, registered.Key))
                )
                    continue;
                expected.Add(registered.Key);
            }
            if (profile.IncludeCaster)
                expected.Add(actor);
            return expected.SetEquals(selection.Creatures)
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid(
                    "The selected creatures no longer match the spell area."
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
                Array.Empty<Modifier>(),
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

        private ActionValidationResult ValidateCreatureTargets(
            RulesSnapshot snapshot,
            CreatureId actor,
            CreatureComponent caster,
            SpellSelectionProfile profile,
            SpellCastSelection selection
        )
        {
            foreach (CreatureId targetId in selection.Creatures)
            {
                if (
                    !creatures.TryGetValue(targetId, out CreatureComponent target)
                    || target == null
                )
                    return ActionValidationResult.Invalid(
                        "A selected spell target is unavailable."
                    );
                if (profile.FriendlyOnly && !AreFriendly(snapshot, actor, targetId))
                    return ActionValidationResult.Invalid(
                        "The selected spell target is not friendly."
                    );
                StrikeTargetResult targeting = StrikeTargeting.Evaluate(
                    caster.gameObject,
                    target.gameObject,
                    tiles,
                    new StrikeTargetRequest
                    {
                        IsRanged = profile.RangeFeet > 5,
                        FixedRangeFeet = profile.RangeFeet,
                        RequiresLineOfEffect = true,
                        IncludeSelf = true,
                    }
                );
                if (targeting == null)
                    return ActionValidationResult.Invalid(
                        "A selected spell target is out of range or has no line of effect."
                    );
            }
            return ActionValidationResult.Valid;
        }

        private bool AreFriendly(RulesSnapshot snapshot, CreatureId source, CreatureId target) =>
            snapshot.Creatures.TryGet(source, out CreatureState sourceState)
            && snapshot.Creatures.TryGet(target, out CreatureState targetState)
            && friendshipProvider.IsFriendly(sourceState.Player, targetState.Player);

        private static AreaDirection ToUnityDirection(SpellAreaDirection direction) =>
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
    }
}
