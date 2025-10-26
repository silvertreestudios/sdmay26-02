using UnityEngine;
using System;
using Game.Dice;

namespace Game.Damage
{
    public enum DamageType
    {
        Bludgeoning,
        Piercing,
        Slashing,
        Fire
    }

    // Struct for damage dice and type
    public struct DamageDice
    {
        public Dice DamageDice;        // Dice object for rolled damage
        public DamageType DamageType;  // e.g., "Piercing", "Slashing"

        public DamageDice(Dice damageDice, DamageType damageType)
        {
            DamageDice = damageDice;
            DamageType = damageType;
        }
    }

    // Tuple for damage type and amount
    public struct DamageValue
    {
        public DamageType DamageType;
        public int DamageAmount;

        public DamageValue(DamageType damageType, int damageAmount)
        {
            DamageType = damageType;
            DamageAmount = damageAmount;
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


        // Roll damage based on list of DamageInfo
        public static List<DamageValue> RollDamage(List<DamageDice> damageRolls, List<DamageValue> damageFlats){
            List<DamageValue> damageInstances = new List<DamageValue>();

            // For each damageDice in damageRolls...
            foreach (DamageDice damageDice in damageRolls){
                DamageValue damageValue = new DamageValue(damageDice.DamageType, Dice.RollDice(damageDice.DamageDice));
                // Group damage by type
                if (damageInstances.Exists(di => di.DamageType == damageValue.DamageType)){
                    DamageValue existingInstance = damageInstances.Find(di => di.DamageType == damageValue.DamageType);
                    existingInstance.DamageAmount += damageValue.DamageAmount;
                }
                else{
                    damageInstances.Add(damageValue);
                }
            }
            foreach (DamageValue damageFlat in damageFlats){
                // Group damage by type
                if (damageInstances.Exists(di => di.DamageType == damageFlat.DamageType)){
                    DamageValue existingInstance = damageInstances.Find(di => di.DamageType == damageFlat.DamageType);
                    existingInstance.DamageAmount += damageFlat.DamageAmount;
                }
                else{
                    damageInstances.Add(damageFlat);
                }
            }
            // TODO: append traits list?
            return damageInstances;
        }

        public static int SumDamage(List<DamageValue> damageValues){
            int totalDamage = 0;
            foreach (DamageValue dv in damageValues){
                totalDamage += dv.DamageAmount;
            }
            return totalDamage;
        }
    }
}