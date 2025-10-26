using UnityEngine;
using Game.Equipment;

namespace Game.Creatures
{
    public static class CreatureActions
    {
        public static void Move(ICreature creature)
        {
            // Movement logic here
        }

        // TODO: weapon info will baked into strike action, no need for weapon param
        // TODO: attacker left in for now, can abstract later?
        // TODO: target creature called from...
        
        public static void Strike(ICreature attacker, Equipment weapon, ICreature target)
        {
            // add attack roll modifiers from weapon, creature, situational
            int attackBonus = attacker.attackBonus + 0; // TODO

            D20Result attackRoll = Roll20.RollD20(attackBonus, target.AC);

            // add flat damage bonuses here
            List<DamageValue> damageBonuses = new List<DamageValue>();

            if (attackRoll.status == D20Status.Success || attackRoll.status == D20Status.CriticalSuccess){
                List<DamageValue> damageValues = DamageRoller.RollDamage(weapon.Damage, damageBonuses);
                DamageRoller.EvaluateCriticalDamage(attackRoll, damageValues);
                target.TakeDamage(damageValues);
            }
        }
    }
}