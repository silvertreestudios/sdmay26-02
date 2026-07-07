using Game.Creature;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using GridPublic;
using GridPrivate;

namespace Game.Strikes
{

[System.Serializable]
public class StrikeWeapon : MultiFrameEntityAction
{
    public override string ActionName => GetWeaponName();
    private Strike Strike;
    private EquipmentWeapon Weapon;
    private int range = 1;
    public string weaponName;

    public static void WeaponStrikeAdder(GameObject creature)
    {
        CreatureComponent cc = creature.GetComponent<CreatureComponent>();
        if (cc == null)
            return;

        if (cc.HasEquippedRightWeapon())
            AddWeaponStrike(creature, cc.equippedRightHand);
        if (cc.HasEquippedLeftWeapon())
            AddWeaponStrike(creature, cc.equippedLeftHand);
    }

    public static void WeaponStrikeAdderAutomatic(GameObject creature)
    {
        CreatureComponent cc = creature.GetComponent<CreatureComponent>();
        if (cc == null)
            return;

        WeaponStrikeAdder(creature);

        if (cc.weapons != null && cc.weapons.Count > 0)
        {
            foreach (EquipmentWeapon weapon in cc.weapons)
                AddWeaponStrike(creature, weapon);
        }

        List<string> weaponsList = cc.weaponsList;
        foreach (string listedWeaponName in weaponsList)
        {
            EquipmentWeapon weapon = DataFileInterface.GetWeapon(listedWeaponName);
            AddWeaponStrike(creature, weapon);
        }
    }

    private static void AddWeaponStrike(GameObject creature, EquipmentWeapon weapon)
    {
        if (creature == null || weapon == null || string.IsNullOrWhiteSpace(weapon.name) || weapon.damage == null)
            return;

        ActionController controller = creature.GetComponent<ActionController>();
        CreatureComponent cc = creature.GetComponent<CreatureComponent>();
        if (controller == null || cc == null || HasAction(controller, weapon.name))
            return;

        StrikeWeapon strikeWeapon = new StrikeWeapon(1, weapon, creature);
        controller.AddAction(strikeWeapon);
        if (!cc.actions.Contains("StrikeWeapon " + strikeWeapon.weaponName))
            cc.actions.Add("StrikeWeapon " + strikeWeapon.weaponName);

        int reloadCost = cc.GetReloadCost(weapon);
        string reloadActionName = "Reload " + weapon.name;
        if (reloadCost > 0 && !HasAction(controller, reloadActionName))
            controller.AddAction(new ReloadWeaponAction((uint)reloadCost, weapon));
    }

    private static bool HasAction(ActionController controller, string actionName)
    {
        foreach (EntityAction action in controller.GetActions())
        {
            if (action.ActionName == actionName)
                return true;
        }
        return false;
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

    public int GetRange()
    {
        return range;
    }

    public StrikeTargetRequest GetTargetRequest()
    {
        bool ranged = IsRangedWeapon();
        int reachFeet = Weapon.traits != null && Weapon.traits.Contains("reach") ? 10 : 5;
        return new StrikeTargetRequest
        {
            ReachFeet = reachFeet,
            RangeIncrementFeet = ranged ? Weapon.range : 0,
            IsRanged = ranged,
            RequiresLineOfEffect = true
        };
    }

    public bool CanStrikeTarget(GameObject attacker, GameObject target, Tile[,] tiles)
    {
        return StrikeTargeting.Evaluate(attacker, target, tiles, GetTargetRequest()) != null;
    }

    public bool IsUsableBy(GameObject attacker)
    {
        CreatureComponent cc = attacker.GetComponent<CreatureComponent>();
        return cc == null || (cc.HasAmmoFor(Weapon) && cc.IsWeaponLoaded(Weapon));
    }

    public bool IsRangedWeapon()
    {
        return Weapon != null && Weapon.range > 0;
    }

    public StrikeWeapon(uint cost, EquipmentWeapon weapon, GameObject creature) : base(cost)
    {
        CreatureComponent cc = creature.GetComponent<CreatureComponent>();
        Weapon = weapon;
        weaponName = weapon.name;
        List<Dice> damageList = new List<Dice> { Weapon.damage };
        List<DamageValue> flatDamageList = new List<DamageValue>();

        if (!IsRangedWeapon() || string.IsNullOrWhiteSpace(Weapon.ammo))
            flatDamageList.Add(new DamageValue(Weapon.damage.damageType, cc.damageBonus));

        if (Weapon.range > 0)
            range = Weapon.range / 5;
        if (Weapon.traits != null && Weapon.traits.Contains("reach"))
            range += 1;

        Strike = new Strike(damageList, flatDamageList);
        Strike.Traits = Weapon.traits ?? new List<string>();
        Strike.AttackBonusOverride = cc.GetAttackBonusForWeapon(weapon);
    }

    protected override IEnumerator MFInvoke(GameObject attacker)
    {
        ActionController ac = attacker.GetComponent<ActionController>();
        CreatureComponent cc = attacker.GetComponent<CreatureComponent>();
        if (cc != null && (!cc.HasAmmoFor(Weapon) || !cc.IsWeaponLoaded(Weapon)))
        {
            CombatLog.GetInstance().Log("- " + attacker.name + " cannot fire " + weaponName + ".");
            if (ac)
                ac.IsTakingAction = false;
            yield break;
        }

        CoroutineResult<StrikeTargetResult> target = new();
        yield return GridAPI.GetInstance().GetStrikeTarget(attacker, GetTargetRequest(), target);

        if (target.Value != null && target.Value.Target != null)
        {
            if (cc != null && !cc.ConsumeAmmoFor(Weapon))
            {
                CombatLog.GetInstance().Log("- " + attacker.name + " has no ammunition for " + weaponName + ".");
                if (ac)
                    ac.IsTakingAction = false;
                yield break;
            }

            CombatLog.GetInstance().Log("- " + attacker.name + " strikes " + target.Value.Target.name + " with " + weaponName + ".");
            Strike.Damage(attacker, target.Value.Target, target.Value);
            cc?.MarkWeaponFired(Weapon);
            if (ac)
            {
                PayCost(ac);
                ac.StrikePenalty += 1;
            }
        }
        if (ac)
            ac.IsTakingAction = false;
    }
}

[System.Serializable]
public class ReloadWeaponAction : EntityAction
{
    private readonly EquipmentWeapon Weapon;

    public override string ActionName => "Reload " + Weapon.name;

    public ReloadWeaponAction(uint cost, EquipmentWeapon weapon) : base(cost)
    {
        Weapon = weapon;
    }

    public override void Invoke(GameObject target)
    {
        ActionController ac = target.GetComponent<ActionController>();
        CreatureComponent cc = target.GetComponent<CreatureComponent>();
        if (cc != null && cc.ReloadWeapon(Weapon))
        {
            CombatLog.GetInstance().Log("- " + target.name + " reloads " + Weapon.name + ".");
            PayCost(ac);
        }
        if (ac)
            ac.IsTakingAction = false;
        CombatManager.GetInstance().CheckForEndOfGame();
    }
}
}