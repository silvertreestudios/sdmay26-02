using System.Collections;
using UnityEngine;

public abstract class MultiFrameEntityAction : EntityAction
{
    public MultiFrameEntityAction(uint cost)
        : base(cost) { }

    public override void Invoke(GameObject target)
    {
        CoroutineRunner.Run(MFInvokeWithEndCheck(target));
    }

    private IEnumerator MFInvokeWithEndCheck(GameObject target)
    {
        yield return CoroutineRunner.Run(MFInvoke(target));
        CombatManager.GetInstance().CheckForEndOfGame();
    }

    protected abstract IEnumerator MFInvoke(GameObject target);
}
