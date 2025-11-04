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
        List<(uint, ActionController)> initiatives = new List<(uint, ActionController)>();
        foreach (ActionController e in combatants)
        {
            initiatives.Add((e.GetInitiative(), e));
        }
        initiatives.Sort();
        combatants.Clear();
        foreach ((uint, ActionController) initiative in initiatives)
        {
            combatants.Enqueue(initiative.Item2);
        }
        NextTurn();
    }

    public override void NextTurn()
    {
        ActionController e = combatants.Dequeue();
        e.StartTurn();
        combatants.Enqueue(e);
    }
}
