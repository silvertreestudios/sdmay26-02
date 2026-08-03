using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    public sealed partial class RuleDispatcher
    {
        private async ValueTask<object> InvokeActionLifecycle(
            IRegistration registration,
            IFrameInvocation invocation,
            IReadOnlyList<BoundMiddlewareRegistration> middleware
        )
        {
            IReceiptedActionOp receiptedAction =
                invocation.FrameView.Operation as IReceiptedActionOp;
            bool costsAlreadyCommitted = false;
            if (
                receiptedAction != null
                && invocation.FrameView.Snapshot.ActionReceipts.TryGet(
                    receiptedAction.InvocationId,
                    out ActionInvocationReceipt receipt
                )
            )
            {
                if (!receiptedAction.HasSameIntent(receipt.Operation))
                    return registration.CreateInvalidResult(
                        ActionReceiptReduction.ConflictingIntentReason
                    );
                if (receipt is ResolvedActionReceipt resolvedReceipt)
                    return registration.CreateResolvedResult(resolvedReceipt.Outcome);
                if (receipt is InterruptedActionReceipt)
                    return registration.CreateInterruptedResult();
                if (receipt is not CostsCommittedActionReceipt)
                    throw new InvalidOperationException(
                        "The action invocation receipt has an unknown lifecycle phase."
                    );
                costsAlreadyCommitted = true;
            }

            ActionOpInfo action = invocation.FrameView.ActionInfo;
            ActionProfile profile = invocation.FrameView.ActionProfile;
            if (!costsAlreadyCommitted)
            {
                ActionValidationResult validation = actionRuntime.Validate(invocation);
                if (validation is ActionValidationResult.InvalidActionValidationResult invalid)
                    return registration.CreateInvalidResult(invalid.Reason);

                CommitActionCostsOp commitCosts =
                    receiptedAction != null
                        ? new CommitActionCostsOp(action.Id, action.Actor, profile, receiptedAction)
                        : new CommitActionCostsOp(action.Id, action.Actor, profile);
                OpResult<ActionCostsOutcome> costs = await DispatchNested(commitCosts, action.Id);
                if (costs is InvalidOpResult<ActionCostsOutcome> invalidCosts)
                    return registration.CreateInvalidResult(invalidCosts.Reason);
                if (!(costs is ResolvedOpResult<ActionCostsOutcome>))
                {
                    throw new InvalidOperationException(
                        "Atomic action costs may only resolve or reject before an action begins."
                    );
                }
            }

            OpResult<ActionStartOutcome> begun = await DispatchNested(
                new ActionBegunOp(action.Id),
                action.Id
            );
            if (!(begun is ResolvedOpResult<ActionStartOutcome> resolvedBegun))
            {
                throw new InvalidOperationException(
                    "ActionBegunOp reports disruption through ActionStartOutcome."
                );
            }
            if (resolvedBegun.Value.Decision == ActionStartDecision.Interrupted)
            {
                if (invocation.FrameView.Operation is IReceiptedActionOp interruptedAction)
                {
                    OpResult<ActionStartOutcome> interrupted = await DispatchNested(
                        new InterruptReceiptedActionOp(interruptedAction),
                        action.Id
                    );
                    if (!(interrupted is ResolvedOpResult<ActionStartOutcome>))
                        throw new InvalidOperationException(
                            "A receipted action interruption could not checkpoint its final phase."
                        );
                }
                return registration.CreateInterruptedResult();
            }

            object featureResult = await InvokeWithMiddleware(
                registration,
                invocation,
                middleware,
                0
            );
            if (registration.GetResultStatus(featureResult) != OpStatus.Resolved)
            {
                throw new InvalidOperationException(
                    "Action feature middleware cannot replace a begun action with a structural "
                        + "Invalid, Interrupted, or Cancelled result. Disruption belongs in ActionBegunOp."
                );
            }
            return featureResult;
        }

        private ValueTask<object> InvokeWithMiddleware(
            IRegistration registration,
            IFrameInvocation invocation,
            IReadOnlyList<BoundMiddlewareRegistration> middleware,
            int index
        )
        {
            while (
                index < middleware.Count
                && !ruleRegistry.IsActive(store.Snapshot, middleware[index].Binding)
            )
            {
                index++;
            }

            if (index >= middleware.Count)
                return registration.Invoke(invocation, this);

            BoundMiddlewareRegistration current = middleware[index];
            int nextIndex = index + 1;
            return current.Registration.Invoke(
                current.Binding,
                invocation,
                this,
                () => InvokeWithMiddleware(registration, invocation, middleware, nextIndex)
            );
        }
    }
}
