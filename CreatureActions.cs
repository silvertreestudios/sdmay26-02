using UnityEngine;
using Game.Equipment;

namespace Game.Creatures
{
    public static class CreatureActions
    {
        public static void Move(ICreature creature)
        {
            // Movement logic here
            Debug.Log($"{creature.Name} moves at speed {creature.Speed}.");
        }

        public static void Strike(ICreature attacker, Equipment weapon, ICreature target)
        {
            if (weapon.Type != EquipmentType.Weapon)
            {
                Debug.LogError("Cannot strike with non-weapon equipment!");
                return;
            }

            AttackStatus attackStatus = AttackStatus.Miss;
            int attackRoll = Random.Range(1, 21);
            int attackRollTotal = attackRoll + attacker.attackBonus;
            Debug.Log($"{attacker.Name} attacks with {weapon.Name} and rolls {attackRollTotal} ({attackRoll}+{attackRollTotal - attackRoll}) against {target.Name} (AC {target.AC}).");

            if (attackRoll == 20 || attackRollTotal >= target.AC + 10)
            {
                attackStatus = AttackStatus.CriticalHit;
                Debug.Log($"{attacker.Name} lands a critical hit with {weapon.Name}!");
            }
            else if (attackRoll == 1)
            {
                attackStatus = AttackStatus.CriticalMiss;
                Debug.Log($"{attacker.Name} critically misses with {weapon.Name}!");
            }
            else if (attackRoll >= target.AC)
            {
                attackStatus = AttackStatus.Hit;
                Debug.Log($"{attacker.Name} hits with {weapon.Name}!");
            }
            else
            {
                attackStatus = AttackStatus.Miss;
                Debug.Log($"{attacker.Name} misses with {weapon.Name}!");
            }

            int damageSum = 0;
            if (attackStatus == AttackStatus.Hit || attackStatus == AttackStatus.CriticalHit)
            {
                int[] damageDealt = new int[weapon.Damage.DiceCount];
                for (int i = 0; i < weapon.Damage.DiceCount; i++)
                {
                    damageDealt[i] = Random.Range(1, weapon.Damage.DiceSides + 1);
                    damageSum += damageDealt[i];
                }

                damageSum += attacker.damageBonus;
                if (attackStatus == AttackStatus.CriticalHit)
                    damageSum *= 2;

                Debug.Log($"{attacker.Name} deals {damageSum} {weapon.Damage.DamageType} ({string.Join(", ", damageDealt)}  +{attacker.damageBonus}) damage with {weapon.Name}.");
                target.TakeDamage(damageSum, weapon.Damage.DamageType);
            }
        }
    }
}