using UnityEngine;
using System;
using System.Collections.Generic;
/*
// Struct for damage dice and type
public class DamageDice : Dice
{
    public string DamageType;  // e.g., "Piercing", "Slashing"

    public DamageDice(int num, int sides, string damageType) : base(num, sides)
    {
        DamageType = damageType;
    }
    public DamageDice(int num, int sides, int flat, string damageType) : base(num, sides, flat)
    {
        DamageType = damageType;
    }
}

// Input list of DamageInfo, output total rolled damage
public class DamageRoller
{
    // Roll damage for a single DamageDice
    public static List<DamageValue> RollDamage(DamageDice damageDice)
    {
        return RollDamage(new List<DamageDice> { damageDice }, new List<DamageValue>());
    }
    // Roll damage for a single DamageDice with a single flat DamageValue
    public static List<DamageValue> RollDamage(DamageDice damageDice, DamageValue damageFlat)
    {
        return RollDamage(new List<DamageDice> { damageDice }, new List<DamageValue> { damageFlat });
    }

    // Roll damage based on list of DamageInfo
    public static List<DamageValue> RollDamage(List<DamageDice> damageRolls, List<DamageValue> damageFlats)
    {
        List<DamageValue> damageInstances = new List<DamageValue>();

        // For each damageDice in damageRolls...
        foreach (DamageDice damageDice in damageRolls)
        {
            DamageValue damageValue = new DamageValue(damageDice.DamageType, damageDice.damageDice.Roll());
            // Group damage by type
            if (damageInstances.Exists(di => di.DamageType == damageValue.DamageType))
            {
                DamageValue existingInstance = damageInstances.Find(di => di.DamageType == damageValue.DamageType);
                existingInstance.DamageAmount += damageValue.DamageAmount;
            }
            else
            {
                damageInstances.Add(damageValue);
            }
        }
        // For each flat damage in damageFlats...
        foreach (DamageValue damageFlat in damageFlats)
        {
            // Group damage by type
            if (damageInstances.Exists(di => di.DamageType == damageFlat.DamageType))
            {
                DamageValue existingInstance = damageInstances.Find(di => di.DamageType == damageFlat.DamageType);
                existingInstance.DamageAmount += damageFlat.DamageAmount;
            }
            else
            {
                damageInstances.Add(damageFlat);
            }
        }
        // TODO: append traits list?
        return damageInstances;
    }

    public static int SumDamage(List<DamageValue> damageValues)
    {
        int totalDamage = 0;
        foreach (DamageValue dv in damageValues)
        {
            totalDamage += dv.DamageAmount;
        }
        return totalDamage;
    }

    public static void EvaluateCriticalDamage(DegreeOfSuccess attackRoll, List<DamageValue> damageValues)
    {
        if (attackRoll == DegreeOfSuccess.CriticalSuccess)
        {
            foreach (DamageValue dv in damageValues)
            {
                dv.DamageAmount *= 2;
            }
        }
    }

    public static void ApplyWeaknessAndResitance(List<DamageValue> incoming, List<DamageValue> weaknesses, List<DamageValue> resistances)
    {
        // for each incoming damage type, apply suitable weaknesses, resistances, and set to 0 if net negative
        foreach (DamageValue inc in incoming)
        {
            if (weaknesses.Exists(di => di.DamageType == inc.DamageType))
            {
                DamageValue existingInstance = weaknesses.Find(di => di.DamageType == inc.DamageType);
                inc.DamageAmount += existingInstance.DamageAmount;
            }
            if (resistances.Exists(di => di.DamageType == inc.DamageType))
            {
                DamageValue existingInstance = resistances.Find(di => di.DamageType == inc.DamageType);
                inc.DamageAmount -= existingInstance.DamageAmount;
            }
            if (inc.DamageAmount < 0) inc.DamageAmount = 0;
        }
    }

}*/