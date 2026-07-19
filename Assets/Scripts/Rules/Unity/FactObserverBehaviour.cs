using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Connects one typed Fact observer to a dispatcher's dynamic registry for this component's
    /// enabled lifetime.
    /// </summary>
    /// <typeparam name="TFact">The committed Fact type handled by the component.</typeparam>
    /// <remarks>
    /// A concrete, non-generic component derives from this helper and is configured explicitly by
    /// its composition root. Configuration does not use a singleton or static event. Disabling or
    /// destroying the component prevents later reductions from selecting it but does not cancel a
    /// callback already frozen for an in-flight reduction.
    /// </remarks>
    public abstract class FactObserverBehaviour<TFact> : MonoBehaviour, IFactObserver<TFact>
        where TFact : RuleFact
    {
        private IRegistrationState registrationState = UnconfiguredState.Instance;

        /// <summary>
        /// Selects the dispatcher whose committed reductions this component observes.
        /// </summary>
        /// <param name="dispatcher">The dispatcher owned by the surrounding composition root.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is null.</exception>
        /// <remarks>
        /// Reconfiguration first unregisters from the previous dispatcher. If this component is
        /// currently active and enabled, registration with the new dispatcher happens immediately.
        /// </remarks>
        public void Configure(RuleDispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));

            registrationState = registrationState.Unregister(this);
            registrationState = new UnregisteredState(dispatcher);
            if (isActiveAndEnabled)
                registrationState = registrationState.Register(this);
        }

        /// <inheritdoc/>
        public abstract ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot);

        private void OnEnable()
        {
            registrationState = registrationState.Register(this);
        }

        private void OnDisable()
        {
            registrationState = registrationState.Unregister(this);
        }

        private void OnDestroy()
        {
            registrationState = registrationState.Unregister(this);
        }

        private interface IRegistrationState
        {
            IRegistrationState Register(IFactObserver<TFact> observer);
            IRegistrationState Unregister(IFactObserver<TFact> observer);
        }

        private sealed class UnconfiguredState : IRegistrationState
        {
            public static UnconfiguredState Instance { get; } = new UnconfiguredState();

            private UnconfiguredState()
            {
            }

            public IRegistrationState Register(IFactObserver<TFact> observer) => this;

            public IRegistrationState Unregister(IFactObserver<TFact> observer) => this;
        }

        private sealed class UnregisteredState : IRegistrationState
        {
            private readonly RuleDispatcher dispatcher;

            public UnregisteredState(RuleDispatcher dispatcher)
            {
                this.dispatcher = dispatcher;
            }

            public IRegistrationState Register(IFactObserver<TFact> observer)
            {
                dispatcher.RegisterFactObserver(observer);
                return new RegisteredState(dispatcher);
            }

            public IRegistrationState Unregister(IFactObserver<TFact> observer) => this;
        }

        private sealed class RegisteredState : IRegistrationState
        {
            private readonly RuleDispatcher dispatcher;

            public RegisteredState(RuleDispatcher dispatcher)
            {
                this.dispatcher = dispatcher;
            }

            public IRegistrationState Register(IFactObserver<TFact> observer) => this;

            public IRegistrationState Unregister(IFactObserver<TFact> observer)
            {
                dispatcher.UnregisterFactObserver(observer);
                return new UnregisteredState(dispatcher);
            }
        }
    }
}
