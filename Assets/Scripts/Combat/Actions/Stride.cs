using UnityEngine;
using System.Collections.Generic;
using System.Collections;

using UnityEngineInternal;
using GridPublic;

public class Stride : MultiFrameEntityAction
{
    public Stride(uint cost) : base(cost)
    {
        
    }

    protected override IEnumerator MFInvoke(GameObject target)
    {
        ActionController ac = target.GetComponent<ActionController>();
        CoroutineResult<bool> canceled = new();
        //yield return GridCharacterController3D.Instance.StrideCoroutine(target, canceled);
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