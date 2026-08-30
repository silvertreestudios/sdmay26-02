using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Runs root-owned work after resolution and before binding-scoped Fact listeners.</summary>
    /// <typeparam name="TResult">The operation's successful structural result type.</typeparam>
    /// <remarks>
    /// This narrow transaction boundary exists for host state that must become resolvable with the
    /// reducer commit and for accepted action work that must settle before an unrelated queued root
    /// can begin. The callback remains inside root serialization and may await a public dispatcher
    /// call; that call becomes a causally linked root instead of waiting on the serialization gate.
    /// A callback must await each dispatcher call before starting another so sibling causal roots
    /// retain their exact parent instead of overlapping one another.
    /// </remarks>
    public interface IRootResolutionObserver<TResult>
    {
        /// <summary>Settles accepted root-owned work before its Fact listeners run.</summary>
        /// <param name="rootId">The exact completed resolution root.</param>
        /// <param name="result">
        /// The root's structural result. Observers must not publish or execute accepted work for an
        /// invalid result.
        /// </param>
        /// <param name="snapshot">The latest snapshot after root resolution.</param>
        /// <returns>A task-like value that settles publication and any causal work.</returns>
        ValueTask OnRootResolved(OpId rootId, OpResult<TResult> result, RulesSnapshot snapshot);
    }

    /// <summary>Observes each exact root after all of its binding-scoped Fact listeners settle.</summary>
    /// <remarks>
    /// Observers execute before the dispatcher releases serialization to an unrelated queued root.
    /// They may sequentially await public dispatcher calls, which become causally linked roots;
    /// each call must finish before another begins. The callback remains responsible for finishing
    /// all of its causal work before it returns. This hook is intended for host presentation
    /// transactions, not for changing the already-settled root result.
    /// </remarks>
    public interface IRootSettlementObserver
    {
        /// <summary>Settles host callbacks owned by one exact completed root.</summary>
        /// <param name="rootId">The root whose listeners and causal work have finished.</param>
        /// <param name="causalParentRootId">
        /// The immediate root whose callback caused this root, or no value for an external root.
        /// A host may use this relationship to retain exact-root ownership while ordering work
        /// across one completed causal tree.
        /// </param>
        /// <param name="snapshot">The latest committed snapshot at the settlement boundary.</param>
        /// <returns>A task-like value that completes after host settlement.</returns>
        ValueTask OnRootSettled(OpId rootId, OpId? causalParentRootId, RulesSnapshot snapshot);
    }

    /// <summary>
    /// Observes an external root after settlement observers for its complete causal tree finish.
    /// </summary>
    /// <remarks>
    /// This final host boundary runs once for an external root while it still owns dispatcher
    /// serialization. It exists for presentation that must preserve exact-root queues but cannot
    /// drain a descendant before callbacks on any ancestor or sibling have settled.
    /// </remarks>
    public interface ICausalTreeSettlementObserver
    {
        /// <summary>Settles host work for one complete external-root causal tree.</summary>
        /// <param name="rootId">The external root at the top of the completed causal tree.</param>
        /// <param name="snapshot">The latest snapshot after every causal root callback.</param>
        /// <returns>A task-like value that completes after final host settlement.</returns>
        ValueTask OnCausalTreeSettled(OpId rootId, RulesSnapshot snapshot);
    }

    /// <summary>
    /// Resolves typed rules operations while preserving frame provenance, committed facts, and diagnostics.
    /// </summary>
    /// <remarks>
    /// Root resolutions are serialized: a second external root waits until the active root and its
    /// post-commit callbacks finish.
    /// Handlers may dispatch nested children through <see cref="OpHandlerContext"/>, but each active frame
    /// may own only one child at a time and must await it. Dynamic Fact observers finish after each
    /// reduction commit, before its parent handler continues. Binding-scoped Fact listeners finish
    /// after the root; listener-dispatched work runs as serialized causal roots.
    /// Trace and diagnostic history accumulate for the lifetime of the dispatcher.
    /// </remarks>
    public sealed partial class RuleDispatcher
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts = Array.AsReadOnly(
            Array.Empty<RuleFact>()
        );
        private static readonly IReadOnlyList<BoundMiddlewareRegistration> NoMiddleware =
            Array.AsReadOnly(Array.Empty<BoundMiddlewareRegistration>());
        private readonly object gate = new object();
        private readonly SemaphoreSlim rootSerial = new SemaphoreSlim(1, 1);
        private readonly List<IRootSettlementObserver> rootSettlementObservers =
            new List<IRootSettlementObserver>();
        private readonly List<ICausalTreeSettlementObserver> causalTreeSettlementObservers =
            new List<ICausalTreeSettlementObserver>();

        // Zero is the idle async-flow sentinel. A unique nonzero lease distinguishes callbacks still
        // running inside this dispatcher's current resolution from callers that should wait on the gate.
        private readonly AsyncLocal<long> activeResolutionFlow = new AsyncLocal<long>();
        private readonly AsyncLocal<long> activeRootCallbackFlow = new AsyncLocal<long>();
        private readonly AsyncLocal<OpId?> activeRootCallbackOwner = new AsyncLocal<OpId?>();
        private readonly AsyncLocal<CallbackWorkCoordinator> activeRootCallbackWork =
            new AsyncLocal<CallbackWorkCoordinator>();
        private long activeResolutionFlowLease;
        private long nextResolutionFlowLease;
        private readonly IRulesStore store;
        private readonly IOpIdProvider ids;
        private readonly IRollService rollService;
        private readonly IReadOnlyDictionary<Type, IRegistration> registrations;
        private readonly RuleRegistry ruleRegistry;
        private readonly ActionRuntime actionRuntime;
        private readonly IFactObserverExceptionReporter factObserverExceptionReporter;
        private RootResolution activeRoot = RootResolution.Idle;

        internal RuleDispatcher(
            IRulesStore store,
            IOpIdProvider ids,
            IRollService rollService,
            IDictionary<Type, IRegistration> registrations,
            RuleRegistry ruleRegistry,
            ActionRuntime actionRuntime,
            IFactObserverExceptionReporter factObserverExceptionReporter
        )
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            this.rollService = rollService ?? throw new ArgumentNullException(nameof(rollService));
            this.registrations = new ReadOnlyDictionary<Type, IRegistration>(
                new Dictionary<Type, IRegistration>(registrations)
            );
            this.ruleRegistry =
                ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
            this.actionRuntime =
                actionRuntime ?? throw new ArgumentNullException(nameof(actionRuntime));
            this.factObserverExceptionReporter =
                factObserverExceptionReporter
                ?? throw new ArgumentNullException(nameof(factObserverExceptionReporter));
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

        /// <summary>Registers a host observer for exact-root settlement.</summary>
        /// <param name="observer">The observer appended to deterministic registration order.</param>
        /// <returns>
        /// An idempotent registration token. Disposing it removes this observer while preserving
        /// the same idle-dispatcher requirement as explicit removal.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Registration was attempted while a root owned serialization, or the observer is already
        /// registered.
        /// </exception>
        public IDisposable RegisterRootSettlementObserver(IRootSettlementObserver observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            lock (gate)
            {
                if (!activeRoot.IsIdle)
                    throw new InvalidOperationException(
                        "Root settlement observers can change only while the dispatcher is idle."
                    );
                if (rootSettlementObservers.Contains(observer))
                    throw new InvalidOperationException(
                        "The root settlement observer is already registered."
                    );
                rootSettlementObservers.Add(observer);
            }

            return new DispatcherObserverRegistration(() =>
                UnregisterRootSettlementObserver(observer)
            );
        }

        /// <summary>Removes a host observer from later exact-root settlement.</summary>
        /// <param name="observer">The previously registered observer.</param>
        /// <returns>Whether the observer was registered and removed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Removal was attempted while a root owned serialization.
        /// </exception>
        public bool UnregisterRootSettlementObserver(IRootSettlementObserver observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            lock (gate)
            {
                if (!activeRoot.IsIdle)
                    throw new InvalidOperationException(
                        "Root settlement observers can change only while the dispatcher is idle."
                    );
                return rootSettlementObservers.Remove(observer);
            }
        }

        /// <summary>Registers the host's single final causal-tree settlement observer.</summary>
        /// <param name="observer">The terminal observer for every external root.</param>
        /// <returns>
        /// An idempotent registration token. Disposing it removes this observer while preserving
        /// the same idle-dispatcher requirement as explicit removal.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Registration was attempted while a root owned serialization, or a terminal observer is
        /// already registered.
        /// </exception>
        public IDisposable RegisterCausalTreeSettlementObserver(
            ICausalTreeSettlementObserver observer
        )
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            lock (gate)
            {
                if (!activeRoot.IsIdle)
                    throw new InvalidOperationException(
                        "Causal-tree settlement observers can change only while the dispatcher is idle."
                    );
                if (causalTreeSettlementObservers.Count != 0)
                    throw new InvalidOperationException(
                        "A causal-tree settlement observer is already registered."
                    );
                causalTreeSettlementObservers.Add(observer);
            }

            return new DispatcherObserverRegistration(() =>
                UnregisterCausalTreeSettlementObserver(observer)
            );
        }

        /// <summary>Removes a host observer from final external-root causal-tree settlement.</summary>
        /// <param name="observer">The previously registered observer.</param>
        /// <returns>Whether the observer was registered and removed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Removal was attempted while a root owned serialization.
        /// </exception>
        public bool UnregisterCausalTreeSettlementObserver(ICausalTreeSettlementObserver observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            lock (gate)
            {
                if (!activeRoot.IsIdle)
                    throw new InvalidOperationException(
                        "Causal-tree settlement observers can change only while the dispatcher is idle."
                    );
                return causalTreeSettlementObservers.Remove(observer);
            }
        }

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
        /// The operation is nested-only, no compatible resolver is registered, the current resolution
        /// calls this public root API outside a registered root callback, or a handler violates
        /// nested-dispatch ownership.
        /// </exception>
        /// <remarks>
        /// Resolver, middleware, observer, and post-commit listener exceptions propagate to the
        /// caller. State already committed by a reducer is not rolled back. If resolution fails
        /// after a commit, listeners receive the durable Facts before the resolution exception is
        /// rethrown. If that notification also fails, an <see cref="AggregateException"/> reports
        /// the resolution exception first and the notification exception second. The dispatcher
        /// then releases root ownership so a later independent root may be dispatched. When a
        /// callback and its unconsumed work both fail, their aggregate likewise retains the callback
        /// exception first. Other external roots remain queued until this entire resolution releases
        /// ownership.
        /// </remarks>
        public ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op) =>
            DispatchExternal(op, NoRootResolutionObserver<TResult>.Instance);

        /// <summary>
        /// Dispatches an external root with accepted root-owned work inside its serialization.
        /// </summary>
        /// <typeparam name="TResult">The successful result type declared by the operation.</typeparam>
        /// <param name="op">The externally allowed operation to resolve.</param>
        /// <param name="observer">
        /// The observer invoked after resolution and before Fact listeners while serialization is
        /// still owned.
        /// </param>
        /// <returns>The settled root result after root-owned work and all listeners.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="op"/> or <paramref name="observer"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The operation is nested-only, no compatible resolver is registered, the current resolution
        /// calls this public root API outside a registered root callback, or a handler violates
        /// nested-dispatch ownership.
        /// </exception>
        public ValueTask<OpResult<TResult>> Dispatch<TResult>(
            IRuleOp<TResult> op,
            IRootResolutionObserver<TResult> observer
        )
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            return DispatchExternal(op, observer);
        }

        private ValueTask<OpResult<TResult>> DispatchExternal<TResult>(
            IRuleOp<TResult> op,
            IRootResolutionObserver<TResult> observer
        )
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));
            long callerFlowLease = activeResolutionFlow.Value;
            OpId? callbackOwner = null;
            CallbackWorkCoordinator callbackWork = null;
            lock (gate)
            {
                if (callerFlowLease != 0 && callerFlowLease == activeResolutionFlowLease)
                {
                    if (activeRootCallbackFlow.Value != activeResolutionFlowLease)
                        throw new InvalidOperationException(
                            "An active resolution cannot call the public root Dispatch API. "
                                + "Use its callback context for nested work."
                        );
                    callbackOwner =
                        activeRootCallbackOwner.Value
                        ?? throw new InvalidOperationException(
                            "A root callback has no exact owning root."
                        );
                    callbackWork =
                        activeRootCallbackWork.Value
                        ?? throw new InvalidOperationException(
                            "A root callback has no active work coordinator."
                        );
                }
            }

            if (callbackOwner.HasValue)
                return callbackWork.StartDispatch(
                    () =>
                        DispatchTriggeredRoot(
                            op,
                            callbackOwner.Value,
                            callbackOwner.Value,
                            observer,
                            "A root callback must await each dispatch before starting another."
                        ),
                    "A root callback cannot dispatch after it returns.",
                    "A root callback must await each dispatch before starting another.",
                    "A root callback cannot dispatch while callback continuation work is active."
                );

            return DispatchIndependentRoot(op, observer);
        }

        private async ValueTask<OpResult<TResult>> DispatchIndependentRoot<TResult>(
            IRuleOp<TResult> op,
            IRootResolutionObserver<TResult> observer
        )
        {
            await rootSerial.WaitAsync();
            // Keep the idle sentinel until construction succeeds so the ownership gate is still
            // released if per-resolution allocation fails.
            RootResolution resolution = RootResolution.Idle;
            try
            {
                resolution = new RootResolution();
                lock (gate)
                {
                    if (!activeRoot.IsIdle)
                    {
                        throw new InvalidOperationException(
                            "Serialized root ownership was not released before the next root began."
                        );
                    }

                    activeRoot = resolution;
                    activeResolutionFlowLease = NextResolutionFlowLease();
                    activeResolutionFlow.Value = activeResolutionFlowLease;
                }

                IRegistration registration = RequireRegistration(op.GetType(), typeof(TResult));
                if (registration.Policy != InvocationPolicy.ExternalAllowed)
                {
                    throw new InvalidOperationException(
                        $"{op.GetType().Name} is nested-only and cannot be externally dispatched."
                    );
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
                    null,
                    null,
                    observer
                );
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(activeRoot, resolution))
                        activeRoot = RootResolution.Idle;
                    activeResolutionFlowLease = 0;
                }
                activeResolutionFlow.Value = 0;
                rootSerial.Release();
            }
        }

        private long NextResolutionFlowLease()
        {
            unchecked
            {
                nextResolutionFlowLease++;
                if (nextResolutionFlowLease == 0)
                    nextResolutionFlowLease++;
                return nextResolutionFlowLease;
            }
        }

        internal async ValueTask<OpResult<TResult>> DispatchNested<TResult>(
            IRuleOp<TResult> op,
            OpId parentId
        )
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            RootResolution resolution;
            ChildReservation reservation;
            lock (gate)
            {
                if (activeRoot.IsIdle)
                    throw new InvalidOperationException(
                        "Nested dispatch requires an active root resolution."
                    );

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
                    parentId
                );
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
            OpId causeId
        )
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            return await DispatchTriggeredRoot(
                op,
                committedRootId,
                causeId,
                NoRootResolutionObserver<TResult>.Instance,
                "Fact-listener dispatch requires its completed root to retain resolution ownership."
            );
        }

        private async ValueTask<OpResult<TResult>> DispatchTriggeredRoot<TResult>(
            IRuleOp<TResult> op,
            OpId owningRootId,
            OpId causeId,
            IRootResolutionObserver<TResult> observer,
            string ownershipFailure
        )
        {
            IRegistration registration = RequireRegistration(op.GetType(), typeof(TResult));
            if (registration.Policy != InvocationPolicy.ExternalAllowed)
                throw new InvalidOperationException(
                    $"{op.GetType().Name} is nested-only and cannot begin a causal root."
                );

            RootResolution owner;
            RootResolution triggered = new RootResolution();
            OpId rootId;
            lock (gate)
            {
                if (activeRoot.IsIdle || activeRoot.RootId != owningRootId)
                    throw new InvalidOperationException(ownershipFailure);
                owner = activeRoot;
                rootId = ids.Next();
                triggered.Initialize(rootId);
                activeRoot = triggered;
            }

            try
            {
                return await DispatchRoot(
                    op,
                    registration,
                    triggered,
                    rootId,
                    causeId,
                    owningRootId,
                    observer
                );
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

        internal async ValueTask InvokeRootCallback(OpId owner, Func<ValueTask> callback)
        {
            long previousFlow = activeRootCallbackFlow.Value;
            OpId? previousOwner = activeRootCallbackOwner.Value;
            CallbackWorkCoordinator previousWork = activeRootCallbackWork.Value;
            CallbackWorkCoordinator work = new CallbackWorkCoordinator();
            activeRootCallbackFlow.Value = activeResolutionFlowLease;
            activeRootCallbackOwner.Value = owner;
            activeRootCallbackWork.Value = work;
            try
            {
                try
                {
                    await callback();
                }
                catch (Exception callbackException)
                {
                    await CallbackFailure.AwaitCleanupPreservingPrimary(
                        callbackException,
                        work.CompleteInvocation("A root callback completed more than once.")
                    );
                    throw;
                }

                if (
                    await work.CompleteInvocation("A root callback completed more than once.")
                    == CallbackWorkCompletion.UnconsumedDispatch
                )
                {
                    throw new InvalidOperationException(
                        $"Root callback for {owner.Value} returned before awaiting its causally linked dispatch."
                    );
                }

                lock (gate)
                {
                    if (activeRoot.IsIdle || activeRoot.RootId != owner)
                        throw new InvalidOperationException(
                            $"Root callback for {owner.Value} crossed resolution root ownership."
                        );
                }
            }
            finally
            {
                activeRootCallbackWork.Value = previousWork;
                activeRootCallbackOwner.Value = previousOwner;
                activeRootCallbackFlow.Value = previousFlow;
            }
        }

        internal async ValueTask NotifyRootSettled(OpId rootId, OpId? causalParentRootId)
        {
            IRootSettlementObserver[] observers;
            ICausalTreeSettlementObserver[] treeObservers;
            lock (gate)
            {
                observers = rootSettlementObservers.ToArray();
                treeObservers = causalParentRootId.HasValue
                    ? Array.Empty<ICausalTreeSettlementObserver>()
                    : causalTreeSettlementObservers.ToArray();
            }

            List<Exception> failures = null;
            foreach (IRootSettlementObserver observer in observers)
            {
                try
                {
                    await InvokeRootCallback(
                        rootId,
                        () => observer.OnRootSettled(rootId, causalParentRootId, Snapshot)
                    );
                }
                catch (Exception exception)
                {
                    if (failures == null)
                        failures = new List<Exception>();
                    failures.Add(exception);
                }
            }

            foreach (ICausalTreeSettlementObserver observer in treeObservers)
            {
                try
                {
                    await InvokeRootCallback(
                        rootId,
                        () => observer.OnCausalTreeSettled(rootId, Snapshot)
                    );
                }
                catch (Exception exception)
                {
                    if (failures == null)
                        failures = new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures == null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("Multiple root settlement observers failed.", failures);
        }

        private sealed class NoRootResolutionObserver<TResult> : IRootResolutionObserver<TResult>
        {
            internal static NoRootResolutionObserver<TResult> Instance { get; } =
                new NoRootResolutionObserver<TResult>();

            /// <inheritdoc/>
            public ValueTask OnRootResolved(
                OpId rootId,
                OpResult<TResult> result,
                RulesSnapshot snapshot
            ) => default;
        }
    }
}
