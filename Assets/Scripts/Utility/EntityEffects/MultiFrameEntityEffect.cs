using System.Collections;
using UnityEngine;

public abstract class MultiFrameEntityEffect : EntityEffect
{
    public override void Invoke(GameObject target)
    {
        CoroutineRunner.Run(MFInvoke(target));
    }

    protected abstract IEnumerator MFInvoke(GameObject target);
}
