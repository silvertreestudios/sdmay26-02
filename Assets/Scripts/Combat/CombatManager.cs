using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

public class CombatManager : CombatManagerInterface
{
    // Fields
    protected List<ActionController> Combatants = new();
    protected List<TurnStep> TurnQueue = new();
    protected ActionController TurnTaker = null;

    // Events
    // See CombatEvents.cs for a list of events triggered by this class

    public override void AddCombatant(ActionController combatant)
    {
        OnCombatantJoin.Invoke(combatant.gameObject);
        Combatants.Add(combatant);
        TurnQueue.Add(new TurnStep(combatant));
    }

    public override void AddEvent(TurnStep ts)
    {
        TurnQueue.Add(ts);
    }

    public override void Remove(ActionController combatant)
    {
        Combatants.Remove(combatant);
        for(int i = 0; i < TurnQueue.Count; i++) 
        {
            if (TurnQueue[i].Player == combatant)
                TurnQueue.RemoveAt(i);
        }
    }

    public override void Remove(TurnStep e)
    {
        for (int i = 0; i < TurnQueue.Count; i++)
        {
            if (TurnQueue[i] == e)
                TurnQueue.RemoveAt(i);
        }
    }

    public override void Remove(UnityAction e)
    {
        for (int i = 0; i < TurnQueue.Count; i++)
        {
            if (TurnQueue[i].Event == e)
                TurnQueue.RemoveAt(i);
        }
    }

    public override GameObject WhosTurn()
    {
        return TurnTaker.gameObject;
    }

    public override List<GameObject> GetCombatants()
    {
        List<GameObject> list = new();
        foreach (var c in Combatants)
            list.Add(c.gameObject);
        return list;
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat()
    {
        OnCombatStart.Invoke();
        Debug.Log("Start Combat.");

        NextTurn();
    }

    public override void NextTurn()
    {
        TurnStep e = TurnQueue[0];
        TurnQueue.RemoveAt(0);
        if (e.Player)
        {
            TurnTaker = e.Player;
            OnNextTurn.Invoke(TurnTaker.gameObject);
        }
        e.Trigger();
        TurnQueue.Add(e);
    }
}