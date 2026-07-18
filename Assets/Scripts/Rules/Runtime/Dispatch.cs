using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Resolves typed rules operations while preserving frame provenance, committed facts, and diagnostics.
    /// </summary>
    /// <remarks>
    /// Root resolutions are serialized: a second external root waits until the active root and its
    /// post-commit listeners finish.
    /// Handlers may dispatch nested children through <see cref="OpHandlerContext"/>, but each active frame
    /// may own only one child at a time and must await it. Committed-Fact listeners finish before
    /// the caller regains root ownership; listener-dispatched work runs as serialized causal roots.
    /// Trace and diagnostic history accumulate for the lifetime of the dispatcher.
    /// </remarks>
    public sealed partial class RuleDispatcher
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts =
            Array.AsReadOnly(Array.Empty<RuleFact>());
        private static readonly IReadOnlyList<BoundMiddlewareRegistration> NoMiddleware =
            Array.AsReadOnly(Array.Empty<BoundMiddlewareRegistration>());
        private readonly object gate = new object();
        private readonly SemaphoreSlim rootSerial = new SemaphoreSlim(1, 1);
        private readonly IRulesStore store;
        private readonly IOpIdProvider ids;
        private readonly IRollService rollService;
        private readonly IReadOnlyDictionary<Type, IRegistration> registrations;
        private readonly RuleRegistry ruleRegistry;
        private readonly ActionRuntime actionRuntime;
        private RootResolution activeRoot = RootResolution.Idle;

        internal RuleDispatcher(
            IRulesStore store,
            IOpIdProvider ids,
            IRollService rollService,
            IDictionary<Type, IRegistration> registrations,
            RuleRegistry ruleRegistry,
            ActionRuntime actionRuntime)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            this.rollService = rollService ?? throw new ArgumentNullException(nameof(rollService));
            this.registrations = new ReadOnlyDictionary<Type, IRegistration>(
                new Dictionary<Type, IRegistration>(registrations));
            this.ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
            this.actionRuntime = actionRuntime ?? throw new ArgumentNullException(nameof(actionRuntime));
            Trace = new ResolutionTrace();
            Diagnostics = new ResolutionDiagnostics(Trace);
        }

        /// <summary>
        /// Gets the latest immutable snapshot committed by the rules store.
        /// </summary>
        public RulesSnapshot Snapshot => store.Snapshot;

        /// <summary>
        /// Gets the lifetime trace of operation frames created by this dispatcher.
        /// </summary>
        public ResolutionTrace Trace { get; }

        /// <summary>
        /// Gets the human-readable diagnostics associated with <see cref="Trace"/>.
        /// </summary>
        public ResolutionDiagnostics Diagnostics { get; }

        /// <summary>
        /// Dispatches an externally allowed operation as a new root resolution.
        /// </summary>
        /// <typeparam name="TResult">The successful result type declared by the operation.</typeparam>
        /// <param name="op">
        /// The operation to resolve. Its concrete runtime type must have a compatible registration.
        /// </param>
        /// <returns>A task-like value containing the root status, value, and all committed subtree facts.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="op"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The operation is nested-only, no compatible resolver is registered, or a handler violates
        /// nested-dispatch ownership.
        /// </exception>
        /// <remarks>
        /// Resolver, middleware, and post-commit listener exceptions propagate to the caller. State
        /// already committed by a reducer is not rolled back. If resolution fails after a commit,
        /// listeners receive the durable Facts before the resolution exception is rethrown. If that
        /// notification also fails, an <see cref="AggregateException"/> reports the resolution
        /// exception first and the notification exception second. The dispatcher then releases root
        /// ownership so a later independent root may be dispatched. When a callback and its unconsumed
        /// work both fail, their aggregate likewise retains the callback exception first. Other
        /// external roots remain queued until this entire resolution releases ownership.
        /// </remarks>
        public async ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            RootResolution resolution = new RootResolution();
            await rootSerial.WaitAsync();
            try
            {
                lock (gate)
                {
                    if (!activeRoot.IsIdle)
                    {
                        throw new InvalidOperationException(
                            "Serialized root ownership was not released before the next root began.");
                    }

                    activeRoot = resolution;
                }

                IRegistration registration = RequireRegistration(op.GetType(), typeof(TResult));
                if (registration.Policy != InvocationPolicy.ExternalAllowed)
                {
                    throw new InvalidOperationException(
                        $"{op.GetType().Name} is nested-only and cannot be externally dispatched.");
                }

                OpId rootId;
                lock (gate)
                {
                    RequireActiveResolution(resolution);
                    rootId = ids.Next();
                    resolution.Initialize(rootId);
                }

                return await DispatchRoot(
                    op,
                    registration,
                    resolution,
                    rootId,
                    null);
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(activeRoot, resolution))
                        activeRoot = RootResolution.Idle;
                }
                rootSerial.Release();
            }
        }

        internal async ValueTask<OpResult<TResult>> DispatchNested<TResult>(
            IRuleOp<TResult> op,
            OpId parentId)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            RootResolution resolution;
            ChildReservation reservation;
            lock (gate)
            {
                if (activeRoot.IsIdle)
                    throw new InvalidOperationException("Nested dispatch requires an active root resolution.");

                resolution = activeRoot;
                reservation = resolution.ReserveChild(parentId);
            }

            try
            {
                IRegistration registration = RequireRegistration(op.GetType(), typeof(TResult));
                return await DispatchCore(
                    op,
                    registration,
                    resolution,
                    resolution.RootId,
                    parentId,
                    parentId);
            }
            finally
            {
                try
                {
                    lock (gate)
                        resolution.ReleaseChild(reservation);
                }
                finally
                {
                    reservation.Settle();
                }
            }
        }

        internal async ValueTask<OpResult<TResult>> DispatchFromFact<TResult>(
            IRuleOp<TResult> op,
            OpId committedRootId,
            OpId causeId)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            IRegistration registration = RequireRegistration(op.GetType(), typeof(TResult));
            if (registration.Policy != InvocationPolicy.ExternalAllowed)
            {
                throw new InvalidOperationException(
                    $"{op.GetType().Name} is nested-only and cannot begin a Fact-listener batch.");
            }

            RootResolution owner;
            RootResolution triggered = new RootResolution();
            OpId rootId;
            lock (gate)
            {
                if (activeRoot.IsIdle || activeRoot.RootId != committedRootId)
                {
                    throw new InvalidOperationException(
                        "Fact-listener dispatch requires its completed root to retain resolution ownership.");
                }

                owner = activeRoot;
                rootId = ids.Next();
                triggered.Initialize(rootId);
                activeRoot = triggered;
            }

            try
            {
                return await DispatchRoot(op, registration, triggered, rootId, causeId);
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(activeRoot, triggered))
                        activeRoot = owner;
                }
            }
        }
    }
}
