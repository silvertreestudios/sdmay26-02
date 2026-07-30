using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;

//TODO abstract AIActionConroller and make a subclass for mindless
public class MindlessController : AIActionController
{
    protected GridAPIPrivate GridAPI;
    protected IPathfinder Pathfinder;
    protected Tile[,] Tiles;
    private Coroutine turnSequence;
    private List<PathNode> plannedStridePath;

    private static readonly Vector3Int[] CardinalDirections = new[]
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(1, 0, 1),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(1, 0, -1),
        new Vector3Int(0, 0, 1),
        new Vector3Int(-1, 0, 1),
        new Vector3Int(0, 0, -1),
        new Vector3Int(-1, 0, -1),
    };

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
        if (GridAPI == null)
        {
            if (
                !GridPublic.GridAPI.TryGetInstance(out GridPublic.GridAPI activeGrid)
                || !(activeGrid is GridAPIPrivate privateGrid)
            )
            {
                base.StartTurn();
                return;
            }
            RebindGrid(privateGrid);
        }

        base.StartTurn();
        if (!isActiveAndEnabled)
            return;
        turnSequence = StartCoroutine(ExecuteTurnSequence());
    }

    /// <inheritdoc/>
    public override void ResetEncounterTurnState()
    {
        CancelTurnSequence();
        base.ResetEncounterTurnState();
    }

    internal void RebindGrid(GridAPIPrivate grid)
    {
        if (GridAPI != null && GridAPI != grid)
            return;

        GridAPI = grid;
        Tiles = grid.GetTiles();
        Pathfinder = grid.GetPathfinder();
        plannedStridePath = null;
        BestTarget = null;
    }

    internal bool CanBindToGrid(GridAPIPrivate grid)
    {
        return GridAPI == null || GridAPI == grid;
    }

    internal bool CanRebindGrid()
    {
        return !IsTurn && !IsTakingAction;
    }

    private IEnumerator ExecuteTurnSequence()
    {
        // Let StartTurn retain the coroutine handle before this sequence can complete or end a turn.
        yield return null;

        while (ActionPoints > 0)
        {
            //Debug.Log(ActionPoints + " action points remaining");
            EntityAction action = SelectNextAction();
            if (action != null)
            {
                uint actionsBeforeInvocation = ActionPoints;
                //wait for half a second as to not spam attacks
                yield return new WaitForSeconds(1f);
                // yield return waits for the TakeAction coroutine to completely finish
                if (action is RulesStrideAction)
                {
                    TakeAction(
                        action,
                        new PlannedStrideSelectionResolver(
                            plannedStridePath.Select(node => node.Location)
                        )
                    );
                }
                else
                {
                    TakeAction(action);
                }
                yield return new WaitUntil(() => !IsTakingAction); // Wait until the action is fully resolved before continuing
                if (ActionPoints >= actionsBeforeInvocation)
                {
                    // A rejected selection or dispatch makes no rules progress. Retrying the same
                    // deterministic decision would otherwise keep this turn alive forever.
                    break;
                }
                //Debug.Log("AI action finished");
            }
            else
            {
                //Debug.Log("No valid actions, ending turn");
                break;
            }
        }
        turnSequence = null;
        EndTurn();
        yield return null;
    }

    /// <summary>Selects the next action for the turn loop.</summary>
    /// <returns>The action to invoke, or no action when the turn should end.</returns>
    /// <remarks>
    /// Derived AI tests and future planners may replace the decision source without replacing the
    /// rules-progress guard owned by the turn loop.
    /// </remarks>
    protected virtual EntityAction SelectNextAction() => MindlessDecision();

    private void CancelTurnSequence()
    {
        if (turnSequence == null)
            return;

        StopCoroutine(turnSequence);
        turnSequence = null;
    }

    //TODO create a gridcharactercontorller3D api for this to use
    //TODO seriously we need a better way of accessing these calculation fucntions
    public EntityAction MindlessDecision()
    {
        // Reset persistent fields so stale paths from prior decisions don't affect this call
        plannedStridePath = null;
        BestTarget = null;

        Vector3Int currentCell = Vector3Int.RoundToInt(transform.position);
        Pathfinder.Search(this.gameObject, currentCell);
        TryGetCombatRules(out UnityCombatRulesBridge bridge, out CreatureId actor);
        Team actorTeam = GetComponent<Team>();
        int minDistance = int.MaxValue;

        foreach (GameObject target in CombatManagerInterface.GetInstance().GetCombatants())
        {
            if (target == this.gameObject)
                continue;
            Team targetTeam = target.GetComponent<Team>();
            bool friendly;
            if (bridge != null && target.TryGetComponent(out CreatureComponent targetCreature))
            {
                CreatureId targetId = bridge.GetCreatureId(targetCreature);
                friendly =
                    bridge.Snapshot.Creatures[actor].Player
                    == bridge.Snapshot.Creatures[targetId].Player;
            }
            else if (
                actorTeam != null
                && targetTeam != null
                && TeamRules.TryGetInstance(out TeamRules teamRules)
            )
            {
                friendly = teamRules.IsFriendly(actorTeam.Name, targetTeam.Name);
            }
            else
            {
                friendly =
                    actorTeam != null
                    && targetTeam != null
                    && string.Equals(
                        actorTeam.Name,
                        targetTeam.Name,
                        StringComparison.OrdinalIgnoreCase
                    );
            }
            if (friendly)
                continue;

            Vector3Int targetCell = Vector3Int.RoundToInt(target.transform.position);

            foreach (var dir in CardinalDirections)
            {
                Vector3Int neighborCell = targetCell + dir;

                // Make sure the neighbor cell is valid and unoccupied (or occupied by ourselves)
                if (
                    neighborCell.x < 0
                    || neighborCell.z < 0
                    || neighborCell.x >= Tiles.GetLength(0)
                    || neighborCell.z >= Tiles.GetLength(1)
                )
                    continue;

                Tile tile = Tiles[neighborCell.x, neighborCell.z];
                if (
                    tile == null
                    || (tile.Occupants.Count > 0 && !tile.Occupants.Contains(this.gameObject))
                )
                    continue;

                List<PathNode> path = Pathfinder.Find(neighborCell);

                if (path != null && path.Count > 0 && path.Count < minDistance)
                {
                    minDistance = path.Count;
                    plannedStridePath = path;
                    BestTarget = target;
                }
            }
        }

        EntityAction legalStrike = BestLegalStrike();
        if (legalStrike != null)
            return legalStrike;

        // Attack if best target is in strike range
        if (BestTarget != null)
        {
            List<Vector3Int> inRange = Pathfinder.CalculateEmination(currentCell, 5.0f);
            foreach (Vector3Int cell in inRange)
            {
                Tile tile = Tiles[cell.x, cell.z];
                if (tile != null && tile.Occupants.Contains(BestTarget))
                {
                    SelectedTile = cell;
                    return BestStrike();
                }
            }
        }

        if (plannedStridePath == null || plannedStridePath.Count == 0)
            return null;

        // Move towards the best target — select the furthest tile in path within movement range
        int maxMoveDist = this.gameObject.GetComponent<CreatureComponent>()?.speed ?? 0;

        if (plannedStridePath.Count > maxMoveDist / 5.0f * 3) // if the furthest reachable tile is beyound the total movement range for this turn take the shorter blocked path instead
        {
            Vector3Int targetCellAlt = Vector3Int.RoundToInt(BestTarget.transform.position);
            List<PathNode> altPath = DecideDetour(currentCell, targetCellAlt);
            if (altPath == null)
            {
                return null;
            }
            else if (altPath.Count > 0 && altPath.Count < plannedStridePath.Count)
            {
                plannedStridePath = altPath;
            }
        }

        return Actions.Find(action => action is RulesStrideAction);
    }

    private EntityAction BestLegalStrike()
    {
        if (!TryGetCombatRules(out UnityCombatRulesBridge bridge, out CreatureId actor))
            return null;
        if (!CombatManagerInterface.TryGetInstance(out CombatManagerInterface combatManager))
            return null;
        EntityAction bestAction = null;
        GameObject bestTarget = null;
        float bestDamage = 0;

        foreach (GameObject target in combatManager.GetCombatants())
        {
            if (target == this.gameObject)
                continue;

            foreach (EntityAction action in Actions)
            {
                if (
                    action is not RulesStrikeAction strike
                    || !strike.IsAvailable(this)
                    || target.GetComponent<CreatureComponent>()
                        is not CreatureComponent targetCreature
                )
                    continue;

                CreatureId targetId = bridge.GetCreatureId(targetCreature);
                if (!strike.CanPreviewTarget(bridge.Snapshot, actor, targetId))
                    continue;

                float damage = (float)strike.AverageDamage;
                if (damage > bestDamage)
                {
                    bestDamage = damage;
                    bestAction = action;
                    bestTarget = target;
                }
            }
        }

        if (bestAction != null)
        {
            BestTarget = bestTarget;
            SelectedTile = Vector3Int.RoundToInt(bestTarget.transform.position);
        }

        return bestAction;
    }

    /// <summary>
    /// when the best path to the target is blocked by other entities, this function determines if the AI can take a detour around the obstacle in one turn.
    /// if it takes longer than one turn to take the calculated best path, the AI will choose not to take the detour and get as close as possible to the target instead.
    /// </summary>
    private List<PathNode> DecideDetour(Vector3Int from, Vector3Int targetCell)
    {
        Pathfinder.Search(null, from);
        List<PathNode> directPath = Pathfinder.Find(targetCell);
        // remove the last cell from the list until the last cell is not occupied by anyone other than ourselves
        for (int i = directPath.Count - 1; i >= 0; i--)
        {
            Vector3Int cell = directPath[i].Location;
            if (Tiles[cell.x, cell.z].Occupants.Count > 0)
                directPath.RemoveAt(i);
            else
                break;
        }

        if (directPath == null || directPath.Count == 0)
            return null;

        Vector3Int lastCell = directPath[directPath.Count - 1].Location;
        // If the direct path is fully blocked by other entities, use best path instead
        Pathfinder.Search(this.gameObject, from);
        List<PathNode> bestAltPath = Pathfinder.Find(lastCell);
        if (bestAltPath != null && bestAltPath.Count < plannedStridePath.Count)
            return bestAltPath;
        else
            return plannedStridePath;
    }
}
