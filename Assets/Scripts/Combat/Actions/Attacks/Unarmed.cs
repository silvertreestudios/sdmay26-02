using Game.Creature;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Unarmed : EntityAction
{
    private Strike Strike;
    public Unarmed(uint cost, List<Dice> damages, List<DamageValue> flatDamages) : base(cost)
    {
        Strike = new Strike(damages, flatDamages);
    }

    public override void Invoke(GameObject attacker)
    {
        // Grid get target;
        GameObject target = CombatManager.GetInstance().GetTarget(attacker);
        Debug.Log(attacker + " Striking " + target);
        Strike.Damage(attacker, target);
    }
}
