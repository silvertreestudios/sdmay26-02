using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;
using System;
using System.Collections;

//TODO abstract AIActionConroller and make a subclass for mindless
public class MindlessController : AIActionController
{  
    

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
        IsTurn = true;
        ActionPoints = 3;
        Debug.Log("Turn: " + this.gameObject.name);
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
        bestPath = null;
        bestTarget = null;

        // we need that api to avoid calls like this one vvv, unless this is fine, ask Cole
        Vector3Int currentCell = Controller.coordinateConverter.GetCharacterCell(this.gameObject);
        float minDistance = float.MaxValue;
        //find closest target, move towards them, and strike if in range
        List<GameObject> targets = CombatManagerInterface.GetInstance().GetCombatants();
        // Define neighbor offsets for 8 surrounding tiles
        Vector3Int[] neighborOffsets = {
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, -1),
            new Vector3Int(0, 0, -1), new Vector3Int(-1, 0, -1), new Vector3Int(-1, 0, 0), new Vector3Int(-1, 0, 1)
        };

        //TODO kinda clunky, the best way to do this is to make the pathfind check ignore the iswalkable flag on the enemy tile and just pathfind to bestPath-1
        foreach (GameObject target in targets)
        {
            if (target != this.gameObject)
            {
                Vector3Int targetCell = Controller.coordinateConverter.GetCharacterCell(target);

                // Check paths to all neighbors of the target
                foreach (Vector3Int offset in neighborOffsets)
                {
                    Vector3Int neighborCell = targetCell + offset;

                    // find closest target neighbor using pathfinding
                    List<Vector3Int> pathResult = null;
                    if(!Controller.TryValidateAndGetPathAI(currentCell, neighborCell, out pathResult))
                    {
                        continue;
                    }
                    
                    // Ensure path is valid and reachable
                    if (pathResult != null && pathResult.Count > 0)
                    {
                        float distance = pathResult.Count - 1; // Subtract 1 to exclude the starting cell
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            bestPath = pathResult;
                            bestTarget = target;
                        }
                    }
                }
            }
        }
        //check if in strike range
        //GridCharacterController3D
        List<GameObject> occupantsInRange = Controller.GetOccupantsInArea(this.gameObject, 1);
        //temp fix until we get highlights out of getoccupantsinarea
        //Controller.rangeHighlighter.ClearHighlights();

        if (occupantsInRange.Contains(bestTarget))
        {
            selectedTile = Controller.coordinateConverter.GetCharacterCell(bestTarget);
            //<call fsm to take action>
            return Actions[0];
        }
        else if (occupantsInRange.Count > 0)
        {
            // this covers the edge case where the mindless creature is next to the target in a 1 tile wide corridor
            // because the itle the creature and target are stating on are not walkable there is a case where it cant
            // "pathfind" to the target because there are no "walkable" tiles between the creature and target
            //TODO need to add team functionality to this dececision tree after merge
            selectedTile = Controller.coordinateConverter.GetCharacterCell(occupantsInRange[0]); 
            bestTarget = occupantsInRange[0];
            return Actions[0];           
        }
        else
        {
            if (bestPath == null || bestPath.Count == 0)
            {
                return null;
            }
            // Move towards the best target, select last tile in path within movement range
            // MovementRange
            //TODO need a way to define movement and combat range on a character instance
            HashSet<Vector3Int> currentReachableTiles = Controller.rangeHighlighter.CalculateReachableTiles(currentCell, Controller.maxMovementDistance);
            for (int i = bestPath.Count - 1; i >= 0; i--)
            {
                Vector3Int tile = bestPath[i];
                if (currentReachableTiles.Contains(tile))
                {
                    // Move to this tile
                    selectedTile = tile;
                    //truncate path to selected tile
                    bestPath = bestPath.GetRange(0, i + 1);
                    //<call fsm to take action>
                    return Movements[0];
                }
            }
           
        }
        return null;
    }
}
