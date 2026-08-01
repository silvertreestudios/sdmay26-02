using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Observes one typed operation immediately after it has structurally resolved.</summary>
    /// <typeparam name="TOp">The concrete operation type to observe.</typeparam>
    /// <typeparam name="TResult">The resolved value type declared by the operation.</typeparam>
    /// <remarks>
    /// The callback is awaited before the resolved operation returns to its parent. It receives no
    /// dispatcher context and therefore has no authority to dispatch rules work. This seam is for
    /// external adapters that must pace parent continuation from an already settled result; rules
    /// responding to committed state changes must observe typed <see cref="RuleFact"/> instances.
    /// Invalid, interrupted, and cancelled operations are never delivered.
    /// </remarks>
    public interface IResolvedOpObserver<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        /// <summary>Observes a typed operation, its resolved value, and current immutable state.</summary>
        /// <param name="operation">The operation that resolved.</param>
        /// <param name="result">The operation's resolved value.</param>
        /// <param name="currentSnapshot">The immutable snapshot current at completion.</param>
        /// <returns>A task-like value that completes when observation has finished.</returns>
        ValueTask OnOperationResolved(TOp operation, TResult result, RulesSnapshot currentSnapshot);
    }

    internal abstract class ResolvedOpObserverRegistration
    {
        protected ResolvedOpObserverRegistration(object observer)
        {
            Observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public object Observer { get; }
        public abstract Type OperationType { get; }
        public abstract Type ResultType { get; }
        public abstract ValueTask Invoke(
            IRuleOp operation,
            object result,
            RulesSnapshot currentSnapshot
        );
    }

    internal sealed class ResolvedOpObserverRegistration<TOp, TResult>
        : ResolvedOpObserverRegistration
        where TOp : IRuleOp<TResult>
    {
        private readonly IResolvedOpObserver<TOp, TResult> observer;

        public ResolvedOpObserverRegistration(IResolvedOpObserver<TOp, TResult> observer)
            : base(observer)
        {
            this.observer = observer;
        }

        public override Type OperationType => typeof(TOp);
        public override Type ResultType => typeof(TResult);

        public override ValueTask Invoke(
            IRuleOp operation,
            object result,
            RulesSnapshot currentSnapshot
        )
        {
            if (!(operation is TOp typedOperation) || !(result is TResult typedResult))
            {
                throw new InvalidOperationException(
                    $"Resolved observer for {typeof(TOp).Name} received incompatible values."
                );
            }

            return observer.OnOperationResolved(typedOperation, typedResult, currentSnapshot);
        }
    }

    public sealed partial class RuleDispatcher
    {
        private static readonly ObserverFailureState EmptyResolvedOperationObserverFailures =
            ObserverFailureState.CreateEmpty(
                "Multiple resolved-operation observers failed after the operation resolved."
            );

        private readonly List<ResolvedOpObserverRegistration> resolvedOpObservers =
            new List<ResolvedOpObserverRegistration>();

        /// <summary>Registers a typed observer for later resolved-operation notification passes.</summary>
        /// <typeparam name="TOp">The exact concrete operation type to observe.</typeparam>
        /// <typeparam name="TResult">The operation's declared resolved value type.</typeparam>
        /// <param name="observer">The observer appended to deterministic registration order.</param>
        /// <returns>
        /// An idempotent registration token. Disposing it removes this observer from later
        /// notification passes.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The same observer is already registered for this operation and result type.
        /// </exception>
        public IDisposable RegisterResolvedOpObserver<TOp, TResult>(
            IResolvedOpObserver<TOp, TResult> observer
        )
            where TOp : IRuleOp<TResult>
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (gate)
            {
                if (
                    resolvedOpObservers.Exists(registration =>
                        registration.OperationType == typeof(TOp)
                        && registration.ResultType == typeof(TResult)
                        && ReferenceEquals(registration.Observer, observer)
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"The observer is already registered for {typeof(TOp).Name}."
                    );
                }

                resolvedOpObservers.Add(new ResolvedOpObserverRegistration<TOp, TResult>(observer));
            }

            return new DispatcherObserverRegistration(() =>
                UnregisterResolvedOpObserver<TOp, TResult>(observer)
            );
        }

        /// <summary>Removes one typed observer from later notification passes.</summary>
        /// <typeparam name="TOp">The operation type used at registration.</typeparam>
        /// <typeparam name="TResult">The result type used at registration.</typeparam>
        /// <param name="observer">The observer to remove.</param>
        /// <returns><see langword="true"/> when a matching registration was removed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        public bool UnregisterResolvedOpObserver<TOp, TResult>(
            IResolvedOpObserver<TOp, TResult> observer
        )
            where TOp : IRuleOp<TResult>
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (gate)
            {
                int index = resolvedOpObservers.FindIndex(registration =>
                    registration.OperationType == typeof(TOp)
                    && registration.ResultType == typeof(TResult)
                    && ReferenceEquals(registration.Observer, observer)
                );
                if (index < 0)
                    return false;

                resolvedOpObservers.RemoveAt(index);
                return true;
            }
        }

        private async ValueTask NotifyResolvedOpObservers(
            IRuleOp operation,
            Type resultType,
            object result,
            RulesSnapshot currentSnapshot
        )
        {
            Type operationType = operation.GetType();
            ResolvedOpObserverRegistration[] observerPlan;
            lock (gate)
            {
                observerPlan = resolvedOpObservers
                    .FindAll(registration =>
                        registration.OperationType == operationType
                        && registration.ResultType == resultType
                    )
                    .ToArray();
            }

            ObserverFailureState failures = EmptyResolvedOperationObserverFailures;
            foreach (ResolvedOpObserverRegistration observer in observerPlan)
            {
                try
                {
                    await observer.Invoke(operation, result, currentSnapshot);
                }
                catch (Exception exception)
                {
                    failures = failures.Add(exception);
                }
            }

            failures.ThrowIfAny();
        }
    }
}
