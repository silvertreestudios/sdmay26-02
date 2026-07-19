using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    public sealed partial class RuleDispatcher
    {
        internal ReductionResult<TResult> Reduce<TOp, TResult>(
            OpFrame<TOp> frame,
            IOpReducer<TOp, TResult> reducer,
            RuleSource source
        )
            where TOp : IRuleOp<TResult>
        {
            return store.Reduce(
                new ReductionContext<TOp>(frame.Op, frame.Id, frame.RootId, source),
                reducer
            );
        }

        internal void CaptureCommittedFacts(
            IFrameInvocation invocation,
            IReadOnlyList<RuleFact> facts
        )
        {
            if (invocation == null)
                throw new ArgumentNullException(nameof(invocation));
            if (facts == null)
                throw new ArgumentNullException(nameof(facts));

            lock (gate)
            {
                if (activeRoot.IsIdle || activeRoot.RootId != invocation.FrameView.RootId)
                    throw new InvalidOperationException(
                        "Reducer Facts crossed resolution root ownership."
                    );

                // Reducer Facts enter the root batch at the store commit point. Middleware may
                // replace the structural result or commit later children while it unwinds, neither
                // of which may discard or reorder an already committed Fact. Root aggregation also
                // retains the source frame's frozen listener selection for later notification.
                invocation.CaptureDirectFacts(facts);
                foreach (RuleFact fact in facts)
                {
                    activeRoot.AddFact(fact, invocation.FrameView.Id, invocation.FrameView.RootId);
                }
            }
        }

        private IReadOnlyList<CommittedFactRecord> SnapshotCommittedFacts(
            RootResolution resolution,
            OpId rootId
        )
        {
            lock (gate)
            {
                RequireActiveResolution(resolution);
                if (resolution.RootId != rootId)
                    throw new InvalidOperationException(
                        "Committed Facts crossed resolution root ownership."
                    );
                return Array.AsReadOnly(resolution.CommittedFacts.ToArray());
            }
        }

        private async ValueTask NotifyFactListeners(
            OpId rootId,
            IReadOnlyList<CommittedFactRecord> committedFacts
        )
        {
            if (
                committedFacts.Any(committed =>
                    committed == null
                    || committed.Fact == null
                    || !committed.Fact.IsStamped
                    || committed.Fact.RootOpId != rootId
                )
            )
            {
                throw new InvalidOperationException(
                    "A completed root contains a Fact from another resolution batch."
                );
            }

            IReadOnlyList<FactListenerDelivery> deliveries =
                ruleRegistry.BuildFactListenerDeliveries(rootId, committedFacts);
            foreach (FactListenerDelivery delivery in deliveries)
            {
                if (delivery.Registration.IsBatch)
                {
                    if (ruleRegistry.IsActive(store.Snapshot, delivery.Binding))
                    {
                        await InvokeFactListener(delivery, delivery.Facts, delivery.RootId);
                    }
                    continue;
                }

                foreach (RuleFact fact in delivery.Facts)
                {
                    if (!ruleRegistry.IsActive(store.Snapshot, delivery.Binding))
                        break;
                    await InvokeFactListener(
                        delivery,
                        Array.AsReadOnly(new[] { fact }),
                        fact.SourceOpId
                    );
                }
            }
        }

        private async ValueTask InvokeFactListener(
            FactListenerDelivery delivery,
            IReadOnlyList<RuleFact> facts,
            OpId causeId
        )
        {
            FactContext context = FactContext.Create(
                this,
                delivery.Binding,
                delivery.RootId,
                causeId
            );
            try
            {
                await delivery.Registration.Invoke(delivery.RootId, facts, context);
            }
            catch (Exception callbackException)
            {
                await CallbackFailure.AwaitCleanupPreservingPrimary(
                    callbackException,
                    context.CompleteInvocation()
                );
                throw;
            }

            if (await context.CompleteInvocation() == CallbackWorkCompletion.UnconsumedDispatch)
            {
                throw new InvalidOperationException(
                    $"Fact listener for {delivery.Registration.FactType.Name} returned before "
                        + "awaiting its causally linked dispatch."
                );
            }
        }
    }
}
