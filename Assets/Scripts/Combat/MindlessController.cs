using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;
using System;
using System.Collections;
using UnityEngine.TextCore.Text;

//TODO abstract AIActionConroller and make a subclass for mindless
public class MindlessController : AIActionController
{


    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
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
        bestPath = null;
        bestTarget = null;

        Vector3Int currentCell = Controller.coordinateConverter.GetCharacterCell(this.gameObject);
        string myTeam = this.gameObject.GetComponent<Team>().Name;
        float minDistance = float.MaxValue;

        foreach (GameObject target in CombatManagerInterface.GetInstance().GetCombatants())
        {
            if (target == this.gameObject || TeamRules.GetInstance().IsFriendly(myTeam, target.GetComponent<Team>().Name))
                continue;

            Vector3Int targetCell = Controller.coordinateConverter.GetCharacterCell(target);
            if (!Controller.TryValidateAndGetPathAI(currentCell, targetCell, out var pathResult, ignoreTargetOccupancy: true))
                continue;

            // Subtract 2 to exclude the starting and ending cells
            if (pathResult?.Count > 0 && pathResult.Count - 2 < minDistance)
            {
                minDistance = pathResult.Count - 2;
                pathResult.RemoveAt(pathResult.Count - 1);
                bestPath = pathResult;
                bestTarget = target;
            }
        }

        // Attack if best target is in strike range
        if (Controller.StrikeOccupantsInArea(this.gameObject, 1).Contains(bestTarget))
        {
            selectedTile = Controller.coordinateConverter.GetCharacterCell(bestTarget);
            return Actions[0];
        }

        if (bestPath == null || bestPath.Count == 0)
            return null;

        // Move towards the best target — select the furthest tile in path within movement range
        int maxMoveDist = this.gameObject.GetComponent<CreatureComponent>()?.speed ?? 0;
        HashSet<Vector3Int> reachableTiles = Controller.rangeHighlighter.CalculateReachableTiles(currentCell, maxMoveDist / 5);
        int tileIndex = bestPath.FindLastIndex(tile => reachableTiles.Contains(tile));

        if (tileIndex < 0)
            return null;

        selectedTile = bestPath[tileIndex];
        bestPath = bestPath.GetRange(0, tileIndex + 1);
        return Movements[0];
    }
}