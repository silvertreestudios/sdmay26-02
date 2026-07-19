using System;
using System.Collections.Generic;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Presents one concrete committed Fact through a focused Unity adapter.
    /// </summary>
    /// <typeparam name="TFact">The exact Fact type handled by the adapter.</typeparam>
    public interface IUnityFactPresenter<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Starts or applies presentation for one already committed Fact.
        /// </summary>
        /// <param name="fact">The typed committed Fact.</param>
        /// <param name="commit">The immutable commit envelope and before/after snapshots.</param>
        /// <remarks>
        /// A presenter may update Unity views or begin animation, but it must never mutate rules
        /// state or make completion of that animation a condition of the rules commit.
        /// </remarks>
        void Present(TFact fact, CommittedRuleFact commit);
    }

    /// <summary>
    /// Dispatches committed Facts to typed presenter registrations without feature-name switches.
    /// </summary>
    public sealed class UnityFactPresenterRegistry
    {
        private readonly Dictionary<Type, List<IPresenterRegistration>> registrations =
            new Dictionary<Type, List<IPresenterRegistration>>();

        /// <summary>
        /// Registers one presenter for an exact concrete Fact type.
        /// </summary>
        /// <typeparam name="TFact">The Fact type presented by <paramref name="presenter"/>.</typeparam>
        /// <param name="presenter">The required focused Unity presenter.</param>
        /// <returns>This registry so composition can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="presenter"/> is <see langword="null"/>.</exception>
        public UnityFactPresenterRegistry Register<TFact>(
            IUnityFactPresenter<TFact> presenter)
            where TFact : RuleFact
        {
            if (presenter == null)
                throw new ArgumentNullException(nameof(presenter));
            if (!registrations.TryGetValue(
                typeof(TFact),
                out List<IPresenterRegistration> typedRegistrations))
            {
                typedRegistrations = new List<IPresenterRegistration>();
                registrations.Add(typeof(TFact), typedRegistrations);
            }
            typedRegistrations.Add(new PresenterRegistration<TFact>(presenter));
            return this;
        }

        /// <summary>
        /// Invokes matching presenters once in registration order.
        /// </summary>
        /// <param name="commit">The committed Fact and snapshot pair to present.</param>
        /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <see langword="null"/>.</exception>
        public void Present(CommittedRuleFact commit)
        {
            if (commit == null)
                throw new ArgumentNullException(nameof(commit));
            if (!registrations.TryGetValue(
                commit.Fact.GetType(),
                out List<IPresenterRegistration> typedRegistrations))
            {
                return;
            }

            foreach (IPresenterRegistration registration in typedRegistrations)
                registration.Present(commit);
        }

        private interface IPresenterRegistration
        {
            void Present(CommittedRuleFact commit);
        }

        private sealed class PresenterRegistration<TFact> : IPresenterRegistration
            where TFact : RuleFact
        {
            private readonly IUnityFactPresenter<TFact> presenter;

            public PresenterRegistration(IUnityFactPresenter<TFact> presenter) =>
                this.presenter = presenter;

            public void Present(CommittedRuleFact commit)
            {
                if (!(commit.Fact is TFact fact))
                {
                    throw new InvalidOperationException(
                        "A typed Fact presenter received an incompatible registration.");
                }
                presenter.Present(fact, commit);
            }
        }
    }
}
