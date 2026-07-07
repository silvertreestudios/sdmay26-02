using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using Game.Strikes;
using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine.TextCore.Text;
using GridPrivate;
using System.IO;
using System.Linq;
using System.Numerics;

//TODO abstract AIActionConroller and make a subclass for mindless
public class MindlessController : AIActionController
{
    protected GridAPIPrivate GridAPI;
    protected IPathfinder Pathfinder;
    protected Tile[,] Tiles;

    private static readonly Vector3Int[] CardinalDirections = new[]
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(1, 0, 1),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(1, 0, -1),
        new Vector3Int(0, 0, 1),
        new Vector3Int(-1, 0, 1),
        new Vector3Int(0, 0, -1),
        new Vector3Int(-1, 0, -1)
    };

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
        if(GridAPI == null)
        {
            GridAPI = (GridAPIPrivate)GridPublic.GridAPI.GetInstance();
            Tiles = GridAPI.GetTiles();
            Pathfinder = GridAPI.GetPathfinder();
        }

        base.StartTurn();
        StartCoroutine(ExecuteTurnSequence());
    }

    private IEnumerator ExecuteTurnSequence()
    {
        while (ActionPoints > 0)
        {
            //Debug.Log(ActionPoints + " action points remaining");
            EntityAction action = MindlessDecision();
            if (action != null)
            {
                //wait for half a second as to not spam attacks
                yield return new WaitForSeconds(1f);
                // yield return waits for the TakeAction coroutine to completely finish
                TakeAction(action);
                yield return new WaitUntil(() => !IsTakingAction); // Wait until the action is fully resolved before continuing
                //Debug.Log("AI action finished");
            }
            else
            {
                //Debug.Log("No valid actions, ending turn");
                break;
            }
        }
        EndTurn();
        yield return null;
    }




    //TODO create a gridcharactercontorller3D api for this to use
    //TODO seriously we need a better way of accessing these calculation fucntions
    public EntityAction MindlessDecision()
    {
        // Reset persistent fields so stale paths from prior decisions don't affect this call
        BestPath = null;
        BestTarget = null;

        Vector3Int currentCell = Vector3Int.RoundToInt(transform.position);
        Pathfinder.Search(this.gameObject, currentCell);
        string myTeam = this.gameObject.GetComponent<Team>().Name;
        int minDistance = int.MaxValue;

        foreach (GameObject target in CombatManagerInterface.GetInstance().GetCombatants())
        {
            if (target == this.gameObject || TeamRules.GetInstance().IsFriendly(myTeam, target.GetComponent<Team>().Name))
                continue;

            Vector3Int targetCell = Vector3Int.RoundToInt(target.transform.position);

            foreach (var dir in CardinalDirections)
            {
                Vector3Int neighborCell = targetCell + dir;
                
                // Make sure the neighbor cell is valid and unoccupied (or occupied by ourselves)
                if (neighborCell.x < 0 || neighborCell.z < 0 || 
                    neighborCell.x >= Tiles.GetLength(0) || neighborCell.z >= Tiles.GetLength(1))
                    continue;

                Tile tile = Tiles[neighborCell.x, neighborCell.z];
                if (tile == null || (tile.Occupants.Count > 0 && !tile.Occupants.Contains(this.gameObject)))
                    continue;

                List<PathNode> path = Pathfinder.Find(neighborCell);
                
                if (path != null && path.Count > 0 && path.Count < minDistance)
                {
                    minDistance = path.Count;
                    BestPath = path;
                    BestTarget = target;
                }
            }
        }

        EntityAction legalStrike = BestLegalStrike(myTeam);
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

        if (BestPath == null || BestPath.Count == 0)
            return null;

        // Move towards the best target — select the furthest tile in path within movement range
        int maxMoveDist = this.gameObject.GetComponent<CreatureComponent>()?.speed ?? 0;


        if (BestPath.Count > maxMoveDist / 5.0f * 3) // if the furthest reachable tile is beyound the total movement range for this turn take the shorter blocked path instead
        {
            Vector3Int targetCellAlt = Vector3Int.RoundToInt(BestTarget.transform.position);
            List<PathNode> altPath = DecideDetour(currentCell, targetCellAlt);
            if(altPath == null){
                return null;
            }
            else if (altPath.Count > 0 && altPath.Count < BestPath.Count){
                BestPath = altPath;
            } 
        }

        // Get cells within distance
        List<Vector3Int> reachableTiles = GetReachableCells(maxMoveDist, BestPath);

        int cellIndex = reachableTiles.Count - 1;
        while (cellIndex >= 0)
        {
            Vector3Int cell = reachableTiles[cellIndex];
            if (Tiles[cell.x, cell.z].Occupants.Count > 0)
                reachableTiles.RemoveAt(cellIndex);
            else
                break;
            cellIndex--;
        }
        
        int tileIndex = BestPath.FindLastIndex(tile => reachableTiles.Contains(tile.Location));
        SelectedTile = BestPath[tileIndex].Location;
        BestPath = BestPath.GetRange(0, tileIndex + 1);
        return Movements[0];
    }

    private EntityAction BestLegalStrike(string myTeam)
    {
        EntityAction bestAction = null;
        GameObject bestTarget = null;
        float bestDamage = 0;

        foreach (GameObject target in CombatManagerInterface.GetInstance().GetCombatants())
        {
            if (target == this.gameObject || TeamRules.GetInstance().IsFriendly(myTeam, target.GetComponent<Team>().Name))
                continue;

            foreach (EntityAction action in Actions)
            {
                if (action is not StrikeWeapon strikeWeapon || !strikeWeapon.IsUsableBy(gameObject))
                    continue;

                if (!strikeWeapon.CanStrikeTarget(gameObject, target, Tiles))
                    continue;

                float damage = strikeWeapon.GetStrike().GetAvgDmg();
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
        for (int i = directPath.Count - 1; i >= 0; i--){
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
        if (bestAltPath.Count < BestPath.Count && bestAltPath != null)
             return bestAltPath;
        else
             return BestPath;
    }

    /// <summary> 
    /// Returns the list of cells in the path that are within movement range. Assumes each step in the path is 5ft.
    /// </summary> 
    private List<Vector3Int> GetReachableCells(int maxMoveDist, List<PathNode> Path)
    {
        List<Vector3Int> reachableCells = new();
        for(int i = 0; i < Path.Count; i++)
        {
            if (Path[i].Dist <= maxMoveDist / 5.0f)
                reachableCells.Add(Path[i].Location);
            else
                break;
        }
        return reachableCells;
    }


}