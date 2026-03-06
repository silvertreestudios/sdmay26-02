using UnityEngine;
using UnityEngine.Events;

public class TurnStep
{
    bool IsPlayer;
    public ActionController Player { get; private set; }
    public UnityAction Event { get; private set; }
    int EventDelay;

    public TurnStep (ActionController player)
    {
        IsPlayer = true;
        Player = player;
    }
    public TurnStep(UnityAction callback)
    {
        Event = callback;
        IsPlayer= false;
        EventDelay = 0;
    }
    public TurnStep(UnityAction callback, int eventDelay)
    {
        Event = callback;
        IsPlayer = false;
        EventDelay = eventDelay;
    }

    public void Trigger () 
    { 
        if(IsPlayer)
            Player.StartTurn();
        else if (EventDelay-- <= 0)
        {
            Event.Invoke();
            CombatManagerInterface.GetInstance().Remove(this);
        }
    }
}