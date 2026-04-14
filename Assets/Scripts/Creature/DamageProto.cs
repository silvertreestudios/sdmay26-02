using UnityEngine;
//using System;
using System.Collections.Generic;
// using System.Diagnostics;

namespace Game.Creature
{

    // Tuple for damage type and amount
    [System.Serializable] public struct DamageValue
    {
        public string DamageType;
        public int DamageAmount;

        public DamageValue(string damageType, int damageAmount)
        {
            DamageType = damageType;
            DamageAmount = damageAmount;
        }
    }

    // Input list of Dice, output total rolled damage
    public class DamageRoller
    {
        // Roll damage for a single Dice
        public static List<DamageValue> RollDamage(Dice dice){
            return RollDamage(new List<Dice> { dice }, new List<DamageValue>());
        }
        // Roll damage for a single Dice with a single flat DamageValue
        public static List<DamageValue> RollDamage(Dice dice, DamageValue damageFlat){
            return RollDamage(new List<Dice> { dice }, new List<DamageValue> { damageFlat });
        }

        // Roll damage based on list of Dice
        public static List<DamageValue> RollDamage(List<Dice> damageRolls, List<DamageValue> damageFlats){
            List<DamageValue> damageInstances = new List<DamageValue>();
            string log = "  Damage: ";
            log += "\n  Rolled ";
            foreach (Dice dice in damageRolls){
                DamageValue damageValue = new DamageValue(dice.damageType, dice.Roll());
                string damageTypeCapitalized = char.ToUpper(damageValue.DamageType[0]) + damageValue.DamageType.Substring(1);
                log += " "+dice.numberOfDice+"d"+dice.sidesPerDie+": "+damageValue.DamageAmount+" " + damageTypeCapitalized + ", ";
                // Group damage by type (string comparison)
                if (damageInstances.Exists(di => di.DamageType == damageValue.DamageType)){
                    int idx = damageInstances.FindIndex(di => di.DamageType == damageValue.DamageType);
                    var existingInstance = damageInstances[idx];
                    existingInstance.DamageAmount += damageValue.DamageAmount;
                    damageInstances[idx] = existingInstance;
                }
                else{
                    damageInstances.Add(damageValue);
                }

            }
            log += "\n       ";
            // For each flat damage in damageFlats...
            foreach (DamageValue damageFlat in damageFlats){
                // Group damage by type (string comparison)
                string damageTypeCapitalized = char.ToUpper(damageFlat.DamageType[0]) + damageFlat.DamageType.Substring(1);
                log += " +"+damageFlat.DamageAmount + " " + damageTypeCapitalized + ", ";
                if (damageInstances.Exists(di => di.DamageType == damageFlat.DamageType)){
                    int idx = damageInstances.FindIndex(di => di.DamageType == damageFlat.DamageType);
                    var existingInstance = damageInstances[idx];
                    existingInstance.DamageAmount += damageFlat.DamageAmount;
                    damageInstances[idx] = existingInstance;
                }
                else{
                    damageInstances.Add(damageFlat);
                }
            }
            // TODO: append traits list?

            Debug.Log(log);
            CombatLog.GetInstance().Log(log);
            return damageInstances;
        }

        public static int SumDamage(List<DamageValue> damageValues){
            int totalDamage = 0;
            foreach (DamageValue dv in damageValues){
                totalDamage += dv.DamageAmount;
            }
            CombatLog.GetInstance().Log("  Total: " + totalDamage +" Damage!");
            return totalDamage;
        }

        public static void EvaluateCriticalDamage(DegreeOfSuccess attackRoll, List<DamageValue> damageValues){
            if (attackRoll == DegreeOfSuccess.CriticalSuccess){
                for (int i = 0; i < damageValues.Count; i++)
                {
                    var dv = damageValues[i];
                    dv.DamageAmount *= 2;
                    damageValues[i] = dv;
                }
            }
            CombatLog.GetInstance().Log("  x2 for Critical Hit!");
        }

        // Called by creature receiving damage via TakeDamage
        public static void ApplyWeaknessAndResistance(List<DamageValue> incoming, List<DamageValue> weaknesses, List<DamageValue> resistances){
            // for each incoming damage type, apply suitable weaknesses, resistances, and set to 0 if net negative
            for (int i = 0; i < incoming.Count; i++)
            {
                var inc = incoming[i];
                if (weaknesses.Exists(di => di.DamageType == inc.DamageType)){
                    //Debug.Log(""+inc.DamageAmount+" "+inc.DamageType+" incoming, applying weaknesses:");
                    var existingInstance = weaknesses.Find(di => di.DamageType == inc.DamageType);
                    inc.DamageAmount += existingInstance.DamageAmount;
                    CombatLog.GetInstance().Log("  +" + existingInstance.DamageAmount + " " + char.ToUpper(existingInstance.DamageType[0]) + existingInstance.DamageType.Substring(1) + " for weakness!");
                }
                if (resistances.Exists(di => di.DamageType == inc.DamageType)){
                    var existingInstance = resistances.Find(di => di.DamageType == inc.DamageType);
                    inc.DamageAmount -= existingInstance.DamageAmount;
                    CombatLog.GetInstance().Log("  -" + existingInstance.DamageAmount + " " + char.ToUpper(existingInstance.DamageType[0]) + existingInstance.DamageType.Substring(1) + " for resistance!");
                }
                if (inc.DamageAmount < 0) inc.DamageAmount = 0;
                incoming[i] = inc;
            }
        }
    }
}