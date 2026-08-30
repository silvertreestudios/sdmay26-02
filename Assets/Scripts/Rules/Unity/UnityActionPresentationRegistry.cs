using System;
using System.Collections.Generic;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>Projects one concrete resolved action and its existing outcome into Unity.</summary>
    /// <typeparam name="TOp">The concrete feature-owned action type.</typeparam>
    /// <typeparam name="TResult">The action's existing feature-owned outcome type.</typeparam>
    public interface IUnityActionPresenter<in TOp, in TResult>
        where TOp : ActionOp<TResult>
    {
        /// <summary>
        /// Projects a committed action occurrence or enqueues Unity-owned asynchronous work.
        /// </summary>
        /// <param name="action">The actual immutable action request and selection.</param>
        /// <param name="outcome">The actual outcome returned after all child mechanics.</param>
        /// <param name="currentSnapshot">The exact snapshot associated with the occurrence.</param>
        void Present(TOp action, TResult outcome, RulesSnapshot currentSnapshot);
    }

    /// <summary>
    /// Routes committed action-resolution Facts to explicitly composed typed Unity presenters.
    /// </summary>
    /// <remarks>
    /// Routing uses stable <see cref="ActionDefinitionId"/> values. The registry type-erases only
    /// at this shared boundary; each registration restores and verifies the concrete action and
    /// outcome pair before invoking feature-owned presentation. Presenters must return immediately
    /// after direct projection or enqueueing asynchronous Unity work.
    /// </remarks>
    public sealed class UnityActionPresentationRegistry : IFactObserver<RuleFact>
    {
        private readonly Dictionary<
            ActionDefinitionId,
            IActionPresentationRegistration
        > registrations = new();

        /// <summary>Registers one typed presenter for a stable action definition.</summary>
        /// <typeparam name="TOp">The concrete action type accepted by the presenter.</typeparam>
        /// <typeparam name="TResult">The action outcome type accepted by the presenter.</typeparam>
        /// <param name="definitionId">The stable action definition used for routing.</param>
        /// <param name="presenter">The feature-owned typed presenter.</param>
        /// <exception cref="ArgumentException"><paramref name="definitionId"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="presenter"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The definition already has a presenter.</exception>
        public void Register<TOp, TResult>(
            ActionDefinitionId definitionId,
            IUnityActionPresenter<TOp, TResult> presenter
        )
            where TOp : ActionOp<TResult>
        {
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "An action presentation definition is required.",
                    nameof(definitionId)
                );
            if (presenter == null)
                throw new ArgumentNullException(nameof(presenter));
            if (
                !registrations.TryAdd(
                    definitionId,
                    new ActionPresentationRegistration<TOp, TResult>(presenter)
                )
            )
            {
                throw new InvalidOperationException(
                    $"Action presentation is already registered for '{definitionId}'."
                );
            }
        }

        /// <inheritdoc/>
        public void OnFactCommitted(RuleFact fact, OpId rootId, RulesSnapshot currentSnapshot)
        {
            if (fact == null)
                throw new ArgumentNullException(nameof(fact));
            if (currentSnapshot == null)
                throw new ArgumentNullException(nameof(currentSnapshot));
            if (fact is not IActionResolvedFact resolved)
                return;
            if (registrations.TryGetValue(resolved.DefinitionId, out var registration))
                registration.Present(fact, currentSnapshot);
        }

        private interface IActionPresentationRegistration
        {
            void Present(RuleFact fact, RulesSnapshot currentSnapshot);
        }

        private sealed class ActionPresentationRegistration<TOp, TResult>
            : IActionPresentationRegistration
            where TOp : ActionOp<TResult>
        {
            private readonly IUnityActionPresenter<TOp, TResult> presenter;

            internal ActionPresentationRegistration(
                IUnityActionPresenter<TOp, TResult> presenter
            ) => this.presenter = presenter;

            public void Present(RuleFact fact, RulesSnapshot currentSnapshot)
            {
                if (
                    fact is not ActionResolvedFact<TResult> resolved
                    || resolved.Action is not TOp action
                )
                {
                    throw new InvalidOperationException(
                        $"Action presentation for {typeof(TOp).Name} received an incompatible Fact."
                    );
                }
                presenter.Present(action, resolved.Outcome, currentSnapshot);
            }
        }
    }

    /// <summary>Registers the shared action presentation observer for one encounter lifetime.</summary>
    internal sealed class UnityActionPresentationModule : Composition.IUnityEncounterRuntimeModule
    {
        private readonly UnityActionPresentationRegistry registry;

        internal UnityActionPresentationModule(UnityActionPresentationRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime) =>
            lifetime.Add(dispatcher.RegisterFactObserver<RuleFact>(registry));
    }
}
