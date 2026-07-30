using System;
using Game.Creature;
using Game.Strikes;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;

//TODO abstract AIActionConroller and make a subclass for mindless
public abstract class AIActionController : ActionController
{
    public GameObject BestTarget { get; protected set; }
    public Vector3Int SelectedTile { get; protected set; }

    protected void Awake()
    {
        CombatManagerInterface.GetInstance().AddCombatant(this);

        AddAction(new RulesStrideAction());
    }

    /// <summary>
    /// Starts this creature's turn
    /// </summary>
    public override void StartTurn()
    {
        base.StartTurn();
    }

    // Changed from private to public so actions can call it
    [ContextMenu("End Turn")]
    public override void EndTurn()
    {
        if (HasTurnAuthority && !IsTakingAction)
        {
            Debug.Log("Turn End: " + this.gameObject.name);
            CombatLog.GetInstance().Log("- " + this.gameObject.name + " ended their turn.");
            CombatManagerInterface.GetInstance().EndCurrentTurn(this);
        }
    }

    /// <summary>
    /// Temporary function to invoke the strike action for testing.
    /// Can be triggered from the Unity editor during runtime via right-click menu.
    /// </summary>
    [ContextMenu("Test Strike")]
    public void TestStrike()
    {
        EntityAction strike = Actions.Find(action => action is RulesStrikeAction);
        if (strike != null)
        {
            Debug.Log("Invoking Strike action...");
            TakeAction(strike);
        }
    }

    public EntityAction BestStrike()
    {
        EntityAction bestStrike = null;
        float bestDamage = 0;
        foreach (EntityAction action in Actions)
        {
            if (action is not RulesStrikeAction strike || !strike.IsAvailable(this))
                continue;
            double damage = strike.AverageDamage;
            if (damage > bestDamage)
            {
                bestDamage = (float)damage;
                bestStrike = action;
            }
        }
        return bestStrike;
    }
}
