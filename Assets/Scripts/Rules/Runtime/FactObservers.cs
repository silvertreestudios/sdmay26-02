using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Observes one typed Fact immediately after the reduction that committed it.
    /// </summary>
    /// <typeparam name="TFact">The committed Fact type accepted by the observer.</typeparam>
    /// <remarks>
    /// Observation is awaited before the reducer result returns to its parent handler. The state
    /// transition is already durable, so an observer may pace subsequent work but cannot alter,
    /// cancel, or roll back the commit. Use the current snapshot for current-state lookups;
    /// transition-specific values belong on the Fact itself. The callback receives no rules
    /// context and has no causal-dispatch authority.
    /// </remarks>
    public interface IFactObserver<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Observes an already committed Fact and its exact post-commit snapshot.
        /// </summary>
        /// <param name="fact">The committed transition payload.</param>
        /// <param name="currentSnapshot">The immutable snapshot produced by the same reduction.</param>
        /// <returns>A task-like value that completes when observation has finished.</returns>
        ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot);
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
        public abstract ValueTask Invoke(RuleFact fact, RulesSnapshot currentSnapshot);
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

        public override ValueTask Invoke(RuleFact fact, RulesSnapshot currentSnapshot)
        {
            if (!(fact is TFact typedFact))
            {
                throw new InvalidOperationException(
                    $"Observer for {typeof(TFact).Name} received {fact.GetType().Name}.");
            }

            return observer.OnFactCommitted(typedFact, currentSnapshot);
        }
    }

    public sealed partial class RuleDispatcher
    {
        private readonly List<FactObserverRegistration> factObservers =
            new List<FactObserverRegistration>();

        /// <summary>
        /// Registers a typed observer for reductions committed after this call.
        /// </summary>
        /// <typeparam name="TFact">The Fact type to observe, including derived Fact types.</typeparam>
        /// <param name="observer">The observer appended to deterministic registration order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The same observer is already registered for <typeparamref name="TFact"/>.
        /// </exception>
        /// <remarks>
        /// Registration and observer selection are ordered atomically at the reduction commit
        /// boundary. Each committed reduction then retains that immutable delivery plan while it
        /// invokes callbacks. Registration changes made during a callback therefore apply to later
        /// reductions only.
        /// </remarks>
        public void RegisterFactObserver<TFact>(IFactObserver<TFact> observer)
            where TFact : RuleFact
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (gate)
            {
                if (factObservers.Exists(registration =>
                    registration.FactType == typeof(TFact) &&
                    ReferenceEquals(registration.Observer, observer)))
                {
                    throw new InvalidOperationException(
                        $"The observer is already registered for {typeof(TFact).Name}.");
                }

                factObservers.Add(new FactObserverRegistration<TFact>(observer));
            }
        }

        /// <summary>
        /// Removes one typed observer from reductions committed after this call.
        /// </summary>
        /// <typeparam name="TFact">The Fact type used when the observer was registered.</typeparam>
        /// <param name="observer">The observer to remove.</param>
        /// <returns><see langword="true"/> when a matching registration was removed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <remarks>
        /// Removal and observer selection are ordered atomically at the reduction commit boundary.
        /// Removal does not cancel callbacks already frozen for an in-flight reduction.
        /// </remarks>
        public bool UnregisterFactObserver<TFact>(IFactObserver<TFact> observer)
            where TFact : RuleFact
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (gate)
            {
                int index = factObservers.FindIndex(registration =>
                    registration.FactType == typeof(TFact) &&
                    ReferenceEquals(registration.Observer, observer));
                if (index < 0)
                    return false;

                factObservers.RemoveAt(index);
                return true;
            }
        }

        internal async ValueTask NotifyFactObservers(
            IReadOnlyList<RuleFact> committedFacts,
            RulesSnapshot currentSnapshot,
            IReadOnlyList<FactObserverRegistration> committedObserverPlan)
        {
            if (committedFacts == null)
                throw new ArgumentNullException(nameof(committedFacts));
            if (currentSnapshot == null)
                throw new ArgumentNullException(nameof(currentSnapshot));
            if (committedObserverPlan == null)
                throw new ArgumentNullException(nameof(committedObserverPlan));
            if (committedFacts.Count == 0)
                return;

            List<Exception> failures = new List<Exception>();
            foreach (RuleFact fact in committedFacts)
            {
                foreach (FactObserverRegistration observer in committedObserverPlan)
                {
                    if (!observer.Matches(fact))
                        continue;

                    try
                    {
                        await observer.Invoke(fact, currentSnapshot);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "Multiple Fact observers failed after the reduction committed.",
                    failures);
            }
        }
    }
}
