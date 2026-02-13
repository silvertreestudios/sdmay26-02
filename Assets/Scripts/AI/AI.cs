using UnityEngine;

public class AI : MonoBehaviour, IDecisionMaker
{

    private ActionController actionController;
    private Movement movement;

    public List<EntityAction> GetActions()
    {

    }

    public List<EntityAction> GetMovements()
    {

    }

    public GameObject GetTarget()
    {

    }

    public bool CanHit(EntityAction, GameObject target)
    {

    }

    public void DecideAction()
    {

    }

    //private List<GameObject> FindEnemiesInRange(float range);
    // range (work with adam) strike range

    private void moveToCell(Vector3Int targetCell)
    {

    }

    private void ExecuteAction(Entity action)
    {

    }
  

    //debug logging for actions, movement

    private Vector3Int FindClosestMoveToTarget(Vector3Int target)
    {

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
