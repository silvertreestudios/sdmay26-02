using UnityEngine;
using System.Collections;
using Game.Creature;
using System.Collections.Generic;


public class StrikeWeapon : MultiFrameEntityAction
{
    public Strike Strike;
    public EquipmentWeapon Weapon;

    
    public Strike(List<Dice> damages, List<DamageValue> flatDamages)
    {
        Damages = damages ?? new();
        FlatDamages = flatDamages ?? new();
    }
    


    public StrikeWeapon(uint cost, EquipmentWeapon weapon, GameObject creature) :base(cost)
    {
        Weapon = weapon;
        int flatDamageBonus = creature.GetComponent<CreatureComponent>().damageBonus;
        flatDamageBonus += creature.GetComponent<CreatureComponent>().strMod;
        List<Dice> damageList = new List<Dice>();
        damageList.Add(weapon.damage);
        List<DamageValue> flatDamageList = new List<DamageValue>();
        flatDamageList.Add(new DamageValue(weapon.damage.damageType, flatDamageBonus));
        
        Strike = new Strike(damageList, flatDamageList);
    }
    public StrikeWeapon(uint cost, List<Dice> damages, List<DamageValue> flatDamages) : base(cost)
    {
        Strike = new Strike(damages, flatDamages);
    }
    


    protected override IEnumerator MFInvoke(GameObject attacker)
    {
        ActionController ac = attacker.GetComponent<ActionController>();
        // Grid get target;
        CoroutineResult<GameObject> target = new();
        CoroutineResult<bool> canceled = new();
        //yield return GridCharacterController3D.Instance.StrikeCoroutine(attacker, 2, target);
        yield return FSM_API.Unarmed(attacker, 2, target, canceled);
        // I implemented a cancel refund for this action, let me know if it needs to change - Adam
        if(target.Value && !canceled.Value)
        {
            Debug.Log(attacker + " Striking " + target.Value);
            Strike.Damage(attacker, target.Value);
            if(ac)
                PayCost(ac);
        }
        if(ac)
            ac.IsTakingAction = false;
    }
    
}
