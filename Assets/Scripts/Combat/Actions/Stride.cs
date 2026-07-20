using System.Collections;
using System.Collections.Generic;
using GridPrivate;
using GridPublic;
using UnityEngine;
using UnityEngineInternal;

public class Stride : MultiFrameEntityAction
{
    // Done by Ryan Meyer 04/07/2026
    public override string ActionName => "Stride";

    public Stride(uint cost)
        : base(cost) { }

    protected override IEnumerator MFInvoke(GameObject target)
    {
        bool canceled = false;
        // Callback triggered on successful action cancelation
        void cancel()
        {
            Debug.Log("Canceled");
            canceled = true;
            OnActionCancel.RemoveListener(cancel);
        }
        OnActionCancel.AddListener(cancel);

        ActionController ac = target.GetComponent<ActionController>();
        bool startedInExploration = ac != null && ac.IsInDungeonExploration;

        CombatLog.GetInstance().Log("- " + target.name + " used Stride");
        yield return GridAPI.GetInstance().Stride(target);

        if (!canceled)
        {
            if (ac && !startedInExploration)
                PayCost(ac);
        }
        if (ac)
            ac.IsTakingAction = false;
    }
}
