using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Stride : MultiFrameEntityAction
{
    public Stride(uint cost) : base(cost)
    {
        
    }

    protected override IEnumerator MFInvoke(GameObject target)
    {
        yield return GridCharacterController3D.Instance.StrideCoroutine(target);
    }
}