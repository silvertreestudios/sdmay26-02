using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
using Game.Creature;

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
        foreach (var c in TurnQueue)
            list.Add(c.Player.gameObject);

        GameObject a = list[list.Count - 1];
        list.Remove(a);
        list.Insert(0, a);
        return list;
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat()
    {
        RollInitiative();
        OnCombatStart.Invoke();
        //Debug.Log("Start Combat.");

        NextTurn();
    }

    /// <summary>
    /// Helper to order inititives.
    /// </summary>
    private void RollInitiative()
    {
        List<ActionController> turnOrder = new();
        List<uint> initiatives = new();
        
        // Insert all AC's in sorted turnOrder
        foreach (ActionController ac in Combatants) 
        {
            // Attempt to insert
            int i;
            uint initiative = ac.GetInitiative();
            for (i = 0; i < turnOrder.Count; i++)
            {
                if (initiatives[i] < initiative)
                {
                    initiatives.Insert(i, initiative);
                    turnOrder.Insert(i, ac);
                    break;
                }
            }
            // If no insertion, insert at end
            if(i == initiatives.Count)
            {
                initiatives.Add(initiative);
                turnOrder.Add(ac);
            }
        }
        // Clear TurnQueue and add by initiative
        TurnQueue.Clear();
        foreach(ActionController ac in turnOrder)
            TurnQueue.Add(new TurnStep(ac));
    }

    public override bool CheckForEndOfGame()
    {
        List<string> teams = new();
        // Check if a team has won yet.
        foreach (var combatant in Combatants)
        {
            string team = combatant.GetComponent<Team>().Name;
            if (!teams.Contains(team))
                teams.Add(team);
        }
        if (teams.Count < 2)
        {
            Debug.Log("Team " + teams[0] + " wins!");
            OnCombatEnd.Invoke(teams[0]);// Signal end
            if (teams[0].ToLower() == "players")
            {
                OnCombatOutcome.Invoke(true);
            }
            else
            {
                OnCombatOutcome.Invoke(false);
            }
            return true;
        }
        return false;
    }

    public override void NextTurn()
    {
        if(CheckForEndOfGame())
            return;
        // Take the next turn.
        TurnStep e = TurnQueue[0];
        TurnQueue.RemoveAt(0);
        if (e.Player)
        {
            TurnTaker = e.Player;
            OnNextTurn.Invoke(TurnTaker.gameObject);
        }
        e.Trigger();
        // Only re-queue if the combatant is still active (not killed during their turn)
        if (e.Player == null || e.Player.gameObject.activeSelf)
            TurnQueue.Add(e);
    }

    //added by Ryan Meyer 5/29/24, For cameraManager to get the positions of all tokens
    public Vector3[] getPoistions()
    {
        Vector3[] positions = new Vector3[Combatants.Count];
        int i = 0;
        foreach(var c in Combatants)
        {
            positions[i] = c.gameObject.transform.position;
            i++;
        }
        return positions;
    }
}