using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using NUnit.Framework;
using System;

//TODO abstract AIActionConroller and make a subclass for mindless
public abstract class MindlessController : AIActionController
{
    protected GridCharacterController3D Controller => GridCharacterController3D.Instance;
    

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
        IsTurn = true;
        ActionPoints = 3;
        //rename to decide action or something
        TakeAction(MindlessDecision());
        //TODO takeaction(action)
        // Provide Options
        // Prompt user or AI for action
        Debug.Log("Turn: " + this.gameObject.name);
    }

//TODO create a gridcharactercontorller3D api for this to use
//TODO seriously we need a better way of accessing these calculation fucntions
    public EntityAction MindlessDecision()
    {
        // we need that api to avoid calls like this one vvv, unless this is fine, ask Cole
        Vector3Int currentCell = Controller.coordinateConverter.GetCharacterCell(this.gameObject);
        float minDistance = float.MaxValue;
        GameObject bestTarget = null;
        List<Vector3Int> bestPath = null;
        //find closest target, move towards them, and strike if in range
        List<GameObject> targets = CombatManagerInterface.GetInstance().GetCombatants();
        foreach (GameObject target in targets)
        {
            if (target != this.gameObject)
            {
                // find closest target using pathfinding
                //GridPathfinder
                var pathResult = Controller.pathfinder.FindPath(currentCell, Controller.coordinateConverter.GetCharacterCell(target));
                float distance = pathResult.distance;
                if (distance < minDistance){
                    minDistance = distance;
                    bestPath = pathResult.path;
                    bestTarget = target;

                }
            }
        }
        //check if in strike range
        //GridCharacterController3D
        List<GameObject> occupantsInRange = Controller.GetOccupantsInArea(this.gameObject, 1);

        if (occupantsInRange.Contains(bestTarget))
        {
            //<call fsm to take action>
            return Actions[0];
        }
        else
        {
            // Move towards the best target, select last tile in path within movement range
            // MovementRange
            //TODO need a way to define movement and combat range on a character instance
            HashSet<Vector3Int> currentReachableTiles = Controller.rangeHighlighter.CalculateReachableTiles(currentCell, 2);
            for (int i = bestPath.Count - 1; i >= 0; i--)
            {
                Vector3Int tile = bestPath[i];
                if (currentReachableTiles.Contains(tile))
                {
                    // Move to this tile
                    //<call fsm to take action>
                    return Movements[0];
                }
            }
           
        }
        return null;
    }
}
