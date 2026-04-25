using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine.TextCore.Text;
using GridPrivate;

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
        Pathfinder.Search(null, currentCell);
        string myTeam = this.gameObject.GetComponent<Team>().Name;
        float minDistance = float.MaxValue;

        foreach (GameObject target in CombatManagerInterface.GetInstance().GetCombatants())
        {
            if (target == this.gameObject || TeamRules.GetInstance().IsFriendly(myTeam, target.GetComponent<Team>().Name))
                continue;

            Vector3Int targetCell = Vector3Int.RoundToInt(target.transform.position);
            List<PathNode> path = Pathfinder.Find(targetCell);
            if (path == null || path.Count < 2)
                continue;

            // Subtract 2 to exclude the starting and ending cells
            if (path.Count > 0 && path.Count - 2 < minDistance)
            {
                minDistance = path.Count - 2;
                path.RemoveAt(path.Count - 1);
                BestPath = path;
                BestTarget = target;
            }
        }

        // Attack if best target is in strike range
        List<Vector3Int> inRange = Pathfinder.CalculateEmination(currentCell, 5.0f);
        foreach (Vector3Int cell in inRange)
        {
            if (Tiles[cell.x, cell.z] != null && Tiles[cell.x, cell.z].Occupants.Contains(BestTarget))
            {
                SelectedTile = cell;
                return BestStrike();
            }
        }

        if (BestPath == null || BestPath.Count == 0)
            return null;

        // Move towards the best target — select the furthest tile in path within movement range
        int maxMoveDist = this.gameObject.GetComponent<CreatureComponent>()?.speed ?? 0;

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

        if (tileIndex < 0)
        {
            // Direct path is fully blocked by a teammate — try an unoccupied neighboring tile of the target
            Vector3Int targetCellAlt = Vector3Int.RoundToInt(BestTarget.transform.position);
            List<PathNode> altPath = FindAlternativeApproachPath(currentCell, targetCellAlt);
            if (altPath == null || altPath.Count == 0)
                return null;
            SelectedTile = altPath[altPath.Count - 1].Location;
            BestPath = altPath;
            return Movements[0];
        }

        SelectedTile = BestPath[tileIndex].Location;
        BestPath = BestPath.GetRange(0, tileIndex + 1);
        return Movements[0];
    }

    /// <summary>
    /// When the direct path to a target is blocked by a teammate, finds the shortest path to
    /// an unoccupied cardinal neighbor of the target that the AI can make progress toward.
    /// </summary>
    private List<PathNode> FindAlternativeApproachPath(Vector3Int from, Vector3Int targetCell)
    {
        List<PathNode> bestAltPath = null;
        int bestAltDist = int.MaxValue;
        int maxMoveDist = this.gameObject.GetComponent<CreatureComponent>()?.speed ?? 0;

        foreach (var dir in CardinalDirections)
        {
            Vector3Int altDest = targetCell + dir;
            if (altDest == from) continue;

            // Only consider tiles that are unoccupied and valid to land on
            if (
                altDest.x < 0 || 
                altDest.z < 0 || 
                altDest.x >= Tiles.GetLength(0) || 
                altDest.z >= Tiles.GetLength(1) ||
                Tiles[altDest.x, altDest.z] == null ||
                Tiles[altDest.x, altDest.z].Occupants.Count > 0
            ) continue;

            List<PathNode> altPath = Pathfinder.Find(altDest);
            if (altPath == null || altPath.Count < 2)
                continue;

            List<Vector3Int> altReachable = GetReachableCells(maxMoveDist, altPath);
            
            if (altReachable.Count < 1) continue; // Need at least one step of forward progress

            if (altPath.Count < bestAltDist)
            {
                bestAltDist = altPath.Count;
                bestAltPath = altPath.GetRange(0, altReachable.Count);
            }
        }

        return bestAltPath;
    }

    /// <summary> 
    /// Returns the list of cells in the path that are within movement range. Assumes each step in the path is 5ft.
    /// </summary> 
    private List<Vector3Int> GetReachableCells(int maxMoveDist, List<PathNode> Path)
    {
        List<Vector3Int> reachableCells = new();
        for(int i = 0; i < Path.Count; i++)
        {
            if (Path[i].Dist <= maxMoveDist / 5)
                reachableCells.Add(Path[i].Location);
            else
                break;
        }
        return reachableCells;
    }


}