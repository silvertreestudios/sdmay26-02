using System;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Strikes;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Combat.Rules
{
    /// <summary>
    /// Captures Unity-only Flanking inputs and delegates the named rule to
    /// <see cref="FlankingRules"/>.
    /// </summary>
    public static class FlankingRule
    {
        private const int DefaultUnarmedReachFeet = 5;

        /// <summary>
        /// Evaluates the retained legacy Strike context through the runtime Flanking selector.
        /// </summary>
        /// <remarks>
        /// Production rules-backed Strikes use the snapshot overload. This adapter remains for
        /// compiled legacy calculation fixtures and intentionally searches only the current grid's
        /// occupants instead of discovering scene combatants.
        /// </remarks>
        public static bool GrantsOffGuardToMeleeAttack(
            GameObject attacker,
            GameObject target,
            StrikeProfile strike
        )
        {
            if (attacker == null || target == null || strike == null || strike.IsRangedAttack)
                return false;

            Tile[,] tiles = TryGetTiles();
            if (tiles == null)
                return false;

            return IsFlanking(
                attacker,
                target,
                tiles,
                Math.Max(DefaultUnarmedReachFeet, strike.ReachFeet)
            );
        }

        /// <summary>
        /// Adapts a standalone Unity grid to the same runtime Flanking selector used in encounters.
        /// </summary>
        public static bool IsFlanking(
            GameObject attacker,
            GameObject target,
            Tile[,] tiles,
            int attackerReachFeet = DefaultUnarmedReachFeet
        )
        {
            if (attacker == null || target == null || tiles == null)
                return false;

            Dictionary<CreatureId, CreatureComponent> creatures = new();
            RulesStateSeed seed = new();
            int nextId = 1;
            CreatureId attackerId = default;
            CreatureId targetId = default;
            foreach (GameObject combatant in GetGridCombatants(tiles, attacker, target))
            {
                CreatureComponent creature = combatant.GetComponent<CreatureComponent>();
                if (creature == null)
                    continue;

                CreatureId id = new($"legacy-flanking-{nextId++}");
                creatures.Add(id, creature);
                int hitPoints = Math.Max(0, creature.hp);
                seed.SeedCreature(new CreatureState(id, new PlayerId("legacy-flanking-adapter")))
                    .SeedHealth(id, new HealthState(hitPoints, hitPoints))
                    .SeedPosition(id, ToRulesPosition(combatant));
                if (combatant == attacker)
                    attackerId = id;
                if (combatant == target)
                    targetId = id;
            }

            if (attackerId.IsEmpty || targetId.IsEmpty)
                return false;

            RulesSnapshot snapshot = new InMemoryRulesStore(seed).Snapshot;
            return IsFlanking(snapshot, attackerId, targetId, creatures, tiles, attackerReachFeet);
        }

        /// <summary>
        /// Captures current Unity availability, relationships, reach, and topology while resolving
        /// roster identity, health, and positions from one authoritative rules snapshot.
        /// </summary>
        public static bool IsFlanking(
            RulesSnapshot snapshot,
            CreatureId attacker,
            CreatureId target,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Tile[,] tiles,
            int attackerReachFeet = DefaultUnarmedReachFeet
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (creatures == null)
                throw new ArgumentNullException(nameof(creatures));
            if (tiles == null)
                throw new ArgumentNullException(nameof(tiles));
            if (
                attacker.IsEmpty
                || target.IsEmpty
                || attackerReachFeet <= 0
                || !creatures.TryGetValue(attacker, out CreatureComponent attackerCreature)
                || !creatures.TryGetValue(target, out CreatureComponent targetCreature)
                || attackerCreature == null
                || targetCreature == null
            )
                return false;

            GameObject attackerObject = attackerCreature.gameObject;
            GameObject targetObject = targetCreature.gameObject;
            Team attackerTeam = attackerObject.GetComponent<Team>();
            Team targetTeam = targetObject.GetComponent<Team>();
            bool attackerCanFlank =
                CanUseFlankingBoundary(attackerObject)
                && attackerTeam != null
                && targetTeam != null
                && !AreFriendly(attackerTeam.Name, targetTeam.Name);
            bool attackerThreatens = ThreatensTarget(
                attackerObject,
                targetObject,
                tiles,
                attackerReachFeet
            );

            List<FlankingParticipant> participants = new();
            foreach (KeyValuePair<CreatureId, CreatureState> entry in snapshot.Creatures)
            {
                CreatureId id = entry.Key;
                if (!creatures.TryGetValue(id, out CreatureComponent creature) || creature == null)
                    continue;

                GameObject candidate = creature.gameObject;
                Team candidateTeam = candidate.GetComponent<Team>();
                int reachFeet = GetBestMeleeReachFeet(candidate);
                participants.Add(
                    new FlankingParticipant(
                        id,
                        CanUseFlankingBoundary(candidate),
                        attackerTeam != null
                            && candidateTeam != null
                            && AreFriendly(attackerTeam.Name, candidateTeam.Name),
                        targetTeam != null
                            && candidateTeam != null
                            && AreFriendly(candidateTeam.Name, targetTeam.Name),
                        ThreatensTarget(candidate, targetObject, tiles, reachFeet)
                    )
                );
            }

            return FlankingRules.IsFlanking(
                snapshot,
                attacker,
                target,
                new FlankingContext(
                    attackerCanFlank,
                    targetObject.activeInHierarchy,
                    attackerThreatens,
                    participants
                )
            );
        }

        private static bool CanUseFlankingBoundary(GameObject combatant)
        {
            if (combatant == null || !combatant.activeInHierarchy)
                return false;

            ActionController controller = combatant.GetComponent<ActionController>();
            return controller != null && controller.enabled && GetBestMeleeReachFeet(combatant) > 0;
        }

        private static bool ThreatensTarget(
            GameObject attacker,
            GameObject target,
            Tile[,] tiles,
            int reachFeet
        )
        {
            if (attacker == null || target == null || reachFeet <= 0)
                return false;

            return StrikeTargeting.Evaluate(
                    attacker,
                    target,
                    tiles,
                    new StrikeTargetRequest
                    {
                        ReachFeet = reachFeet,
                        IsRanged = false,
                        RequiresLineOfEffect = true,
                    }
                ) != null;
        }

        private static int GetBestMeleeReachFeet(GameObject combatant)
        {
            ActionController controller = combatant?.GetComponent<ActionController>();
            if (controller == null)
                return 0;

            int reachFeet = 0;
            foreach (EntityAction action in controller.GetActions())
            {
                if (action is RulesStrikeAction rulesStrike)
                {
                    if (!rulesStrike.IsRanged)
                        reachFeet = Math.Max(reachFeet, rulesStrike.Item.ReachFeet);
                    continue;
                }

                if (
                    string.Equals(
                        action.ActionName,
                        "Unarmed Strike",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    reachFeet = Math.Max(reachFeet, DefaultUnarmedReachFeet);
            }

            return reachFeet;
        }

        private static IEnumerable<GameObject> GetGridCombatants(
            Tile[,] tiles,
            GameObject attacker,
            GameObject target
        )
        {
            HashSet<GameObject> seen = new();
            if (attacker != null && seen.Add(attacker))
                yield return attacker;
            if (target != null && seen.Add(target))
                yield return target;

            for (int x = 0; x < tiles.GetLength(0); x++)
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                Tile tile = tiles[x, z];
                if (tile == null)
                    continue;
                foreach (GameObject occupant in tile.Occupants)
                    if (occupant != null && seen.Add(occupant))
                        yield return occupant;
            }
        }

        private static GridPosition ToRulesPosition(GameObject combatant)
        {
            Vector3Int cell = Vector3Int.RoundToInt(combatant.transform.position);
            return new GridPosition(cell.x, cell.y, cell.z);
        }

        private static bool AreFriendly(string firstTeam, string secondTeam)
        {
            if (string.IsNullOrWhiteSpace(firstTeam) || string.IsNullOrWhiteSpace(secondTeam))
                return false;

            if (
                !TeamRules.TryGetInstance(out TeamRules teamRules)
                || !teamRules.Contains(firstTeam)
                || !teamRules.Contains(secondTeam)
            )
                return string.Equals(firstTeam, secondTeam, StringComparison.OrdinalIgnoreCase);

            return teamRules.IsFriendly(firstTeam, secondTeam);
        }

        private static Tile[,] TryGetTiles()
        {
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            return grid?.GetTiles();
        }
    }
}
