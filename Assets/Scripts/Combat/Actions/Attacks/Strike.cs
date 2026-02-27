using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Game.Creature;
using static UnityEngine.GraphicsBuffer;
using UnityEditor.Experimental.GraphView;

public class Strike
{
    public List<Dice> Damages;
    public List<DamageValue> FlatDamages;
    public Strike(List<Dice> damages, List<DamageValue> flatDamages)
    {
        Damages = damages ?? new();
        FlatDamages = flatDamages ?? new();
    }

    public void Damage(GameObject from_go, GameObject to_go)
    {
        // Get Data
        CreatureComponent from = from_go.GetComponent<CreatureComponent>();
        CreatureComponent to = to_go.GetComponent<CreatureComponent>();
        uint penalty = 5* (from_go.GetComponent<ActionController>()?.StrikePenalty ?? 0);
        int attackBonus = from.attackBonus;
        int damageBonus = from.damageBonus;

        // Hit Check
        D20Result attackRoll = D20.Roll(attackBonus -(int)penalty, to.ac);

        string log = "Attack:\n  AC: " + to.ac + "\n  Attack Roll: " + attackRoll.total 
            +" (" +attackRoll.roll +" + " +attackBonus + " - " + penalty +")" 
            +"\n  Result: "+attackRoll.degree ;
        if (attackRoll.degree == DegreeOfSuccess.Success || attackRoll.degree == DegreeOfSuccess.CriticalSuccess)
        {
            log += "\n  Damage: ";
            foreach (var d in Damages)
            {
                log += " " + d.numberOfDice + "d" + d.sidesPerDie +"+" +damageBonus + " " + d.damageType+", ";
            }
            // Adds a new flat damage for the damage bonus, type matching the first damage type
            FlatDamages.Add(new DamageValue(Damages[0].damageType, damageBonus));
            List<DamageValue> damageValues = DamageRoller.RollDamage(Damages, FlatDamages);
            DamageRoller.EvaluateCriticalDamage(attackRoll.degree, damageValues);
            DamageRoller.ApplyWeaknessAndResistance(damageValues, to.weaknesses, to.resistances);
            uint damage = (uint)DamageRoller.SumDamage(damageValues);
            to.TakeDamage(damage);
            log += "\nDamage Rolls: ";
            foreach (var d in damageValues)
            {
                log += "\n  " + d.DamageType + ": " + d.DamageAmount;
            }
            log += "\n  Total: " + damage;
        }
        log+= "\n";
        Debug.Log(log);
    }
}