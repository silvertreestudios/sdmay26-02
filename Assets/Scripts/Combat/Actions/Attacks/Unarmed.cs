using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Unity;
using GridPublic;
using UnityEngine;

namespace Game.Strikes
{
    [System.Serializable]
    public class Unarmed : MultiFrameEntityAction
    {
        // Done by Ryan Meyer 04/07/2026
        public override string ActionName => "Unarmed Strike";
        private StrikeProfile Profile;
        private int range = 1; // default range of 1 tile

        public Unarmed(uint cost, List<Dice> damages, List<DamageValue> flatDamages)
            : base(cost)
        {
            Profile = new StrikeProfile(damages, flatDamages);
            // Unarmed strike traits based on PF2e rules
            Profile.Traits = new List<string>() { "agile", "finesse", "nonlethal", "unarmed" };
            Profile.ItemSlug = "unarmed";
            Profile.WeaponCategory = "unarmed";
            Profile.SourceInfo = new AttackSourceInfo(
                "Unarmed Strike",
                "unarmed",
                "unarmed",
                Profile.Traits
            );
            Profile.ReachFeet = range * 5;
        }

        public StrikeProfile GetStrikeProfile()
        {
            return Profile;
        }

        protected override IEnumerator MFInvoke(GameObject attacker)
        {
            ActionController ac = attacker.GetComponent<ActionController>();
            CoroutineResult<StrikeTargetResult> target = new();
            StrikeTargetRequest request = new StrikeTargetRequest
            {
                ReachFeet = range * 5,
                IsRanged = false,
                RequiresLineOfEffect = true,
            };
            yield return GridAPI.GetInstance().GetStrikeTarget(attacker, request, target);

            // null target value equates to canceled action
            if (target.Value != null && target.Value.Target != null)
            {
                if (!StrikeEncounterTargeting.IsValid(attacker, target.Value.Target))
                {
                    CombatLog
                        .GetInstance()
                        .Log(
                            "- "
                                + attacker.name
                                + " cannot strike a creature outside its encounter."
                        );
                    yield break;
                }

                uint attackCount = ac == null ? 0 : ac.StrikePenalty;
                if (ac != null)
                {
                    yield return CoroutineRunner.Await(PayCostAsync(ac));
                    yield return CoroutineRunner.Await(ac.IncrementMultipleAttackPenaltyAsync());
                }
                CombatLog
                    .GetInstance()
                    .Log(
                        "- "
                            + attacker.name
                            + " attacks "
                            + target.Value.Target.name
                            + " with unarmed strike."
                    );
                Debug.Log(attacker + " Striking " + target.Value.Target);
                attacker
                    .GetComponent<CreaturePresentation>()
                    ?.PlayAttack(AnimationStyle.Unarmed, target.Value.Target.transform.position);
                yield return CoroutineRunner.Await(
                    StrikeResolutionPipeline.ResolveAsync(
                        new StrikeResolutionRequest
                        {
                            Attacker = attacker,
                            Target = target.Value.Target,
                            Profile = Profile,
                            TargetingResult = target.Value,
                            MultipleAttackCountOverride = attackCount,
                        }
                    )
                );
            }
        }

        // adds default unarmed strike to creature, called from action controller awake()
        public static void AddUnarmedStrike(GameObject creature)
        {
            var comp = creature.GetComponent<CreatureComponent>();
            List<Dice> damageDice = new() { new Dice(1, 3, "Bludgeoning") };
            List<DamageValue> damageFlat = new() { new DamageValue("Bludgeoning", comp.strMod) };
            Unarmed unarmedStrike = new Unarmed(1, damageDice, damageFlat);
            creature.GetComponent<ActionController>()?.AddAction(unarmedStrike);
            creature.GetComponent<CreatureComponent>()?.actions.Add("Unarmed Strike");
            Debug.Log("Unarmed strike added to " + creature.name);
        }
    }

    /// <summary>
    /// Enforces authoritative encounter membership after target selection but before a Strike
    /// commits any action, MAP, ammunition, roll, animation, or health work.
    /// </summary>
    internal static class StrikeEncounterTargeting
    {
        internal static bool IsValid(GameObject attacker, GameObject target)
        {
            CreatureComponent attackerCreature =
                attacker == null ? null : attacker.GetComponent<CreatureComponent>();
            CreatureComponent targetCreature =
                target == null ? null : target.GetComponent<CreatureComponent>();
            if (attackerCreature == null || targetCreature == null)
                return false;

            // Standalone and health-only fixtures have no committed encounter lifecycle and keep
            // their existing behavior. Once a lifecycle exists, closed phases reject new work and
            // an active Strike must resolve both ends through this exact immutable roster.
            if (
                !attackerCreature.TryGetEncounterRulesBridge(
                    out UnityEncounterRulesBridge attackerBridge
                )
            )
                return true;
            if (!attackerBridge.AllowsNewActionLifecycle)
                return false;
            if (!attackerBridge.HasActiveEncounter)
                return true;

            return targetCreature.TryGetEncounterRulesBridge(
                    out UnityEncounterRulesBridge targetBridge
                )
                && object.ReferenceEquals(attackerBridge, targetBridge)
                && attackerBridge.IsActiveEncounterParticipant(attackerCreature)
                && attackerBridge.IsActiveEncounterParticipant(targetCreature);
        }
    }
}
