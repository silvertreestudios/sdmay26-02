using System.Collections;
using UnityEngine;

public abstract class MultiFrameEntityAction : EntityAction
{
    public MultiFrameEntityAction(uint cost)
        : base(cost) { }

    public override void Invoke(GameObject target)
    {
        ActionController owner = target == null ? null : target.GetComponent<ActionController>();
        if (owner == null)
            throw new System.ArgumentException(
                "A multi-frame action requires an ActionController owner.",
                nameof(target)
            );
        owner.StartCoroutine(MFInvokeWithEndCheck(target));
    }

    private IEnumerator MFInvokeWithEndCheck(GameObject target)
    {
        ActionController owner = target.GetComponent<ActionController>();
        try
        {
            yield return MFInvoke(target);
        }
        finally
        {
            // Completion owns this flag even when awaited rule work faults after the action began.
            // Dungeon exploration presentation waits on it after encounter completion.
            owner.IsTakingAction = false;
            CombatManager.GetInstance().CheckForEndOfGame();
        }
    }

    protected abstract IEnumerator MFInvoke(GameObject target);
}
