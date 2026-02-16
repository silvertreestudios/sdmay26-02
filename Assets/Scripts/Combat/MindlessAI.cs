using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using System;

public class MindlessAI : ActionController
{
    private void Start()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);
        
        Stride strideAction = new Stride(1); // Cost of 1 action point
        Movements.Add(strideAction);

        List<Dice> dices = new() { new Dice(1, 3, "Bludgeoning") };

        Unarmed unarmed = new Unarmed(1, dices, new());
        Actions.Add(unarmed);
    }

    public override void StartTurn()
    {
        IsTurn = true;
        DecideAction();
    }

    public override void EndTurn()
    {
        IsTurn = false;
        IsTakingAction = false;
    }

    private void DecideAction()
    {
        // For a mindless AI, we can simply take the first available action or do nothing
        if (Actions.Count > 0)
        {
            TakeAction(Actions[0]);
        }
        else
        {
            EndTurn(); // No actions available, end turn immediately
        }
    }
}