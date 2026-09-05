using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>Contributes ordered Unity presentation for one concrete action lifecycle.</summary>
    /// <typeparam name="TOp">The concrete feature-owned action type.</typeparam>
    /// <typeparam name="TResult">The action's existing feature-owned outcome type.</typeparam>
    public interface IUnityActionPresenter<in TOp, in TResult>
        where TOp : ActionOp<TResult>
    {
        /// <summary>
        /// Creates the presentation step that runs before the action's child mechanics are shown.
        /// </summary>
        /// <param name="action">The actual immutable action request and selection.</param>
        /// <param name="currentSnapshot">The exact snapshot associated with the begun occurrence.</param>
        /// <returns>A Unity coroutine step that may complete immediately.</returns>
        IEnumerator PresentBeginning(TOp action, RulesSnapshot currentSnapshot);

        /// <summary>Creates the final action-specific presentation step.</summary>
        /// <param name="action">The actual immutable action request and selection.</param>
        /// <param name="outcome">The actual outcome returned after all child mechanics.</param>
        /// <param name="currentSnapshot">The exact snapshot associated with resolution.</param>
        /// <returns>A Unity coroutine step that may complete immediately.</returns>
        IEnumerator PresentResolved(TOp action, TResult outcome, RulesSnapshot currentSnapshot);
    }

    /// <summary>
    /// Routes committed action-lifecycle Facts to explicitly composed typed Unity presenters.
    /// </summary>
    /// <remarks>
    /// Routing uses stable <see cref="ActionDefinitionId"/> values. The registry type-erases only
    /// at this shared boundary; each registration restores and verifies the concrete action and
    /// outcome pair before scheduling feature-owned presentation. Presenter methods create
    /// coroutines only when their ordered step is drained; they must not change rules state.
    /// </remarks>
    public sealed class UnityActionPresentationRegistry : IFactObserver<RuleFact>
    {
        private readonly Dictionary<
            ActionDefinitionId,
            IActionPresentationRegistration
        > registrations = new();
        private readonly UnityActionPresentationCoordinator coordinator;

        /// <summary>Creates an isolated typed presentation registry.</summary>
        public UnityActionPresentationRegistry()
            : this(new UnityActionPresentationCoordinator()) { }

        internal UnityActionPresentationRegistry(UnityActionPresentationCoordinator coordinator) =>
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        internal UnityActionPresentationCoordinator Coordinator => coordinator;

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
            if (fact is not IActionLifecycleFact lifecycle)
                return;
            if (!registrations.TryGetValue(lifecycle.DefinitionId, out var registration))
                return;
            if (fact is IActionBegunFact)
                registration.Begin(fact, rootId, currentSnapshot, coordinator);
            else if (fact is IActionResolvedFact)
            {
                registration.Resolve(fact, currentSnapshot, coordinator);
            }
        }

        private interface IActionPresentationRegistration
        {
            void Begin(
                RuleFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot,
                UnityActionPresentationCoordinator coordinator
            );

            void Resolve(
                RuleFact fact,
                RulesSnapshot currentSnapshot,
                UnityActionPresentationCoordinator coordinator
            );
        }

        private sealed class ActionPresentationRegistration<TOp, TResult>
            : IActionPresentationRegistration
            where TOp : ActionOp<TResult>
        {
            private readonly IUnityActionPresenter<TOp, TResult> presenter;

            internal ActionPresentationRegistration(
                IUnityActionPresenter<TOp, TResult> presenter
            ) => this.presenter = presenter;

            public void Begin(
                RuleFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot,
                UnityActionPresentationCoordinator coordinator
            )
            {
                if (fact is not ActionBegunFact<TResult> begun || begun.Action is not TOp action)
                {
                    throw new InvalidOperationException(
                        $"Action presentation for {typeof(TOp).Name} received an incompatible begun Fact."
                    );
                }
                if (begun.ActionInfo.RootId != rootId)
                    throw new InvalidOperationException(
                        "Action presentation received mismatched root provenance."
                    );

                coordinator.Begin(action, rootId);
                coordinator.Enqueue(
                    action,
                    () => presenter.PresentBeginning(action, currentSnapshot)
                );
            }

            public void Resolve(
                RuleFact fact,
                RulesSnapshot currentSnapshot,
                UnityActionPresentationCoordinator coordinator
            )
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
                coordinator.Enqueue(
                    action,
                    () => presenter.PresentResolved(action, resolved.Outcome, currentSnapshot)
                );
            }
        }
    }

    /// <summary>
    /// Retains encounter-scoped Unity coroutine steps in committed Fact order until the exact
    /// action caller drains them. The first execution failure is logged once, abandons the
    /// remaining steps, and releases both action and root correlation.
    /// </summary>
    internal sealed class UnityActionPresentationCoordinator : IDisposable
    {
        private readonly Dictionary<object, Sequence> byAction = new(ReferenceComparer.Instance);
        private readonly Dictionary<OpId, Sequence> byRoot = new();

        internal void Begin(object action, OpId rootId)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (rootId.IsEmpty)
                throw new ArgumentException(
                    "Action presentation requires a root ID.",
                    nameof(rootId)
                );
            if (byAction.ContainsKey(action) || byRoot.ContainsKey(rootId))
                throw new InvalidOperationException(
                    "An action presentation sequence is already active for this action or root."
                );

            Sequence sequence = new(action, rootId);
            byAction.Add(action, sequence);
            byRoot.Add(rootId, sequence);
        }

        internal void Enqueue(object action, Func<IEnumerator> step)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            if (!byAction.TryGetValue(action, out Sequence sequence))
                throw new InvalidOperationException(
                    "Action presentation was enqueued before its begun occurrence."
                );
            sequence.Steps.Enqueue(step);
        }

        internal bool TryEnqueue(OpId rootId, Func<IEnumerator> step)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            if (!byRoot.TryGetValue(rootId, out Sequence sequence))
                return false;
            sequence.Steps.Enqueue(step);
            return true;
        }

        internal IEnumerator Drain(object action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (!byAction.TryGetValue(action, out Sequence sequence))
                yield break;

            try
            {
                while (sequence.Steps.Count > 0)
                {
                    Func<IEnumerator> createStep = sequence.Steps.Dequeue();
                    IEnumerator step = null;
                    Exception failure;
                    while (TryMoveNext(createStep, ref step, out object current, out failure))
                        yield return current;
                    if (failure != null)
                    {
                        Debug.LogException(failure);
                        yield break;
                    }
                }
            }
            finally
            {
                byAction.Remove(sequence.Action);
                byRoot.Remove(sequence.RootId);
            }
        }

        private static bool TryMoveNext(
            Func<IEnumerator> createStep,
            ref IEnumerator step,
            out object current,
            out Exception failure
        )
        {
            try
            {
                step ??=
                    createStep()
                    ?? throw new InvalidOperationException(
                        "An action presenter returned no coroutine step."
                    );
                if (step.MoveNext())
                {
                    current = step.Current;
                    failure = null;
                    return true;
                }
            }
            catch (Exception exception)
            {
                current = null;
                failure = exception;
                return false;
            }

            current = null;
            failure = null;
            return false;
        }

        public void Dispose()
        {
            byAction.Clear();
            byRoot.Clear();
        }

        private sealed class Sequence
        {
            internal Sequence(object action, OpId rootId)
            {
                Action = action;
                RootId = rootId;
            }

            internal object Action { get; }
            internal OpId RootId { get; }
            internal Queue<Func<IEnumerator>> Steps { get; } = new();
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static ReferenceComparer Instance { get; } = new();

            public new bool Equals(object left, object right) => ReferenceEquals(left, right);

            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
    }

    /// <summary>Registers the shared action presentation observer for one encounter lifetime.</summary>
    internal sealed class UnityActionPresentationModule : Composition.IUnityEncounterRuntimeModule
    {
        private readonly UnityActionPresentationRegistry registry;

        internal UnityActionPresentationModule(UnityActionPresentationRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            lifetime.Add(dispatcher.RegisterFactObserver<RuleFact>(registry));
            lifetime.Add(registry.Coordinator);
        }
    }
}
