using UnityEngine;
using System.Collections.Generic;

public class CombatManager : CombatManagerInterface
{
    protected Queue<ActionController> combatants = new Queue<ActionController>();
    protected ActionController TurnTaker = null;

    public override void AddCombatant(ActionController combatant)
    {
        combatants.Enqueue(combatant);
    }

    public override GameObject WhosTurn()
    {
        return TurnTaker.gameObject;
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
