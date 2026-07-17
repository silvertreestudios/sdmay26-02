using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies the semantic stage in which a rule extension participates.
    /// </summary>
    /// <remarks>
    /// The fixed stages provide deterministic ordering without exposing arbitrary numeric
    /// priorities. Choose the stage that describes the rule's purpose. If two rules need a more
    /// specific ordering relationship, introduce a distinct lifecycle operation instead of using
    /// a stage as an undocumented priority.
    /// </remarks>
    public enum RuleLifecyclePhase
    {
        /// <summary>
        /// Rules that can prevent the operation or committed response from proceeding normally.
        /// </summary>
        Prevention,

        /// <summary>
        /// Rules that replace or transform the value produced by ordinary resolution.
        /// </summary>
        Transformation,

        /// <summary>
        /// Rules that perform a rules response, such as offering or resolving a reaction.
        /// </summary>
        Reaction,

        /// <summary>
        /// Rules that observe the settled outcome after rules-changing stages have run.
        /// </summary>
        Observation
    }

    /// <summary>
    /// Continues an operation's middleware chain and returns its typed structural result.
    /// </summary>
    /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
    /// <returns>The result produced by the next middleware or the operation's resolver.</returns>
    /// <remarks>
    /// Middleware may invoke this delegate at most once and must await it before returning. It may
    /// omit the call to short-circuit or replace the remaining work.
    /// </remarks>
    public delegate ValueTask<OpResult<TResult>> OpNext<TResult>();

    /// <summary>
    /// Wraps resolution of one concrete operation type for each matching active rule binding.
    /// </summary>
    /// <typeparam name="TOp">The concrete operation type being wrapped.</typeparam>
    /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
    /// <remarks>
    /// Middleware receives read-only state and may change rules state only by dispatching nested
    /// operations through its context. It runs before the enclosing operation has settled, so it
    /// may continue, replace, or short-circuit that work.
    /// </remarks>
    public interface IOpMiddleware<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        /// <summary>
        /// Wraps the next stage of resolution for one active binding.
        /// </summary>
        /// <param name="binding">The active rule instance authorizing this middleware invocation.</param>
        /// <param name="frame">The immutable frame for the operation being resolved.</param>
        /// <param name="context">Read-only rules services and binding-scoped nested dispatch.</param>
        /// <param name="next">The remaining middleware chain and final resolver.</param>
        /// <returns>The structural operation result to expose to the enclosing stage.</returns>
        ValueTask<OpResult<TResult>> Invoke(
            ActiveRuleBinding binding,
            OpFrame<TOp> frame,
            OpContext context,
            OpNext<TResult> next);
    }

    /// <summary>
    /// Reacts to one committed Fact after the Fact's complete root resolution has finished.
    /// </summary>
    /// <typeparam name="TFact">The committed Fact type observed by the listener.</typeparam>
    /// <remarks>
    /// A listener cannot change or cancel the state transition described by the Fact. It may
    /// dispatch a new, causally linked root operation through <see cref="FactContext"/>.
    /// </remarks>
    public interface IFactListener<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Handles one matching committed Fact for one active binding.
        /// </summary>
        /// <param name="binding">The active rule instance authorizing this notification.</param>
        /// <param name="fact">The already committed Fact.</param>
        /// <param name="context">Post-commit state, trace data, and causal dispatch.</param>
        /// <returns>A task-like value that completes when the listener and its dispatched work finish.</returns>
        ValueTask OnFactCommitted(
            ActiveRuleBinding binding,
            TFact fact,
            FactContext context);
    }

    /// <summary>
    /// Reacts once to all matching Facts committed by one completed root resolution.
    /// </summary>
    /// <typeparam name="TFact">The committed Fact type grouped for the listener.</typeparam>
    public interface IFactBatchListener<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Handles the matching Facts from one committed root for one active binding.
        /// </summary>
        /// <param name="binding">The active rule instance authorizing this notification.</param>
        /// <param name="batch">A non-empty, root-scoped collection in commit order.</param>
        /// <param name="context">Post-commit state, trace data, and causal dispatch.</param>
        /// <returns>A task-like value that completes when the listener and its dispatched work finish.</returns>
        ValueTask OnFactsCommitted(
            ActiveRuleBinding binding,
            CommittedFactBatch<TFact> batch,
            FactContext context);
    }

    /// <summary>
    /// Provides all matching Facts of one type committed beneath one root resolution.
    /// </summary>
    /// <typeparam name="TFact">The common committed Fact type in the batch.</typeparam>
    public sealed class CommittedFactBatch<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Gets the completed root operation that committed the Facts.
        /// </summary>
        public OpId RootId { get; }

        /// <summary>
        /// Gets the matching Facts in their deterministic commit order.
        /// </summary>
        public IReadOnlyList<TFact> Facts { get; }

        internal CommittedFactBatch(OpId rootId, IReadOnlyList<TFact> facts)
        {
            if (rootId.IsEmpty)
                throw new ArgumentException("A committed Fact batch requires a root operation ID.", nameof(rootId));
            if (facts == null || facts.Count == 0)
                throw new ArgumentException("A committed Fact batch cannot be empty.", nameof(facts));
            if (facts.Any(fact => fact == null || !fact.IsStamped || fact.RootOpId != rootId))
                throw new InvalidOperationException("Every Fact in a committed batch must belong to its root.");

            RootId = rootId;
            Facts = facts;
        }
    }

    /// <summary>
    /// Exposes post-commit state and causally linked dispatch to one Fact-listener invocation.
    /// </summary>
    /// <remarks>
    /// The context is valid only while its listener is executing. A listener may have at most one
    /// dispatched root in flight and must await it before returning or dispatching another. Any
    /// dispatched operation begins a new root resolution; it cannot retroactively alter the Facts
    /// that caused the notification.
    /// </remarks>
    public sealed class FactContext
    {
        private readonly object gate = new object();
        private readonly RuleDispatcher dispatcher;
        private readonly OpId causeId;
        private bool isActive = true;
        private IFactDispatch activeDispatch;

        internal FactContext(
            RuleDispatcher dispatcher,
            ActiveRuleBinding binding,
            OpId committedRootId,
            OpId causeId)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (committedRootId.IsEmpty || causeId.IsEmpty)
                throw new ArgumentException("Fact contexts require committed root and cause IDs.");
            CommittedRootId = committedRootId;
            this.causeId = causeId;
        }

        /// <summary>
        /// Gets the active binding authorizing the current listener.
        /// </summary>
        public ActiveRuleBinding Binding { get; }

        /// <summary>
        /// Gets the stable rule source associated with <see cref="Binding"/>.
        /// </summary>
        public RuleSource Source => Binding.Source;

        /// <summary>
        /// Gets the root operation whose committed Facts caused this notification.
        /// </summary>
        public OpId CommittedRootId { get; }

        /// <summary>
        /// Gets the latest committed rules snapshot, including the state described by the Fact.
        /// </summary>
        public RulesSnapshot Snapshot => dispatcher.Snapshot;

        /// <summary>
        /// Gets the lifetime trace containing the committed root and listener-dispatched work.
        /// </summary>
        public ResolutionTrace Trace => dispatcher.Trace;

        /// <summary>
        /// Dispatches an externally allowed operation as a new root caused by this notification.
        /// </summary>
        /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
        /// <param name="op">The operation that reacts to the committed Fact or batch.</param>
        /// <returns>The structural result of the new causally linked root.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="op"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The listener has returned, another listener dispatch is in flight, or the operation is
        /// not registered for external invocation.
        /// </exception>
        public ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            FactDispatch<TResult> dispatch;
            lock (gate)
            {
                if (!isActive)
                    throw new InvalidOperationException("A Fact context cannot dispatch after its listener returns.");
                if (activeDispatch != null)
                {
                    throw new InvalidOperationException(
                        "A Fact listener cannot overlap dispatched roots. Await the active dispatch first.");
                }

                dispatch = new FactDispatch<TResult>(
                    this,
                    dispatcher,
                    op,
                    CommittedRootId,
                    causeId);
                activeDispatch = dispatch;
            }

            dispatch.Start();
            return dispatch.AsValueTask();
        }

        internal async ValueTask<bool> CompleteInvocation()
        {
            IFactDispatch pending = null;
            lock (gate)
            {
                if (!isActive)
                    throw new InvalidOperationException("A Fact context completed more than once.");
                isActive = false;
                pending = activeDispatch;
            }

            if (pending == null)
                return false;

            await pending.Completion;
            pending.ThrowIfFailed();
            return !pending.WasConsumed;
        }

        private void ReleaseDispatch(IFactDispatch dispatch)
        {
            lock (gate)
            {
                if (ReferenceEquals(activeDispatch, dispatch))
                    activeDispatch = null;
            }
        }

        private interface IFactDispatch
        {
            Task Completion { get; }

            bool WasConsumed { get; }

            void ThrowIfFailed();
        }

        private sealed class FactDispatch<TResult> :
            IFactDispatch,
            IValueTaskSource<OpResult<TResult>>
        {
            private readonly FactContext owner;
            private readonly RuleDispatcher dispatcher;
            private readonly IRuleOp<TResult> op;
            private readonly OpId committedRootId;
            private readonly OpId causeId;
            private readonly TaskCompletionSource<bool> completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private ManualResetValueTaskSourceCore<OpResult<TResult>> source;
            private ExceptionDispatchInfo failure;
            private int wasConsumed;
            private int wasStarted;

            public FactDispatch(
                FactContext owner,
                RuleDispatcher dispatcher,
                IRuleOp<TResult> op,
                OpId committedRootId,
                OpId causeId)
            {
                this.owner = owner;
                this.dispatcher = dispatcher;
                this.op = op;
                this.committedRootId = committedRootId;
                this.causeId = causeId;
                source.RunContinuationsAsynchronously = true;
            }

            public Task Completion => completion.Task;

            public bool WasConsumed => Volatile.Read(ref wasConsumed) != 0;

            public ValueTask<OpResult<TResult>> AsValueTask() =>
                new ValueTask<OpResult<TResult>>(this, source.Version);

            public void Start()
            {
                if (Interlocked.Exchange(ref wasStarted, 1) != 0)
                    throw new InvalidOperationException("A Fact dispatch cannot start more than once.");

                _ = Run();
            }

            public void ThrowIfFailed() => failure?.Throw();

            public OpResult<TResult> GetResult(short token)
            {
                if (Interlocked.Exchange(ref wasConsumed, 1) != 0)
                    throw new InvalidOperationException("A Fact dispatch result may be consumed only once.");

                try
                {
                    return source.GetResult(token);
                }
                finally
                {
                    owner.ReleaseDispatch(this);
                }
            }

            public ValueTaskSourceStatus GetStatus(short token) =>
                source.GetStatus(token);

            public void OnCompleted(
                Action<object> continuation,
                object state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) =>
                source.OnCompleted(continuation, state, token, flags);

            private async Task Run()
            {
                try
                {
                    OpResult<TResult> result =
                        await dispatcher.DispatchFromFact(op, committedRootId, causeId);
                    source.SetResult(result);
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                    source.SetException(exception);
                }
                finally
                {
                    completion.TrySetResult(true);
                }
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
            long registrationOrder)
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
            Func<ValueTask<object>> next);
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
            long registrationOrder)
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
            ActiveRuleBinding binding,
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            FactContext context);
    }

    /// <summary>
    /// Collects the static extensions contributed by one feat, condition, effect, item, or rule module.
    /// </summary>
    /// <remarks>
    /// Definitions are immutable after construction and contain no per-binding mutable state.
    /// Runtime participation is controlled exclusively by matching <see cref="ActiveRuleBinding"/>
    /// values in the current <see cref="RulesSnapshot"/>.
    /// </remarks>
    public sealed class RuleDefinition
    {
        internal RuleDefinition(
            RuleDefinitionId id,
            IReadOnlyList<MiddlewareRegistration> middleware,
            IReadOnlyList<FactListenerRegistration> factListeners)
        {
            Id = id;
            Middleware = middleware;
            FactListeners = factListeners;
        }

        /// <summary>
        /// Gets the stable ID referenced by active bindings.
        /// </summary>
        public RuleDefinitionId Id { get; }

        /// <summary>
        /// Gets the definition's immutable middleware registrations.
        /// </summary>
        public IReadOnlyList<MiddlewareRegistration> Middleware { get; }

        /// <summary>
        /// Gets the definition's immutable committed-Fact listener registrations.
        /// </summary>
        public IReadOnlyList<FactListenerRegistration> FactListeners { get; }
    }

    /// <summary>
    /// Builds the static extensions for one <see cref="RuleDefinition"/>.
    /// </summary>
    public sealed class RuleDefinitionBuilder
    {
        private readonly List<MiddlewareRegistration> middleware =
            new List<MiddlewareRegistration>();
        private readonly List<FactListenerRegistration> factListeners =
            new List<FactListenerRegistration>();
        private long registrationOrder;

        internal RuleDefinitionBuilder(RuleDefinitionId id)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(id));
            Id = id;
        }

        /// <summary>
        /// Gets the stable ID assigned to the definition under construction.
        /// </summary>
        public RuleDefinitionId Id { get; }

        /// <summary>
        /// Adds one typed middleware extension to this definition.
        /// </summary>
        /// <typeparam name="TOp">The concrete operation type wrapped by the middleware.</typeparam>
        /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
        /// <param name="phase">The semantic stage used to order active middleware.</param>
        /// <param name="value">The stateless middleware implementation.</param>
        /// <returns>This definition builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// This definition already registers the operation in the same lifecycle phase.
        /// </exception>
        public RuleDefinitionBuilder Middleware<TOp, TResult>(
            RuleLifecyclePhase phase,
            IOpMiddleware<TOp, TResult> value)
            where TOp : IRuleOp<TResult>
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            RequirePhase(phase);
            if (middleware.Any(item => item.OperationType == typeof(TOp) && item.Phase == phase))
            {
                throw new InvalidOperationException(
                    $"Definition {Id.Value} already registers {typeof(TOp).Name} middleware in {phase}.");
            }

            middleware.Add(new TypedMiddlewareRegistration<TOp, TResult>(
                phase, registrationOrder++, value));
            return this;
        }

        /// <summary>
        /// Adds a listener that receives matching committed Facts one at a time.
        /// </summary>
        /// <typeparam name="TFact">The committed Fact type to observe.</typeparam>
        /// <param name="phase">The semantic stage used to order active listeners.</param>
        /// <param name="value">The stateless listener implementation.</param>
        /// <returns>This definition builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// This definition already registers the same single-Fact listener type and phase.
        /// </exception>
        public RuleDefinitionBuilder FactListener<TFact>(
            RuleLifecyclePhase phase,
            IFactListener<TFact> value)
            where TFact : RuleFact
        {
            AddFactListener(
                typeof(TFact), phase, false, value,
                order => new TypedFactListenerRegistration<TFact>(phase, order, value));
            return this;
        }

        /// <summary>
        /// Adds a listener that receives all matching Facts together once per committed root.
        /// </summary>
        /// <typeparam name="TFact">The committed Fact type to group and observe.</typeparam>
        /// <param name="phase">The semantic stage used to order active listeners.</param>
        /// <param name="value">The stateless batch-listener implementation.</param>
        /// <returns>This definition builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// This definition already registers the same batch-listener type and phase.
        /// </exception>
        public RuleDefinitionBuilder FactBatchListener<TFact>(
            RuleLifecyclePhase phase,
            IFactBatchListener<TFact> value)
            where TFact : RuleFact
        {
            AddFactListener(
                typeof(TFact), phase, true, value,
                order => new TypedFactBatchListenerRegistration<TFact>(phase, order, value));
            return this;
        }

        internal RuleDefinition Build()
        {
            MiddlewareRegistration[] middlewareCopy = middleware.ToArray();
            FactListenerRegistration[] listenerCopy = factListeners.ToArray();
            return new RuleDefinition(
                Id,
                Array.AsReadOnly(middlewareCopy),
                Array.AsReadOnly(listenerCopy));
        }

        private void AddFactListener(
            Type factType,
            RuleLifecyclePhase phase,
            bool isBatch,
            object value,
            Func<long, FactListenerRegistration> create)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            RequirePhase(phase);
            if (factListeners.Any(item =>
                item.FactType == factType && item.Phase == phase && item.IsBatch == isBatch))
            {
                throw new InvalidOperationException(
                    $"Definition {Id.Value} already registers this {factType.Name} listener in {phase}.");
            }
            factListeners.Add(create(registrationOrder++));
        }

        private static void RequirePhase(RuleLifecyclePhase phase)
        {
            if (!Enum.IsDefined(typeof(RuleLifecyclePhase), phase))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phase),
                    phase,
                    "Rule extensions must use a defined semantic lifecycle phase.");
            }
        }
    }

    /// <summary>
    /// Builds an immutable registry of static rule definitions.
    /// </summary>
    public sealed class RuleRegistryBuilder
    {
        private readonly Dictionary<RuleDefinitionId, RuleDefinitionBuilder> definitions =
            new Dictionary<RuleDefinitionId, RuleDefinitionBuilder>();

        /// <summary>
        /// Starts one static rule definition.
        /// </summary>
        /// <param name="id">The stable definition ID stored by active bindings.</param>
        /// <returns>A builder used to register the definition's typed extensions.</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="id"/> was already defined.</exception>
        public RuleDefinitionBuilder Define(RuleDefinitionId id)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(id));
            if (definitions.ContainsKey(id))
                throw new InvalidOperationException($"Rule definition {id.Value} is already registered.");

            RuleDefinitionBuilder definition = new RuleDefinitionBuilder(id);
            definitions.Add(id, definition);
            return definition;
        }

        /// <summary>
        /// Builds an immutable registry snapshot from all definitions currently registered.
        /// </summary>
        /// <returns>A registry that is unaffected by later builder changes.</returns>
        public RuleRegistry Build() => new RuleRegistry(definitions.Values.Select(value => value.Build()));
    }

    /// <summary>
    /// Stores immutable rule definitions and selects their extensions from active snapshot bindings.
    /// </summary>
    public sealed class RuleRegistry
    {
        private readonly IReadOnlyDictionary<RuleDefinitionId, RuleDefinition> byId;

        internal static RuleRegistry Empty { get; } = new RuleRegistry(Array.Empty<RuleDefinition>());

        internal RuleRegistry(IEnumerable<RuleDefinition> definitions)
        {
            Dictionary<RuleDefinitionId, RuleDefinition> values = definitions.ToDictionary(
                definition => definition.Id,
                definition => definition);
            byId = new ReadOnlyDictionary<RuleDefinitionId, RuleDefinition>(values);
            Definitions = Array.AsReadOnly(values.Values.OrderBy(value => value.Id.Value).ToArray());
        }

        /// <summary>
        /// Gets all static definitions ordered by stable definition ID.
        /// </summary>
        public IReadOnlyList<RuleDefinition> Definitions { get; }

        internal void ValidateResolvers(IReadOnlyDictionary<Type, IRegistration> resolvers)
        {
            foreach (RuleDefinition definition in Definitions)
            {
                foreach (MiddlewareRegistration middlewareRegistration in definition.Middleware)
                {
                    if (!resolvers.TryGetValue(
                        middlewareRegistration.OperationType,
                        out IRegistration resolver))
                    {
                        throw new InvalidOperationException(
                            $"Middleware for {middlewareRegistration.OperationType.Name} has no registered resolver.");
                    }
                    if (resolver.ResultType != middlewareRegistration.ResultType)
                    {
                        throw new InvalidOperationException(
                            $"Middleware for {middlewareRegistration.OperationType.Name} expects " +
                            $"{middlewareRegistration.ResultType.Name}, but its resolver returns " +
                            $"{resolver.ResultType.Name}.");
                    }
                }
            }
        }

        internal IReadOnlyList<BoundMiddlewareRegistration> SelectMiddleware(
            Type operationType,
            Type resultType,
            RulesSnapshot snapshot)
        {
            List<BoundMiddlewareRegistration> selected = new List<BoundMiddlewareRegistration>();
            foreach (KeyValuePair<BindingId, ActiveRuleBinding> pair in snapshot.RuleBindings)
            {
                ActiveRuleBinding binding = pair.Value;
                if (!binding.IsEnabled)
                    continue;
                RuleDefinition definition = RequireDefinition(binding.DefinitionId);
                foreach (MiddlewareRegistration registration in definition.Middleware)
                {
                    if (registration.OperationType == operationType && registration.ResultType == resultType)
                        selected.Add(new BoundMiddlewareRegistration(binding, registration));
                }
            }

            selected.Sort(BoundMiddlewareRegistration.Compare);
            return selected;
        }

        internal IReadOnlyList<FactListenerDelivery> SelectFactListeners(
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            RulesSnapshot snapshot)
        {
            List<FactListenerDelivery> selected = new List<FactListenerDelivery>();
            foreach (KeyValuePair<BindingId, ActiveRuleBinding> pair in snapshot.RuleBindings)
            {
                ActiveRuleBinding binding = pair.Value;
                if (!binding.IsEnabled)
                    continue;
                RuleDefinition definition = RequireDefinition(binding.DefinitionId);
                foreach (FactListenerRegistration registration in definition.FactListeners)
                {
                    RuleFact[] matching = facts.Where(registration.Matches).ToArray();
                    if (matching.Length > 0)
                    {
                        selected.Add(new FactListenerDelivery(
                            binding,
                            registration,
                            rootId,
                            Array.AsReadOnly(matching)));
                    }
                }
            }

            selected.Sort(FactListenerDelivery.Compare);
            return selected;
        }

        internal bool IsActive(RulesSnapshot snapshot, ActiveRuleBinding binding) =>
            snapshot.RuleBindings.TryGet(binding.Id, out ActiveRuleBinding current) &&
            current.IsEnabled && current.Equals(binding);

        private RuleDefinition RequireDefinition(RuleDefinitionId id)
        {
            if (!byId.TryGetValue(id, out RuleDefinition definition))
                throw new InvalidOperationException($"Active binding references unknown rule definition {id.Value}.");
            return definition;
        }
    }

    internal sealed class BoundMiddlewareRegistration
    {
        public ActiveRuleBinding Binding { get; }
        public MiddlewareRegistration Registration { get; }

        public BoundMiddlewareRegistration(
            ActiveRuleBinding binding,
            MiddlewareRegistration registration)
        {
            Binding = binding;
            Registration = registration;
        }

        public static int Compare(BoundMiddlewareRegistration left, BoundMiddlewareRegistration right)
        {
            int phase = left.Registration.Phase.CompareTo(right.Registration.Phase);
            if (phase != 0)
                return phase;
            int creation = left.Binding.CreationOrder.CompareTo(right.Binding.CreationOrder);
            if (creation != 0)
                return creation;
            int id = string.Compare(left.Binding.Id.Value, right.Binding.Id.Value, StringComparison.Ordinal);
            if (id != 0)
                return id;
            return left.Registration.RegistrationOrder.CompareTo(right.Registration.RegistrationOrder);
        }
    }

    internal sealed class FactListenerDelivery
    {
        public ActiveRuleBinding Binding { get; }
        public FactListenerRegistration Registration { get; }
        public OpId RootId { get; }
        public IReadOnlyList<RuleFact> Facts { get; }

        public FactListenerDelivery(
            ActiveRuleBinding binding,
            FactListenerRegistration registration,
            OpId rootId,
            IReadOnlyList<RuleFact> facts)
        {
            Binding = binding;
            Registration = registration;
            RootId = rootId;
            Facts = facts;
        }

        public static int Compare(FactListenerDelivery left, FactListenerDelivery right)
        {
            int phase = left.Registration.Phase.CompareTo(right.Registration.Phase);
            if (phase != 0)
                return phase;
            int creation = left.Binding.CreationOrder.CompareTo(right.Binding.CreationOrder);
            if (creation != 0)
                return creation;
            int id = string.Compare(left.Binding.Id.Value, right.Binding.Id.Value, StringComparison.Ordinal);
            if (id != 0)
                return id;
            return left.Registration.RegistrationOrder.CompareTo(right.Registration.RegistrationOrder);
        }
    }

    internal sealed class TypedMiddlewareRegistration<TOp, TResult> : MiddlewareRegistration
        where TOp : IRuleOp<TResult>
    {
        private readonly IOpMiddleware<TOp, TResult> middleware;

        public TypedMiddlewareRegistration(
            RuleLifecyclePhase phase,
            long registrationOrder,
            IOpMiddleware<TOp, TResult> middleware)
            : base(typeof(TOp), typeof(TResult), phase, registrationOrder) =>
            this.middleware = middleware;

        internal override async ValueTask<object> Invoke(
            ActiveRuleBinding binding,
            IFrameInvocation invocation,
            RuleDispatcher dispatcher,
            Func<ValueTask<object>> next)
        {
            if (!(invocation is FrameInvocation<TOp> typed))
                throw new InvalidOperationException("Middleware received an incompatible operation frame.");

            MiddlewareContinuation<TResult> continuation =
                new MiddlewareContinuation<TResult>(next);
            OpContext context = new OpContext(dispatcher, typed.Frame.Id, binding);
            OpResult<TResult> result;
            try
            {
                result = await middleware.Invoke(
                    binding,
                    typed.Frame,
                    context,
                    continuation.Invoke);
            }
            catch
            {
                try
                {
                    await continuation.CompleteInvocation();
                }
                finally
                {
                    await context.CompleteInvocation();
                }
                throw;
            }

            bool continuationWasActive = false;
            bool contextWasActive;
            try
            {
                continuationWasActive = await continuation.CompleteInvocation();
            }
            finally
            {
                contextWasActive = await context.CompleteInvocation();
            }
            if (continuationWasActive)
            {
                throw new InvalidOperationException(
                    $"Middleware for {typeof(TOp).Name} returned before awaiting its continuation.");
            }
            if (contextWasActive)
            {
                throw new InvalidOperationException(
                    $"Middleware for {typeof(TOp).Name} returned before awaiting its child dispatch.");
            }
            if (result == null)
                throw new InvalidOperationException($"Middleware for {typeof(TOp).Name} returned a null result.");
            return result;
        }
    }

    internal sealed class MiddlewareContinuation<TResult>
    {
        private readonly object gate = new object();
        private readonly Func<ValueTask<object>> next;
        private bool isActive = true;
        private bool wasInvoked;
        private TaskCompletionSource<bool> settlement;

        public MiddlewareContinuation(Func<ValueTask<object>> next) =>
            this.next = next ?? throw new ArgumentNullException(nameof(next));

        public ValueTask<OpResult<TResult>> Invoke()
        {
            TaskCompletionSource<bool> current;
            lock (gate)
            {
                if (!isActive)
                    throw new InvalidOperationException("Middleware cannot continue after its callback returns.");
                if (wasInvoked)
                    throw new InvalidOperationException("Middleware may invoke its continuation at most once.");
                wasInvoked = true;
                current = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                settlement = current;
            }
            return InvokeAndSettle(current);
        }

        public async ValueTask<bool> CompleteInvocation()
        {
            Task pending;
            lock (gate)
            {
                if (!isActive)
                    throw new InvalidOperationException("A middleware continuation completed more than once.");
                isActive = false;
                pending = settlement?.Task;
            }
            if (pending == null || pending.IsCompleted)
                return false;
            await pending;
            return true;
        }

        private async ValueTask<OpResult<TResult>> InvokeAndSettle(
            TaskCompletionSource<bool> current)
        {
            try
            {
                object value = await next();
                if (!(value is OpResult<TResult> typed))
                    throw new InvalidOperationException("Middleware continuation returned an impossible result type.");
                return typed;
            }
            finally
            {
                current.TrySetResult(true);
            }
        }
    }

    internal sealed class TypedFactListenerRegistration<TFact> : FactListenerRegistration
        where TFact : RuleFact
    {
        private readonly IFactListener<TFact> listener;

        public TypedFactListenerRegistration(
            RuleLifecyclePhase phase,
            long registrationOrder,
            IFactListener<TFact> listener)
            : base(typeof(TFact), phase, false, registrationOrder) => this.listener = listener;

        internal override bool Matches(RuleFact fact) => fact is TFact;

        internal override ValueTask Invoke(
            ActiveRuleBinding binding,
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            FactContext context)
        {
            if (facts.Count != 1 || !(facts[0] is TFact typed))
                throw new InvalidOperationException("A single-Fact listener received an impossible delivery.");
            return listener.OnFactCommitted(binding, typed, context);
        }
    }

    internal sealed class TypedFactBatchListenerRegistration<TFact> : FactListenerRegistration
        where TFact : RuleFact
    {
        private readonly IFactBatchListener<TFact> listener;

        public TypedFactBatchListenerRegistration(
            RuleLifecyclePhase phase,
            long registrationOrder,
            IFactBatchListener<TFact> listener)
            : base(typeof(TFact), phase, true, registrationOrder) => this.listener = listener;

        internal override bool Matches(RuleFact fact) => fact is TFact;

        internal override ValueTask Invoke(
            ActiveRuleBinding binding,
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            FactContext context)
        {
            TFact[] typed = facts.Cast<TFact>().ToArray();
            return listener.OnFactsCommitted(
                binding,
                new CommittedFactBatch<TFact>(rootId, Array.AsReadOnly(typed)),
                context);
        }
    }
}
