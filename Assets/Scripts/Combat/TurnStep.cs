using UnityEngine;
using UnityEngine.Events;

public class TurnStep
{
    bool IsPlayer;
    public ActionController Player { get; private set; }
    public UnityEvent Event { get; private set; }
    int EventDelay;

    public TurnStep (ActionController player)
    {
        IsPlayer = true;
        Player = player;
    }
    public TurnStep(UnityEvent e, int eventDelay)
    {
        EventDelay = eventDelay;
        IsPlayer= false;
        Event = e;
    }

    public void Trigger () 
    { 
        if(IsPlayer)
            Player.StartTurn();
        else if (EventDelay-- <= 0)
        {
            Event.Invoke();
            CombatManager.Remove(this);
        }

    }
}
