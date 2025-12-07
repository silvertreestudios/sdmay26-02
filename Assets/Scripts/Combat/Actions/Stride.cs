using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEditor.Experimental.GraphView;

public class Stride : MultiFrameEntityAction
{
    public Stride(uint cost) : base(cost)
    {
        
    }

    protected override IEnumerator MFInvoke(GameObject target)
    {
        CoroutineResult<bool> canceled = new();
        yield return GridCharacterController3D.Instance.StrideCoroutine(target, canceled);
        if (!canceled.Value)
        {
            if (target.TryGetComponent<ActionController>(out var ac))
            {
                PayCost(ac);
                ac.IsTakingAction = false;
            }
        }
    }
}