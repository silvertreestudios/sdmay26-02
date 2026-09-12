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

        internal IReadOnlyList<CommittedFactRecord> CaptureCommittedFacts(
            IFrameInvocation invocation,
            IReadOnlyList<RuleFact> facts,
            RuleSource source,
            RulesSnapshot snapshot
        )
        {
            if (invocation == null)
                throw new ArgumentNullException(nameof(invocation));
            if (facts == null)
                throw new ArgumentNullException(nameof(facts));
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

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
                List<CommittedFactRecord> committedFacts = new List<CommittedFactRecord>(
                    facts.Count
                );
                foreach (RuleFact fact in facts)
                {
                    committedFacts.Add(
                        activeRoot.AddFact(
                            fact,
                            invocation.FrameView.Id,
                            invocation.FrameView.RootId,
                            source,
                            snapshot
                        )
                    );
                }
                return Array.AsReadOnly(committedFacts.ToArray());
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
                    committed == null || committed.Fact == null || committed.RootOpId != rootId
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
                        await InvokeFactListener(
                            delivery,
                            SelectFacts(delivery.CommittedFacts),
                            delivery.RootId
                        );
                    }
                    continue;
                }

                foreach (CommittedFactRecord committed in delivery.CommittedFacts)
                {
                    if (!ruleRegistry.IsActive(store.Snapshot, delivery.Binding))
                        break;
                    await InvokeFactListener(
                        delivery,
                        Array.AsReadOnly(new[] { committed.Fact }),
                        committed.SourceOpId
                    );
                }
            }
        }

        private static IReadOnlyList<RuleFact> SelectFacts(
            IReadOnlyList<CommittedFactRecord> committedFacts
        )
        {
            RuleFact[] facts = new RuleFact[committedFacts.Count];
            for (int index = 0; index < committedFacts.Count; index++)
                facts[index] = committedFacts[index].Fact;
            return Array.AsReadOnly(facts);
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
