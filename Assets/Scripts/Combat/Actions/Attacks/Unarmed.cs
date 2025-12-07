using Game.Creature;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

[System.Serializable]
public class Unarmed : MultiFrameEntityAction
{
    private Strike Strike;
    public Unarmed(uint cost, List<Dice> damages, List<DamageValue> flatDamages) : base(cost)
    {
        Strike = new Strike(damages, flatDamages);
    }

    protected override IEnumerator MFInvoke(GameObject attacker)
    {
        // Grid get target;
        CoroutineResult<GameObject> target = new();
        yield return GridCharacterController3D.Instance.StrikeCoroutine(attacker, 2, target);
        if(target.Value)
        {
            Debug.Log(attacker + " Striking " + target.Value);
            Strike.Damage(attacker, target.Value);
            if(attacker.TryGetComponent<ActionController>(out var ac))
            {
                PayCost(ac);
                ac.IsTakingAction = false;
            }
        }
    }
}
