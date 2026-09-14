using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Observes one typed Fact immediately after it commits.
    /// </summary>
    /// <typeparam name="TFact">The committed Fact type accepted by the observer.</typeparam>
    /// <remarks>
    /// Observation is a synchronous, non-authoritative notification boundary. Implementations
    /// must project immediately or enqueue host-owned asynchronous presentation and return. The
    /// callback receives no rules context and has no causal-dispatch authority. Exceptions are
    /// isolated, logged best-effort, and cannot fail or interrupt reducers, handlers, rules Fact
    /// listeners, or other external observers.
    /// </remarks>
    public interface IFactObserver<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Observes an already committed Fact and its exact post-commit snapshot.
        /// </summary>
        /// <param name="fact">The committed transition or occurrence payload.</param>
        /// <param name="observationRootId">
        /// The action or independent root shared by this Fact and supporting causal roots created
        /// while its rule listeners settle. A causally dispatched action opens a new observation
        /// root. Exact descendant-root provenance remains dispatcher-internal.
        /// </param>
        /// <param name="currentSnapshot">The immutable snapshot produced by the same reduction.</param>
        void OnFactCommitted(TFact fact, OpId observationRootId, RulesSnapshot currentSnapshot);
    }

    internal abstract class FactObserverRegistration
    {
        protected FactObserverRegistration(object observer)
        {
            Observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public object Observer { get; }
        public abstract Type FactType { get; }
        public abstract bool Matches(RuleFact fact);
        public abstract void Invoke(RuleFact fact, OpId rootId, RulesSnapshot currentSnapshot);
    }

    internal sealed class FactObserverRegistration<TFact> : FactObserverRegistration
        where TFact : RuleFact
    {
        private readonly IFactObserver<TFact> observer;

        public FactObserverRegistration(IFactObserver<TFact> observer)
            : base(observer)
        {
            this.observer = observer;
        }

        public override Type FactType => typeof(TFact);

        public override bool Matches(RuleFact fact) => fact is TFact;

        public override void Invoke(RuleFact fact, OpId rootId, RulesSnapshot currentSnapshot)
        {
            if (!(fact is TFact typedFact))
            {
                throw new InvalidOperationException(
                    $"Observer for {typeof(TFact).Name} received {fact.GetType().Name}."
                );
            }

            observer.OnFactCommitted(typedFact, rootId, currentSnapshot);
        }
    }

    public sealed partial class RuleDispatcher
    {
        private readonly List<FactObserverRegistration> factObservers =
            new List<FactObserverRegistration>();

        /// <summary>
        /// Registers a typed observer for later committed-Fact notification passes.
        /// </summary>
        /// <typeparam name="TFact">The Fact type to observe, including derived Fact types.</typeparam>
        /// <param name="observer">The observer appended to deterministic registration order.</param>
        /// <returns>
        /// An idempotent registration token. Disposing it removes this observer from later
        /// notification passes.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The same observer is already registered for <typeparamref name="TFact"/>.
        /// </exception>
        /// <remarks>
        /// Immediately before invoking observers, the dispatcher snapshots its current
        /// registrations for the entire notification pass. Registration changes made during a
        /// callback therefore apply to later reductions only.
        /// </remarks>
        public IDisposable RegisterFactObserver<TFact>(IFactObserver<TFact> observer)
            where TFact : RuleFact
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (gate)
            {
                if (
                    factObservers.Exists(registration =>
                        registration.FactType == typeof(TFact)
                        && ReferenceEquals(registration.Observer, observer)
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"The observer is already registered for {typeof(TFact).Name}."
                    );
                }

                factObservers.Add(new FactObserverRegistration<TFact>(observer));
            }

            return new DispatcherObserverRegistration(() => UnregisterFactObserver(observer));
        }

        /// <summary>
        /// Removes one typed observer from later committed-Fact notification passes.
        /// </summary>
        /// <typeparam name="TFact">The Fact type used when the observer was registered.</typeparam>
        /// <param name="observer">The observer to remove.</param>
        /// <returns><see langword="true"/> when a matching registration was removed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <remarks>
        /// Removal does not cancel callbacks already selected for an in-progress notification.
        /// </remarks>
        public bool UnregisterFactObserver<TFact>(IFactObserver<TFact> observer)
            where TFact : RuleFact
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (gate)
            {
                int index = factObservers.FindIndex(registration =>
                    registration.FactType == typeof(TFact)
                    && ReferenceEquals(registration.Observer, observer)
                );
                if (index < 0)
                    return false;

                factObservers.RemoveAt(index);
                return true;
            }
        }

        internal void NotifyFactObservers(IReadOnlyList<CommittedFactRecord> committedFacts)
        {
            if (committedFacts == null)
                throw new ArgumentNullException(nameof(committedFacts));
            if (committedFacts.Count == 0)
                return;

            FactObserverRegistration[] observerPlan;
            lock (gate)
            {
                // Observer callbacks may change the live registry. A per-notification copy keeps
                // iteration stable without making reducers or the state commit aware of observers.
                observerPlan = factObservers.ToArray();
            }

            foreach (CommittedFactRecord committed in committedFacts)
            {
                if (committed == null)
                    throw new InvalidOperationException("A Fact observer delivery cannot be null.");
                foreach (FactObserverRegistration observer in observerPlan)
                {
                    if (!observer.Matches(committed.Fact))
                        continue;

                    try
                    {
                        observer.Invoke(
                            committed.Fact,
                            committed.ObservationRootOpId,
                            committed.Snapshot
                        );
                    }
                    catch (Exception exception)
                    {
                        TraceFactObserverFailure(committed.Fact, exception);
                    }
                }
            }
        }

        private static void TraceFactObserverFailure(RuleFact fact, Exception exception)
        {
            try
            {
                System.Diagnostics.Trace.TraceError(
                    $"Fact observer failed for {fact.GetType().Name}: {exception}"
                );
            }
            catch
            {
                // Diagnostics are best-effort and cannot affect rules resolution.
            }
        }
    }
}
