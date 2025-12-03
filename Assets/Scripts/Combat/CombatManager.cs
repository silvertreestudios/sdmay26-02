using UnityEngine;
using System.Collections.Generic;

public class CombatManager : CombatManagerInterface
{
    protected Queue<ActionController> combatants = new Queue<ActionController>();

    public override void AddCombatant(ActionController combatant)
    {
        combatants.Enqueue(combatant);
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
        e.StartTurn();
        combatants.Enqueue(e);
    }
}
