using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;

public interface IDecisionMaker
{
    List<EntityAction> GetActions();

    List<EntityAction> GetMovements();

    GameObject GetTarget();

    bool CanHit(EntityAction action, GameObject target);

    void DecideAction();
}
