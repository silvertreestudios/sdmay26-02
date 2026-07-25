using System;
using System.Collections.Generic;
using Game.Creature;
using Game.Strikes;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Combat.Rules
{
    public static class FlankingRule
    {
        private const int DefaultUnarmedReachFeet = 5;

        public static bool GrantsOffGuardToMeleeAttack(
            GameObject attacker,
            GameObject target,
            StrikeProfile strike
        )
        {
            if (attacker == null || target == null || strike == null || strike.IsRangedAttack)
                return false;

            Tile[,] tiles = TryGetTiles();
            return IsFlanking(
                attacker,
                target,
                tiles,
                Math.Max(DefaultUnarmedReachFeet, strike.ReachFeet)
            );
        }

        public static bool IsFlanking(
            GameObject attacker,
            GameObject target,
            Tile[,] tiles,
            int attackerReachFeet = DefaultUnarmedReachFeet
        )
        {
            if (!CanFlank(attacker) || target == null || !target.activeInHierarchy)
                return false;

            if (!ThreatensTarget(attacker, target, tiles, attackerReachFeet))
                return false;

            Team attackerTeam = attacker.GetComponent<Team>();
            Team targetTeam = target.GetComponent<Team>();
            if (
                attackerTeam == null
                || targetTeam == null
                || AreFriendly(attackerTeam.Name, targetTeam.Name)
            )
                return false;

            Vector3Int attackerCell = CellOf(attacker);
            Vector3Int targetCell = CellOf(target);

            foreach (GameObject ally in GetCombatants(tiles))
            {
                if (ally == null || ally == attacker || ally == target)
                    continue;
                if (!CanFlank(ally))
                    continue;

                Team allyTeam = ally.GetComponent<Team>();
                if (
                    allyTeam == null
                    || !AreFriendly(attackerTeam.Name, allyTeam.Name)
                    || AreFriendly(allyTeam.Name, targetTeam.Name)
                )
                    continue;

                if (!ThreatensTarget(ally, target, tiles, GetBestMeleeReachFeet(ally)))
                    continue;
                if (IsOppositeSideOrCorner(attackerCell, CellOf(ally), targetCell))
                    return true;
            }

            return false;
        }

        private static bool CanFlank(GameObject combatant)
        {
            if (combatant == null || !combatant.activeInHierarchy)
                return false;

            ActionController controller = combatant.GetComponent<ActionController>();
            CreatureComponent creature = combatant.GetComponent<CreatureComponent>();
            return controller != null
                && controller.enabled
                && creature != null
                && creature.hp > 0
                && GetBestMeleeReachFeet(combatant) > 0;
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

            StrikeTargetRequest request = new()
            {
                ReachFeet = reachFeet,
                IsRanged = false,
                RequiresLineOfEffect = true,
            };

            if (tiles != null)
                return StrikeTargeting.Evaluate(attacker, target, tiles, request) != null;

            return StrikeTargeting.IsWithinStrikeRange(CellOf(attacker), CellOf(target), request);
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

        private static IEnumerable<GameObject> GetCombatants(Tile[,] tiles)
        {
            HashSet<GameObject> seen = new();
            if (tiles != null)
            {
                for (int x = 0; x < tiles.GetLength(0); x++)
                {
                    for (int z = 0; z < tiles.GetLength(1); z++)
                    {
                        Tile tile = tiles[x, z];
                        if (tile == null)
                            continue;

                        foreach (GameObject occupant in tile.Occupants)
                        {
                            if (occupant != null && seen.Add(occupant))
                                yield return occupant;
                        }
                    }
                }
                yield break;
            }

            foreach (
                ActionController controller in UnityEngine.Object.FindObjectsByType<ActionController>(
                    FindObjectsSortMode.None
                )
            )
            {
                if (controller != null && seen.Add(controller.gameObject))
                    yield return controller.gameObject;
            }
        }

        private static bool IsOppositeSideOrCorner(
            Vector3Int attackerCell,
            Vector3Int allyCell,
            Vector3Int targetCell
        )
        {
            Vector2Int attackerDirection = DirectionFromTarget(attackerCell, targetCell);
            Vector2Int allyDirection = DirectionFromTarget(allyCell, targetCell);
            if (attackerDirection == Vector2Int.zero || allyDirection == Vector2Int.zero)
                return false;

            return attackerDirection.x == -allyDirection.x
                && attackerDirection.y == -allyDirection.y;
        }

        private static Vector2Int DirectionFromTarget(Vector3Int cell, Vector3Int targetCell)
        {
            return new Vector2Int(
                Math.Sign(cell.x - targetCell.x),
                Math.Sign(cell.z - targetCell.z)
            );
        }

        private static Vector3Int CellOf(GameObject go)
        {
            return Vector3Int.RoundToInt(go.transform.position);
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
