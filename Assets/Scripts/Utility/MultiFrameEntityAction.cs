using System.Collections;
using UnityEngine;

public abstract class MultiFrameEntityAction : EntityAction
{
    public MultiFrameEntityAction(uint cost) : base(cost) { }

    public override void Invoke(GameObject target)
    {
        CoroutineRunner.Run(MFEnact(target));
    }

    protected abstract IEnumerator MFEnact(GameObject target);
}
