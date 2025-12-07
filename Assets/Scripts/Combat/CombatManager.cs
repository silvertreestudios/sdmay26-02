using UnityEngine;
using System.Collections.Generic;

public class CombatManager : CombatManagerInterface
{
    protected Queue<ActionController> combatants = new();
    protected ActionController TurnTaker = null;

    public override void AddCombatant(ActionController combatant)
    {
        combatants.Enqueue(combatant);
    }

    public override GameObject WhosTurn()
    {
        return TurnTaker.gameObject;
    }

    public override List<GameObject> GetCombatants()
    {
        List<GameObject> list = new();
        foreach(var c in combatants)
            list.Add(c.gameObject);
        return list;
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat()
    {
        Debug.Log("Start Combat.");

        NextTurn();
    }

    public override void NextTurn()
    {
        ActionController e = combatants.Dequeue();
        TurnTaker = e;
        e.StartTurn();
        combatants.Enqueue(e);
    }

    public override GameObject GetTarget(GameObject attacker)
    {
        foreach(var ac in combatants)
        {
            if (ac.GetInstanceID() != attacker.GetInstanceID())
                return ac.gameObject;
        }
        return null;
    }
}
