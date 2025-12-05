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
        Debug.Log(attacker + " Striking " + target);
        Strike.Damage(attacker, target.Value);
        yield return null;
    }
}
