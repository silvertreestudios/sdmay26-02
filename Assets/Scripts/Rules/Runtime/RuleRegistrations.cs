using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Owns a group of disposable resources that share one explicit lifetime.
    /// </summary>
    /// <remarks>
    /// Resources are released in reverse registration order so later resources can depend on
    /// earlier ones during cleanup. Disposal is idempotent and attempts every cleanup even when
    /// one resource fails.
    /// </remarks>
    public sealed class CompositeLifetime : IDisposable
    {
        private readonly object gate = new object();
        private readonly List<IDisposable> resources = new List<IDisposable>();
        private bool isDisposed;

        /// <summary>Adds a resource that will be disposed with this lifetime.</summary>
        /// <typeparam name="TResource">The disposable resource type.</typeparam>
        /// <param name="resource">The required resource to own.</param>
        /// <returns>
        /// The same resource, allowing callers to retain its more specific type while transferring
        /// cleanup ownership.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">This lifetime is already disposing or disposed.</exception>
        public TResource Add<TResource>(TResource resource)
            where TResource : IDisposable
        {
            if (ReferenceEquals(resource, null))
                throw new ArgumentNullException(nameof(resource));

            lock (gate)
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(CompositeLifetime));
                resources.Add(resource);
            }

            return resource;
        }

        /// <summary>
        /// Releases every owned resource once, in reverse registration order.
        /// </summary>
        /// <exception cref="AggregateException">More than one resource failed during cleanup.</exception>
        public void Dispose()
        {
            IDisposable[] ownedResources;
            lock (gate)
            {
                if (isDisposed)
                    return;

                isDisposed = true;
                ownedResources = resources.ToArray();
                resources.Clear();
            }

            List<Exception> failures = new List<Exception>();
            for (int index = ownedResources.Length - 1; index >= 0; index--)
            {
                try
                {
                    ownedResources[index].Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count == 0)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(
                "Multiple resources failed while disposing a composite lifetime.",
                failures
            );
        }
    }

    internal sealed class DispatcherObserverRegistration : IDisposable
    {
        private readonly object gate = new object();
        private readonly Action unregister;
        private bool isDisposed;

        internal DispatcherObserverRegistration(Action unregister)
        {
            this.unregister = unregister ?? throw new ArgumentNullException(nameof(unregister));
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (isDisposed)
                    return;

                unregister();
                isDisposed = true;
            }
        }
    }

    /// <summary>
    /// Describes one registered middleware extension without storing active-instance state.
    /// </summary>
    public abstract class MiddlewareRegistration
    {
        internal MiddlewareRegistration(
            Type operationType,
            Type resultType,
            RuleLifecyclePhase phase,
            long registrationOrder
        )
        {
            OperationType = operationType ?? throw new ArgumentNullException(nameof(operationType));
            ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
            Phase = phase;
            RegistrationOrder = registrationOrder;
        }

        /// <summary>
        /// Gets the concrete operation type wrapped by this registration.
        /// </summary>
        public Type OperationType { get; }

        /// <summary>
        /// Gets the successful result type required from the operation's resolver.
        /// </summary>
        public Type ResultType { get; }

        /// <summary>
        /// Gets the semantic lifecycle stage used for deterministic ordering.
        /// </summary>
        public RuleLifecyclePhase Phase { get; }

        internal long RegistrationOrder { get; }

        internal abstract ValueTask<object> Invoke(
            ActiveRuleBinding binding,
            IFrameInvocation invocation,
            RuleDispatcher dispatcher,
            Func<ValueTask<object>> next
        );
    }

    /// <summary>
    /// Describes one typed post-commit listener extension without storing active-instance state.
    /// </summary>
    public abstract class FactListenerRegistration
    {
        internal FactListenerRegistration(
            Type factType,
            RuleLifecyclePhase phase,
            bool isBatch,
            long registrationOrder
        )
        {
            FactType = factType ?? throw new ArgumentNullException(nameof(factType));
            Phase = phase;
            IsBatch = isBatch;
            RegistrationOrder = registrationOrder;
        }

        /// <summary>
        /// Gets the committed Fact type selected by this registration.
        /// </summary>
        public Type FactType { get; }

        /// <summary>
        /// Gets the semantic lifecycle stage used for deterministic ordering.
        /// </summary>
        public RuleLifecyclePhase Phase { get; }

        /// <summary>
        /// Gets whether matching Facts are delivered together once per committed root.
        /// </summary>
        public bool IsBatch { get; }

        internal long RegistrationOrder { get; }
        internal abstract bool Matches(RuleFact fact);
        internal abstract ValueTask Invoke(
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            FactContext context
        );
    }
}
