using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Game.Creature;
using static UnityEngine.GraphicsBuffer;
using UnityEditor.Experimental.GraphView;
using System;
using Unity.VisualScripting;


public class Strike
{
    public List<Dice> Damages;
    public List<DamageValue> FlatDamages;
    public List<string> Traits = new List<string>();
    public GameObject From = null; // for event listeners
    public GameObject To = null; // for event listeners

    public Strike(List<Dice> damages, List<DamageValue> flatDamages)
    {
        Damages = damages ?? new();
        FlatDamages = flatDamages ?? new();
    }
    public Strike(Strike strike)
    {
        this.Damages = new List<Dice>();
        // Deep Copy Damages
        foreach(Dice dice in strike.Damages)
            this.Damages.Add(new Dice(dice.numberOfDice, dice.sidesPerDie, dice.damageType));
        this.FlatDamages = new List<DamageValue>(strike.FlatDamages);
        this.Traits = new List<string>(strike.Traits);
    }

    public void Damage(GameObject from_go, GameObject to_go)
    {
        // Create new strike that can be modified by listeners without affecting original action data
        Strike evaluated = new Strike(this);
        evaluated.From = from_go;
        evaluated.To = to_go;
        
        // TEMP for testing strike effects
        // Debug.Log("Strike pre-eval: " + this.ToString());
        // Debug.Log("Strike post-eval: " + evaluated.ToString());
        OnStrikeEvent.Invoke(new(evaluated, from_go)); 
        // Debug.Log("Strike post-rage: " + evaluated.ToString());

        evaluated.DamageEvaluate();
    }

    protected void DamageEvaluate()
    {
        // Get Data
        CreatureComponent from = From.GetComponent<CreatureComponent>();
        CreatureComponent to = To.GetComponent<CreatureComponent>();
        uint penalty = 5 * (From.GetComponent<ActionController>()?.StrikePenalty ?? 0);

        // TODO calculate actual bonuses
        int attackBonus = from.attackBonus;
        int damageBonus = from.damageBonus;

        // Hit Check
        D20Result attackRoll = D20.Roll(attackBonus - (int)penalty, to.ac);

        string log = "Attack:\n  AC: " + to.ac + "\n  Attack Roll: " + attackRoll.total
            + " (" + attackRoll.roll + " + " + attackBonus + " - " + penalty + ")"
            + "\n  Result: " + attackRoll.degree;

        if (attackRoll.degree == DegreeOfSuccess.Success || attackRoll.degree == DegreeOfSuccess.CriticalSuccess)
        {
            OnDamageDealt.Invoke(Damages[0].damageType);
            // Adds a new flat damage for the damage bonus, type matching the first damage type
            FlatDamages.Add(new DamageValue(Damages[0].damageType, damageBonus));
            List<DamageValue> damageValues = DamageRoller.RollDamage(Damages, FlatDamages);
            DamageRoller.EvaluateCriticalDamage(attackRoll.degree, damageValues);
            DamageRoller.ApplyWeaknessAndResistance(damageValues, to.weaknesses, to.resistances);
            uint damage = (uint)DamageRoller.SumDamage(damageValues);
            to.TakeDamage(damage);
            log += "\nDamage Dealt: ";
            foreach (var d in damageValues)
            {
                log += "\n  " + d.DamageType + ": " + d.DamageAmount;
            }
            log += "\n  Total: " + damage;
        } else {
            OnAttackMiss.Invoke(From);
            log += "\nAttack Missed!";
        }
        log += "\n";
        // Debug.Log(log);
        CombatLog.GetInstance().Log(log);
    }

    public List<string> getTraits()
    {
        return Traits;
    }
    public String ToString()
    {
        string traits = "";
        foreach (string trait in Traits)
        {
            traits += trait + " ";
        }
        return "Strike Action: " + Damages.Count + " damage rolls, " + FlatDamages.Count + " flat damages, Traits: " + traits;
    }
}

public class OnStrikeEvent : StaticUnityEvent<OnStrikeEvent, Tuple<Strike, GameObject>>{ }
