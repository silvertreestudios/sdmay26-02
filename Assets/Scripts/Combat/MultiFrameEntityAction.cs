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
        if (!owner.TryReserveAction(out ActionReservationToken reservation))
            return;
        try
        {
            InvokeReserved(target, reservation);
        }
        catch
        {
            owner.ReleaseActionReservation(reservation);
            throw;
        }
    }

    internal void InvokeReserved(GameObject target, ActionReservationToken reservation)
    {
        ActionController owner = target == null ? null : target.GetComponent<ActionController>();
        if (owner == null || !owner.OwnsActionReservation(reservation))
            throw new System.InvalidOperationException(
                "A multi-frame action requires its caller's exact active reservation."
            );
        // The actor may deactivate itself while committed damage and encounter presentation settle.
        // Host the reservation-owning wrapper on the scene lifecycle runner so Unity cannot stop
        // its finally together with the defeated actor and strand encounter host completion.
        CoroutineRunner.Run(MFInvokeWithEndCheck(target, reservation));
    }

    private IEnumerator MFInvokeWithEndCheck(GameObject target, ActionReservationToken reservation)
    {
        ActionController owner = target.GetComponent<ActionController>();
        try
        {
            yield return MFInvoke(target);
        }
        finally
        {
            // Completion owns this flag even when awaited rule work faults after the action began.
            // Encounter outcome presentation observes this exact terminal release, so no action
            // work may follow it in the outer lifecycle.
            owner.ReleaseActionReservation(reservation);
        }
    }

    protected abstract IEnumerator MFInvoke(GameObject target);
}
