using Game.Creature;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace Game.Strikes
{

[System.Serializable]
public class StrikeWeapon : MultiFrameEntityAction
{
    private Strike Strike;
    private EquipmentWeapon Weapon;
    private int range =1; // default range of 1 tile
    public string weaponName;


    // Auto add strike actions based on equipped weapons
    public void WeaponStrikeAdder(GameObject creature)
    {
        CreatureComponent cc = creature.GetComponent<CreatureComponent>();
        // Check each hand for a weapon, and add corresponding strike action
        // Assumes that CreatureComponent is properly enforcing rules for what can be equipped
        // TODO prevent duplicate left/right actions if called multiple times
        if (cc.equippedRightHand != null)
        {
            StrikeWeapon strikeWeaponRight = new StrikeWeapon(1, cc.equippedRightHand, creature);
            creature.GetComponent<ActionController>().AddAction(strikeWeaponRight);
        }
        if (cc.equippedLeftHand != null)
        {
            StrikeWeapon strikeWeaponLeft = new StrikeWeapon(1, cc.equippedLeftHand, creature);
            creature.GetComponent<ActionController>().AddAction(strikeWeaponLeft);
        }
    }

    // Temp method for testing, adds first listed melee weapon as StrikeWeapon action
    public static void WeaponStrikeAdderTEMP(GameObject creature)
    {
        // Debug.Log("WeaponStrikeAdderTEMP called for " + creature.name);
        List<string> weaponsList = creature.GetComponent<CreatureComponent>().weaponsList;
        foreach(string weaponName in weaponsList)
        {
            EquipmentWeapon weapon = DataFileInterface.GetWeapon(weaponName);
            if (weapon.range == null || weapon.range == 0)
            {
                StrikeWeapon strikeWeaponAction = new StrikeWeapon(1, weapon, creature);
                creature.GetComponent<ActionController>().AddAction(strikeWeaponAction);
                Debug.Log("WeaponStrikeAdderTEMP added StrikeWeapon action for " + weapon.name + " to " + creature.name);
                break;
            }
        }
        // Debug.Log("WeaponStrikeAdderTEMP finished for " + creature.name);
        //return null;
    }

    public string GetWeaponName()
    {
        return weaponName;
    }

    public EquipmentWeapon GetWeapon()
    {
        return Weapon;
    }

    public Strike GetStrike()
    {
        return Strike;
    }

    // Variant of Strike action based on a weapon
    public StrikeWeapon(uint cost, EquipmentWeapon weapon, GameObject creature) :base(cost)
    {
        int flatDamageBonus = creature.GetComponent<CreatureComponent>().damageBonus;
        flatDamageBonus += creature.GetComponent<CreatureComponent>().strMod;
        Weapon = weapon;
        weaponName = weapon.name;
        List<Dice> damageList = new List<Dice>();
        damageList.Add(Weapon.damage);
        List<DamageValue> flatDamageList = new List<DamageValue>();
        flatDamageList.Add(new DamageValue(Weapon.damage.damageType, flatDamageBonus));
        // TODO When size>medium creatures are implemented that will need accounted for in range
        if(Weapon.range != null && Weapon.range > 0)
        {
            range = Weapon.range/5; // convert from feet to grid units, assuming 5 foot grid squares
        }
        if (Weapon.traits.Contains("reach"))
        {
            range += 1; // extend range for reach weapon
        }
        Strike = new Strike(damageList, flatDamageList);
    }

    protected override IEnumerator MFInvoke(GameObject attacker)
    {
        ActionController ac = attacker.GetComponent<ActionController>();
        // Grid get target;
        CoroutineResult<GameObject> target = new();
        CoroutineResult<bool> canceled = new();
        //yield return GridCharacterController3D.Instance.StrikeCoroutine(attacker, 2, target);
        yield return FSM_API.Strike(attacker, range, target, canceled);
        // I implemented a cancel refund for this action, let me know if it needs to change - Adam
        if(target.Value && !canceled.Value)
        {
            Debug.Log(attacker + " Striking " + target.Value +" with " + Weapon.name);
            // TODO: need to modify strike/damage to account for character abilities, weapons traits, etc
            Strike.Damage(attacker, target.Value);
            if(ac)
                PayCost(ac);
        }
        if(ac)
            ac.IsTakingAction = false;
    }
}
}




