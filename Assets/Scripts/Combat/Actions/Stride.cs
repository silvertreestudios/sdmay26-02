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

    /// <inheritdoc/>
    /// <remarks>
    /// Exploration authority is captured before yielding to the grid coroutine. If a committed
    /// step activates combat, the Stride that began in exploration remains free while later
    /// combat Strides use normal action costs.
    /// </remarks>
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
    }
}
