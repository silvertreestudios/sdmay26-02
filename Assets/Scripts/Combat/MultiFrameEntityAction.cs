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
        ActionController controller =
            target != null ? target.GetComponent<ActionController>() : null;
        try
        {
            yield return CoroutineRunner.Run(MFInvoke(target));
            CombatManager.GetInstance().CheckForEndOfGame();
        }
        finally
        {
            controller?.CompleteAction();
        }
    }

    protected abstract IEnumerator MFInvoke(GameObject target);
}
