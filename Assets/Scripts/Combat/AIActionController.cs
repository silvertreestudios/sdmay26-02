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
        if (TryCompleteTurn())
        {
            Debug.Log("Turn End: " + this.gameObject.name);
            CombatLog.GetInstance().Log("- " + this.gameObject.name + " ended their turn.");
            // Clean up turn state
            // I.E. UI, etc
            CombatManagerInterface.GetInstance().NextTurn();
            OnGameplayStateCommitted.Invoke();
        }
    }

    /// <summary>
    /// Temporary function to invoke the strike action for testing.
    /// Can be triggered from the Unity editor during runtime via right-click menu.
    /// </summary>
    [ContextMenu("Test Strike")]
    public void TestStrike()
    {
        EntityAction strike = Actions.Find(action => action is Unarmed || action is StrikeWeapon);
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
            float damage = 0;
            if (action is Unarmed)
            {
                Unarmed unarmedAction = action as Unarmed;
                damage = unarmedAction.GetStrikeProfile().GetAverageDamage();
            }
            else if (action is StrikeWeapon)
            {
                StrikeWeapon strikeWeaponAction = action as StrikeWeapon;
                if (!strikeWeaponAction.IsUsableBy(gameObject))
                    continue;
                damage = strikeWeaponAction.GetStrikeProfile().GetAverageDamage();
            }
            if (damage > bestDamage)
            {
                bestDamage = damage;
                bestStrike = action;
            }
        }
        return bestStrike;
    }
}
