using Game.Creature;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using GridPublic;

namespace Game.Strikes
{

[System.Serializable]
public class Unarmed : MultiFrameEntityAction
{
    // Done by Ryan Meyer 04/07/2026
    public override string ActionName => "Unarmed Strike";
    private Strike Strike;
    private int range = 1; // default range of 1 tile
    
    public Unarmed(uint cost, List<Dice> damages, List<DamageValue> flatDamages) : base(cost)
    {
        Strike = new Strike(damages, flatDamages);
        // Unarmed strike traits based on PF2e rules
        Strike.Traits = new List<string>() {"agile", "finesse", "nonlethal", "unarmed"};
        Strike.ItemSlug = "unarmed";
        Strike.WeaponCategory = "unarmed";
        Strike.SourceInfo = new AttackSourceInfo("Unarmed Strike", "unarmed", "unarmed", Strike.Traits);
    }

    public Strike GetStrike()
    {
        return Strike;
    }

    protected override IEnumerator MFInvoke(GameObject attacker)
    {
        ActionController ac = attacker.GetComponent<ActionController>();
        CoroutineResult<StrikeTargetResult> target = new();
        StrikeTargetRequest request = new StrikeTargetRequest
        {
            ReachFeet = range * 5,
            IsRanged = false,
            RequiresLineOfEffect = true
        };
        yield return GridAPI.GetInstance().GetStrikeTarget(attacker, request, target);
            
        // null target value equates to canceled action
        if(target.Value != null && target.Value.Target != null)
        {            Debug.Log(attacker + " Striking " + target.Value.Target);
            Strike.Damage(attacker, target.Value.Target, target.Value);
            if (ac)
            {
                PayCost(ac);
                ac.StrikePenalty += 1;
            }
        }
        if(ac)
            ac.IsTakingAction = false;
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

}
