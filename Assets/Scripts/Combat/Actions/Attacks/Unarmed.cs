using Game.Creature;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

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

    protected override IEnumerator MFInvoke(GameObject attacker)
    {
        ActionController ac = attacker.GetComponent<ActionController>();
        // Grid get target;
        CoroutineResult<GameObject> target = new();
        CoroutineResult<bool> canceled = new();
        //yield return GridCharacterController3D.Instance.StrikeCoroutine(attacker, 2, target);
        yield return GridAPI.GetInstance().Strike(attacker, range, target, canceled);
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
}
