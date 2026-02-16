using UnityEngine;
using System.Collections.Generic;

public class AI : MonoBehaviour, IDecisionMaker
{

    private PlayerActionController actionController;
    private Movement movement;

    public List<EntityAction> GetActions()
    {
        return null;
    }

    public List<EntityAction> GetMovements()
    {
        return null;
    }

    public GameObject GetTarget()
    {
        return null;
    }

    public bool CanHit(EntityAction action, GameObject target)
    {
        return false;
    }

    public void DecideAction()
    {

    }

    //private List<GameObject> FindEnemiesInRange(float range);
    // range (work with adam) strike range

    private void moveToCell(Vector3Int targetCell)
    {

    }

    private void ExecuteAction(EntityAction action)
    {

    }
  

    //debug logging for actions, movement

    private Vector3Int FindClosestMoveToTarget(Vector3Int target)
    {
        return Vector3Int.zero;
    }
    // get list of distances from grid
    
    
    
    // trigger all functionality on function calls 
    // abstract out to a class

    // basic AI derived from interface

    // reference dedicated pathfinding class

    // 

    // different ai factions fighting each other

    // targeting other ai entities
}
