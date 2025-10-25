using System.Collections;
using UnityEngine;

public abstract class MultiFrameEntityAction : EntityAction
{
    public MultiFrameEntityAction(uint cost) : base(cost) { }

    public override void Invoke(GameObject caller)
    {
        CoroutineRunner.Run(MFEnact(caller));
    }

    protected abstract IEnumerator MFEnact(GameObject caller);
}
