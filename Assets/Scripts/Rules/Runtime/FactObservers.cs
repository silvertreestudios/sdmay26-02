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
    /// isolated, reported through <see cref="IFactObserverExceptionReporter"/>, and never delay or
    /// fail reducers, handlers, rules Fact listeners, or other external observers.
    /// </remarks>
    public interface IFactObserver<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Observes an already committed Fact and its exact post-commit snapshot.
        /// </summary>
        /// <param name="fact">The committed transition or occurrence payload.</param>
        /// <param name="currentSnapshot">The exact immutable snapshot associated with the Fact.</param>
        void OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot);
    }

    /// <summary>Reports an isolated external Fact-observer failure.</summary>
    public interface IFactObserverExceptionReporter
    {
        /// <summary>Reports one observer exception without changing rules resolution.</summary>
        /// <param name="fact">The committed Fact whose observer failed.</param>
        /// <param name="exception">The observer exception.</param>
        void Report(RuleFact fact, Exception exception);
    }

    internal sealed class TraceFactObserverExceptionReporter : IFactObserverExceptionReporter
    {
        public static TraceFactObserverExceptionReporter Instance { get; } = new();

        private TraceFactObserverExceptionReporter() { }

        public void Report(RuleFact fact, Exception exception) =>
            System.Diagnostics.Trace.TraceError(
                $"Fact observer failed for {fact.GetType().Name}: {exception}"
            );
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
        public abstract void Invoke(RuleFact fact, RulesSnapshot currentSnapshot);
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

        public override void Invoke(RuleFact fact, RulesSnapshot currentSnapshot)
        {
            if (!(fact is TFact typedFact))
            {
                throw new InvalidOperationException(
                    $"Observer for {typeof(TFact).Name} received {fact.GetType().Name}."
                );
            }

            observer.OnFactCommitted(typedFact, currentSnapshot);
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

        internal void NotifyFactObservers(
            IReadOnlyList<RuleFact> committedFacts,
            RulesSnapshot currentSnapshot
        )
        {
            if (committedFacts == null)
                throw new ArgumentNullException(nameof(committedFacts));
            if (currentSnapshot == null)
                throw new ArgumentNullException(nameof(currentSnapshot));
            if (committedFacts.Count == 0)
                return;

            FactObserverRegistration[] observerPlan;
            lock (gate)
            {
                // Observer callbacks may change the live registry. A per-notification copy keeps
                // iteration stable without making reducers or the state commit aware of observers.
                observerPlan = factObservers.ToArray();
            }

            foreach (RuleFact fact in committedFacts)
            {
                foreach (FactObserverRegistration observer in observerPlan)
                {
                    if (!observer.Matches(fact))
                        continue;

                    try
                    {
                        observer.Invoke(fact, currentSnapshot);
                    }
                    catch (Exception exception)
                    {
                        ReportFactObserverFailure(fact, exception);
                    }
                }
            }
        }

        private void ReportFactObserverFailure(RuleFact fact, Exception exception)
        {
            try
            {
                factObserverExceptionReporter.Report(fact, exception);
            }
            catch (Exception reporterException)
            {
                System.Diagnostics.Trace.TraceError(
                    $"Fact observer reporter failed for {fact.GetType().Name}: {reporterException}"
                );
                System.Diagnostics.Trace.TraceError(
                    $"Original Fact observer failure for {fact.GetType().Name}: {exception}"
                );
            }
        }
    }
}
