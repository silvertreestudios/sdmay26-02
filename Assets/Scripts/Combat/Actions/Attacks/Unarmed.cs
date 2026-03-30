using Game.Creature;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace Game.Strikes
{

[System.Serializable]
public class Unarmed : MultiFrameEntityAction
{
    private Strike Strike;
    private int range = 1; // default range of 1 tile
    
    public Unarmed(uint cost, List<Dice> damages, List<DamageValue> flatDamages) : base(cost)
    {
        Strike = new Strike(damages, flatDamages);
        // Unarmed strike traits based on PF2e rules
        Strike.Traits = new List<string>() {"agile", "finesse", "nonlethal", "unarmed"};
    }

    public Strike GetStrike()
    {
        return Strike;
    }

    protected override IEnumerator MFInvoke(GameObject attacker)
    {
        ActionController ac = attacker.GetComponent<ActionController>();
        // Grid get target;
        CoroutineResult<GameObject> target = new();
        CoroutineResult<bool> canceled = new();
        yield return GridAPIFSM.GetInstance().Strike(attacker, range, target, canceled);
        // I implemented a cancel refund for this action, let me know if it needs to change - Adam
        if(target.Value && !canceled.Value)
        {
            Debug.Log(attacker + " Striking " + target.Value);
            Strike.Damage(attacker, target.Value);
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
        Debug.Log("Unarmed strike added to " + creature.name);
    }

}

}