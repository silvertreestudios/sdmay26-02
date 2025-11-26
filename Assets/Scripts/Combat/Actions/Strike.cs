using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Strike : MultiFrameEntityAction
{
    public Strike(uint cost) : base(cost)
    {
        
    }

    protected override IEnumerator MFInvoke(GameObject target)
    {
        throw new System.NotImplementedException();
    }
}