using UnityEngine;
using System.Collections.Generic;
using System.Collections;

using UnityEngineInternal;
using GridPublic;

public class Stride : MultiFrameEntityAction
{
    // Done by Ryan Meyer 04/07/2026
    public override string ActionName => "Stride";
    public Stride(uint cost) : base(cost)
    {
        
    }

    protected override IEnumerator MFInvoke(GameObject target)
    {
        ActionController ac = target.GetComponent<ActionController>();
        CoroutineResult<bool> canceled = new();
        //yield return GridCharacterController3D.Instance.StrideCoroutine(target, canceled);
        CombatLog.GetInstance().Log("- " + target.name + " used Stride");
        yield return GridAPI.GetInstance().Stride(target);
        
        if (!canceled.Value)
        {
            if (ac)
                PayCost(ac);
        }
        if (ac)
            ac.IsTakingAction = false;
    }
}