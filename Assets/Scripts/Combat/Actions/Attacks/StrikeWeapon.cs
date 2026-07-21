using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Strikes
{
    [System.Serializable]
    public class StrikeWeapon : MultiFrameEntityAction
    {
        private const string CreatureActionLabelPrefix = "StrikeWeapon ";
        private const string ReachTrait = "reach";

        public override string ActionName => GetWeaponName();
        private StrikeProfile Profile;
        private EquipmentWeapon Weapon;
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
            if (
                creature == null
                || weapon == null
                || string.IsNullOrWhiteSpace(weapon.name)
                || weapon.damage == null
            )
                return;

            ActionController controller = creature.GetComponent<ActionController>();
            CreatureComponent cc = creature.GetComponent<CreatureComponent>();
            if (controller == null || cc == null || HasWeaponStrike(controller, weapon))
                return;

            StrikeWeapon strikeWeapon = new StrikeWeapon(1, weapon, creature);
            controller.AddAction(strikeWeapon);
            string creatureActionLabel = CreatureActionLabelPrefix + strikeWeapon.weaponName;
            if (!cc.actions.Contains(creatureActionLabel))
                cc.actions.Add(creatureActionLabel);

            int reloadCost = cc.GetReloadCost(weapon);
            if (reloadCost > 0 && !HasReloadAction(controller, weapon))
                controller.AddAction(new ReloadWeaponAction((uint)reloadCost, weapon));
        }

        private static bool HasWeaponStrike(ActionController controller, EquipmentWeapon weapon)
        {
            foreach (EntityAction action in controller.GetActions())
            {
                if (action is StrikeWeapon strikeWeapon && strikeWeapon.RepresentsWeapon(weapon))
                    return true;
            }
            return false;
        }

        private static bool HasReloadAction(ActionController controller, EquipmentWeapon weapon)
        {
            foreach (EntityAction action in controller.GetActions())
            {
                if (
                    action is ReloadWeaponAction reloadAction
                    && reloadAction.RepresentsWeapon(weapon)
                )
                    return true;
            }
            return false;
        }

        private bool RepresentsWeapon(EquipmentWeapon weapon)
        {
            return Weapon == weapon
                || (
                    Weapon != null
                    && weapon != null
                    && string.Equals(
                        Weapon.name,
                        weapon.name,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                );
        }

        public string GetWeaponName()
        {
            return weaponName;
        }

        public EquipmentWeapon GetWeapon()
        {
            return Weapon;
        }

        public StrikeProfile GetStrikeProfile()
        {
            return Profile;
        }

        public int GetRange()
        {
            return Mathf.CeilToInt(GetTargetRequest().MaximumRangeFeet / 5.0f);
        }

        public StrikeTargetRequest GetTargetRequest()
        {
            bool ranged = IsRangedWeapon();
            int reachFeet = Weapon.traits != null && Weapon.traits.Contains(ReachTrait) ? 10 : 5;
            return new StrikeTargetRequest
            {
                ReachFeet = reachFeet,
                RangeIncrementFeet = ranged ? Weapon.range : 0,
                IsRanged = ranged,
                RequiresLineOfEffect = true,
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

        public StrikeWeapon(uint cost, EquipmentWeapon weapon, GameObject creature)
            : base(cost)
        {
            CreatureComponent cc = creature.GetComponent<CreatureComponent>();
            Weapon = weapon;
            weaponName = weapon.name;
            List<Dice> damageList = new List<Dice> { Weapon.damage };
            List<DamageValue> flatDamageList = new List<DamageValue>();

            if (!IsRangedWeapon() || string.IsNullOrWhiteSpace(Weapon.ammo))
                flatDamageList.Add(new DamageValue(Weapon.damage.damageType, cc.damageBonus));

            Profile = new StrikeProfile(damageList, flatDamageList);
            Profile.Traits = Weapon.traits ?? new List<string>();
            Profile.SourceInfo = AttackSourceInfo.FromWeapon(Weapon);
            Profile.ItemSlug = Pf2eSlug.FromName(Weapon.name);
            Profile.WeaponCategory = Weapon.category;
            Profile.IsRangedAttack = IsRangedWeapon();
            Profile.ReachFeet = GetTargetRequest().ReachFeet;
            Profile.AttackModifierOverride = cc.GetAttackBonusForWeapon(weapon);
        }

        protected override IEnumerator MFInvoke(GameObject attacker)
        {
            ActionController ac = attacker.GetComponent<ActionController>();
            CreatureComponent cc = attacker.GetComponent<CreatureComponent>();
            if (cc != null && (!cc.HasAmmoFor(Weapon) || !cc.IsWeaponLoaded(Weapon)))
            {
                CombatLog
                    .GetInstance()
                    .Log("- " + attacker.name + " cannot fire " + weaponName + ".");
                if (ac)
                    ac.IsTakingAction = false;
                yield break;
            }

            CoroutineResult<StrikeTargetResult> target = new();
            yield return GridAPI
                .GetInstance()
                .GetStrikeTarget(attacker, GetTargetRequest(), target);

            if (target.Value != null && target.Value.Target != null)
            {
                if (cc != null && !cc.ConsumeAmmoFor(Weapon))
                {
                    CombatLog
                        .GetInstance()
                        .Log("- " + attacker.name + " has no ammunition for " + weaponName + ".");
                    if (ac)
                        ac.IsTakingAction = false;
                    yield break;
                }

                CombatLog
                    .GetInstance()
                    .Log(
                        "- "
                            + attacker.name
                            + " strikes "
                            + target.Value.Target.name
                            + " with "
                            + weaponName
                            + "."
                    );
                attacker
                    .GetComponent<CreaturePresentation>()
                    ?.PlayAttack(Weapon, target.Value.Target.transform.position);
                yield return CoroutineRunner.Await(
                    StrikeResolutionPipeline.ResolveAsync(
                        new StrikeResolutionRequest
                        {
                            Attacker = attacker,
                            Target = target.Value.Target,
                            Profile = Profile,
                            TargetingResult = target.Value,
                        }
                    )
                );
                cc?.MarkWeaponFired(Weapon);
                if (ac)
                {
                    yield return CoroutineRunner.Await(PayCostAsync(ac));
                    yield return CoroutineRunner.Await(ac.IncrementMultipleAttackPenaltyAsync());
                }
            }
            if (ac)
                ac.IsTakingAction = false;
        }
    }

    [System.Serializable]
    public class ReloadWeaponAction : MultiFrameEntityAction
    {
        private readonly EquipmentWeapon Weapon;

        public override string ActionName => "Reload " + Weapon.name;

        public ReloadWeaponAction(uint cost, EquipmentWeapon weapon)
            : base(cost)
        {
            Weapon = weapon;
        }

        public bool RepresentsWeapon(EquipmentWeapon weapon)
        {
            return Weapon == weapon
                || (
                    Weapon != null
                    && weapon != null
                    && string.Equals(
                        Weapon.name,
                        weapon.name,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                );
        }

        protected override IEnumerator MFInvoke(GameObject target)
        {
            ActionController ac = target.GetComponent<ActionController>();
            CreatureComponent cc = target.GetComponent<CreatureComponent>();
            if (cc != null && cc.ReloadWeapon(Weapon))
            {
                CombatLog.GetInstance().Log("- " + target.name + " reloads " + Weapon.name + ".");
                yield return CoroutineRunner.Await(PayCostAsync(ac));
            }
            if (ac)
                ac.IsTakingAction = false;
        }
    }
}
