using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Provides all matching Facts of one type committed beneath one root resolution.
    /// </summary>
    /// <typeparam name="TFact">The common committed Fact type in the batch.</typeparam>
    public sealed class CommittedFactBatch<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Gets the completed root operation that committed the Facts.
        /// </summary>
        public OpId RootId { get; }

        /// <summary>
        /// Gets the matching Facts in their deterministic commit order.
        /// </summary>
        public IReadOnlyList<TFact> Facts { get; }

        internal CommittedFactBatch(OpId rootId, IReadOnlyList<TFact> facts)
        {
            if (rootId.IsEmpty)
                throw new ArgumentException(
                    "A committed Fact batch requires a root operation ID.",
                    nameof(rootId)
                );
            if (facts == null || facts.Count == 0)
                throw new ArgumentException(
                    "A committed Fact batch cannot be empty.",
                    nameof(facts)
                );
            if (facts.Any(fact => fact == null || !fact.IsStamped || fact.RootOpId != rootId))
                throw new InvalidOperationException(
                    "Every Fact in a committed batch must belong to its root."
                );

            RootId = rootId;
            Facts = facts;
        }
    }

    /// <summary>
    /// Exposes post-commit state and causally linked dispatch to one Fact-listener invocation.
    /// </summary>
    /// <remarks>
    /// The context is valid only while its listener is executing. A listener may have at most one
    /// dispatched root in flight and must await it before returning or dispatching another. Any
    /// dispatched operation begins a new root resolution; it cannot retroactively alter the Facts
    /// that caused the notification. If the listener and unconsumed dispatched work both fail,
    /// dispatch reports both failures in an <see cref="AggregateException"/>, with the listener
    /// failure first.
    /// </remarks>
    public sealed class FactContext
    {
        private readonly RuleDispatcher dispatcher;
        private readonly OpId causeId;
        private readonly CallbackWorkCoordinator work;

        private FactContext(
            RuleDispatcher dispatcher,
            ActiveRuleBinding binding,
            OpId committedRootId,
            OpId causeId,
            CallbackWorkCoordinator work
        )
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            this.work = work ?? throw new ArgumentNullException(nameof(work));
            if (committedRootId.IsEmpty || causeId.IsEmpty)
                throw new ArgumentException("Fact contexts require committed root and cause IDs.");
            CommittedRootId = committedRootId;
            this.causeId = causeId;
        }

        /// <summary>
        /// Gets the active binding authorizing the current listener.
        /// </summary>
        public ActiveRuleBinding Binding { get; }

        /// <summary>
        /// Gets the stable rule source associated with <see cref="Binding"/>.
        /// </summary>
        public RuleSource Source => Binding.Source;

        /// <summary>
        /// Gets the root operation whose committed Facts caused this notification.
        /// </summary>
        public OpId CommittedRootId { get; }

        /// <summary>
        /// Gets the latest committed rules snapshot, including the state described by the Fact.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The listener callback that owned this context has returned.
        /// </exception>
        public RulesSnapshot Snapshot
        {
            get
            {
                RequireActive();
                return dispatcher.Snapshot;
            }
        }

        /// <summary>
        /// Gets the lifetime trace containing the committed root and listener-dispatched work.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The listener callback that owned this context has returned.
        /// </exception>
        public ResolutionTrace Trace
        {
            get
            {
                RequireActive();
                return dispatcher.Trace;
            }
        }

        /// <summary>
        /// Dispatches an externally allowed operation as a new root caused by this notification.
        /// </summary>
        /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
        /// <param name="op">The operation that reacts to the committed Fact or batch.</param>
        /// <returns>The structural result of the new causally linked root.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="op"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The listener has returned, another listener dispatch is in flight, or the operation is
        /// not registered for external invocation.
        /// </exception>
        public ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            const string overlapMessage =
                "A Fact listener cannot overlap dispatched roots. Await the active dispatch first.";
            return work.StartDispatch(
                () => dispatcher.DispatchFromFact(op, CommittedRootId, causeId),
                "A Fact context cannot dispatch after its listener returns.",
                overlapMessage,
                overlapMessage
            );
        }

        internal ValueTask<CallbackWorkCompletion> CompleteInvocation() =>
            work.CompleteInvocation("A Fact context completed more than once.");

        internal static FactContext Create(
            RuleDispatcher dispatcher,
            ActiveRuleBinding binding,
            OpId committedRootId,
            OpId causeId
        ) =>
            new FactContext(
                dispatcher,
                binding,
                committedRootId,
                causeId,
                new CallbackWorkCoordinator()
            );

        private void RequireActive() =>
            work.RequireActive("A Fact context cannot be used after its listener returns.");
    }
}
