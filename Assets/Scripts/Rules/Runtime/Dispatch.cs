using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    public interface IRuleOp
    {
    }

    public interface IRuleOp<TResult> : IRuleOp
    {
    }

    public interface IOpHandler<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        ValueTask<TResult> Handle(OpFrame<TOp> frame, OpContext context);
    }

    public enum InvocationPolicy
    {
        ExternalAllowed,
        NestedOnly
    }

    public enum OpStatus
    {
        Resolved,
        Invalid,
        Interrupted,
        Cancelled
    }

    /// <summary>
    /// Reserved frame data for the action lifecycle introduced by issue #122.
    /// Dispatch issue #120 intentionally never supplies a value for this slot.
    /// </summary>
    public sealed class ActionProfile
    {
        internal ActionProfile()
        {
        }
    }

    public sealed class OpResult<TResult>
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts =
            Array.AsReadOnly(Array.Empty<RuleFact>());

        public OpStatus Status { get; }
        public TResult Value { get; }
        public IReadOnlyList<RuleFact> Facts { get; }
        public string InvalidReason { get; }

        private OpResult(
            OpStatus status,
            TResult value,
            IReadOnlyList<RuleFact> facts,
            string invalidReason)
        {
            if (status == OpStatus.Invalid && string.IsNullOrWhiteSpace(invalidReason))
                throw new ArgumentException("An invalid result requires a reason.", nameof(invalidReason));
            if (status != OpStatus.Invalid && invalidReason != null)
                throw new ArgumentException("Only an invalid result can have an invalid reason.", nameof(invalidReason));

            Status = status;
            Value = value;
            Facts = facts ?? NoFacts;
            InvalidReason = invalidReason;
        }

        public static OpResult<TResult> Resolved(TResult value) =>
            new OpResult<TResult>(OpStatus.Resolved, value, NoFacts, null);

        public static OpResult<TResult> Invalid(string reason) =>
            new OpResult<TResult>(OpStatus.Invalid, default, NoFacts, reason);

        public static OpResult<TResult> Interrupted() =>
            new OpResult<TResult>(OpStatus.Interrupted, default, NoFacts, null);

        public static OpResult<TResult> Cancelled() =>
            new OpResult<TResult>(OpStatus.Cancelled, default, NoFacts, null);

        internal OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new OpResult<TResult>(Status, Value, facts, InvalidReason);
    }

    public sealed class OpFrame<TOp>
        where TOp : IRuleOp
    {
        public OpId Id { get; }
        public OpId RootId { get; }
        public OpId? ParentId { get; }
        public OpId? CauseId { get; }
        public InvocationPolicy InvocationPolicy { get; }
        public TOp Op { get; }
        public RulesSnapshot StartSnapshot { get; }
        public ActionProfile ActionProfile { get; }

        internal OpFrame(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            InvocationPolicy invocationPolicy,
            TOp op,
            RulesSnapshot startSnapshot)
        {
            if (id.IsEmpty || rootId.IsEmpty)
                throw new ArgumentException("Frame and root IDs are required.");
            if (ReferenceEquals(op, null))
                throw new ArgumentNullException(nameof(op));

            Id = id;
            RootId = rootId;
            ParentId = parentId;
            CauseId = causeId;
            InvocationPolicy = invocationPolicy;
            Op = op;
            StartSnapshot = startSnapshot ?? throw new ArgumentNullException(nameof(startSnapshot));
            ActionProfile = null;
        }
    }

    public interface IOpIdProvider
    {
        OpId Next();
    }

    public sealed class SequentialOpIdProvider : IOpIdProvider
    {
        private long next;

        public SequentialOpIdProvider(long firstValue = 1)
        {
            if (firstValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(firstValue));
            next = firstValue;
        }

        public OpId Next()
        {
            if (next == long.MaxValue)
                throw new InvalidOperationException("The operation ID sequence is exhausted.");
            return new OpId(next++);
        }
    }

    internal interface IOpFrameView
    {
        OpId Id { get; }
        OpId RootId { get; }
        OpId? ParentId { get; }
        OpId? CauseId { get; }
        Type OpType { get; }
        object TypedFrame { get; }
    }

    internal sealed class OpFrameView<TOp> : IOpFrameView
        where TOp : IRuleOp
    {
        private readonly OpFrame<TOp> frame;

        public OpFrameView(OpFrame<TOp> frame) =>
            this.frame = frame ?? throw new ArgumentNullException(nameof(frame));

        public OpId Id => frame.Id;
        public OpId RootId => frame.RootId;
        public OpId? ParentId => frame.ParentId;
        public OpId? CauseId => frame.CauseId;
        public Type OpType => typeof(TOp);
        public object TypedFrame => frame;
    }

    public sealed class ResolutionTrace
    {
        private readonly Dictionary<OpId, IOpFrameView> frames =
            new Dictionary<OpId, IOpFrameView>();

        internal void Add(IOpFrameView frame)
        {
            if (frames.ContainsKey(frame.Id))
                throw new InvalidOperationException($"Duplicate operation ID {frame.Id.Value}.");
            frames.Add(frame.Id, frame);
        }

        public bool Exists(OpId id) => frames.ContainsKey(id);

        public OpFrame<TOp> Get<TOp>(OpId id)
            where TOp : IRuleOp
        {
            IOpFrameView view = Require(id);
            if (view.TypedFrame is OpFrame<TOp> typed)
                return typed;
            throw new InvalidOperationException(
                $"Operation {id.Value} is {view.OpType.Name}, not {typeof(TOp).Name}.");
        }

        public bool IsDescendantOf(OpId candidateId, OpId ancestorId) =>
            Follows(candidateId, ancestorId, frame => frame.ParentId);

        public bool IsCausedBy(OpId candidateId, OpId causeId) =>
            Follows(candidateId, causeId, frame => frame.CauseId);

        public OpFrame<TOp> FindNearestAncestor<TOp>(OpId candidateId)
            where TOp : IRuleOp =>
            FindFollowing<TOp>(candidateId, frame => frame.ParentId);

        public OpFrame<TOp> FindCausingAncestor<TOp>(OpId candidateId)
            where TOp : IRuleOp =>
            FindFollowing<TOp>(candidateId, frame => frame.CauseId);

        internal IReadOnlyList<IOpFrameView> OrderedFrames =>
            frames.Values.OrderBy(frame => frame.Id.Value).ToArray();

        internal IOpFrameView Require(OpId id)
        {
            if (!frames.TryGetValue(id, out IOpFrameView frame))
                throw new InvalidOperationException($"Operation {id.Value} is not in this trace.");
            return frame;
        }

        private bool Follows(
            OpId candidateId,
            OpId targetId,
            Func<IOpFrameView, OpId?> next)
        {
            IOpFrameView current = Require(candidateId);
            HashSet<OpId> visited = new HashSet<OpId> { candidateId };
            while (next(current).HasValue)
            {
                OpId nextId = next(current).Value;
                if (nextId == targetId)
                    return true;
                if (!visited.Add(nextId))
                    throw new InvalidOperationException("A cycle exists in operation provenance.");
                current = Require(nextId);
            }
            return false;
        }

        private OpFrame<TOp> FindFollowing<TOp>(
            OpId candidateId,
            Func<IOpFrameView, OpId?> next)
            where TOp : IRuleOp
        {
            IOpFrameView current = Require(candidateId);
            HashSet<OpId> visited = new HashSet<OpId> { candidateId };
            while (next(current).HasValue)
            {
                OpId nextId = next(current).Value;
                if (!visited.Add(nextId))
                    throw new InvalidOperationException("A cycle exists in operation provenance.");
                current = Require(nextId);
                if (current.TypedFrame is OpFrame<TOp> typed)
                    return typed;
            }
            return null;
        }
    }

    public sealed class ResolutionDiagnostics
    {
        private readonly Dictionary<OpId, DiagnosticCompletion> completions =
            new Dictionary<OpId, DiagnosticCompletion>();
        private readonly ResolutionTrace trace;

        internal ResolutionDiagnostics(ResolutionTrace trace) =>
            this.trace = trace ?? throw new ArgumentNullException(nameof(trace));

        public string Compact
        {
            get
            {
                List<string> lines = new List<string>();
                foreach (IOpFrameView frame in trace.OrderedFrames)
                {
                    int depth = Depth(frame);
                    string prefix = new string(' ', depth * 2);
                    string relation = frame.ParentId.HasValue
                        ? $" parent={frame.ParentId.Value.Value} cause={frame.CauseId.Value.Value}"
                        : " root";
                    completions.TryGetValue(frame.Id, out DiagnosticCompletion completion);
                    string result = completion == null ? string.Empty : $" -> {completion.Status}";
                    lines.Add($"{prefix}[op {frame.Id.Value}{relation}] {frame.OpType.Name}{result}");

                    if (completion == null)
                        continue;
                    foreach (RuleFact fact in completion.DirectFacts)
                    {
                        lines.Add(
                            $"{prefix}  [fact {fact.Id.Value}] {fact.GetType().Name} " +
                            $"source={fact.SourceOpId.Value} root={fact.RootOpId.Value}");
                    }
                }
                return string.Join("\n", lines);
            }
        }

        internal void Complete(OpId id, OpStatus status, IReadOnlyList<RuleFact> directFacts)
        {
            if (completions.ContainsKey(id))
                throw new InvalidOperationException($"Operation {id.Value} completed more than once.");
            completions.Add(id, new DiagnosticCompletion(status, directFacts));
        }

        private int Depth(IOpFrameView frame)
        {
            int depth = 0;
            HashSet<OpId> visited = new HashSet<OpId> { frame.Id };
            while (frame.ParentId.HasValue)
            {
                if (!visited.Add(frame.ParentId.Value))
                    throw new InvalidOperationException("A cycle exists in operation ancestry.");
                frame = trace.Require(frame.ParentId.Value);
                depth++;
            }
            return depth;
        }

        private sealed class DiagnosticCompletion
        {
            public OpStatus Status { get; }
            public IReadOnlyList<RuleFact> DirectFacts { get; }

            public DiagnosticCompletion(OpStatus status, IReadOnlyList<RuleFact> directFacts)
            {
                Status = status;
                DirectFacts = directFacts ?? Array.AsReadOnly(Array.Empty<RuleFact>());
            }
        }
    }

    public sealed class OpContext
    {
        private readonly RuleDispatcher dispatcher;
        private readonly OpId parentId;

        internal OpContext(RuleDispatcher dispatcher, OpId parentId)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.parentId = parentId;
        }

        public RulesSnapshot Snapshot => dispatcher.Snapshot;
        public ResolutionTrace Trace => dispatcher.Trace;

        public ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op) =>
            dispatcher.DispatchNested(op, parentId);
    }

    public sealed class RuleDispatcherBuilder
    {
        private readonly Dictionary<Type, IRegistration> registrations =
            new Dictionary<Type, IRegistration>();
        private readonly IRulesStore store;
        private readonly IOpIdProvider ids;

        public RuleDispatcherBuilder(IRulesStore store, IOpIdProvider ids = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ids = ids ?? new SequentialOpIdProvider();
        }

        public RuleDispatcherBuilder RegisterHandler<TOp, TResult>(
            IOpHandler<TOp, TResult> handler,
            InvocationPolicy policy = InvocationPolicy.ExternalAllowed)
            where TOp : IRuleOp<TResult>
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            Add(new HandlerRegistration<TOp, TResult>(handler, policy));
            return this;
        }

        public RuleDispatcherBuilder RegisterReducer<TOp, TResult>(
            IOpReducer<TOp, TResult> reducer,
            RuleSource source)
            where TOp : IRuleOp<TResult>
        {
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));
            if (source.IsEmpty)
                throw new ArgumentException("A reducer registration requires a rule source.", nameof(source));
            Add(new ReducerRegistration<TOp, TResult>(reducer, source));
            return this;
        }

        public RuleDispatcher Build() => new RuleDispatcher(store, ids, registrations);

        private void Add(IRegistration registration)
        {
            if (registrations.ContainsKey(registration.OpType))
                throw new InvalidOperationException(
                    $"A resolver is already registered for {registration.OpType.Name}.");
            registrations.Add(registration.OpType, registration);
        }
    }

    public sealed class RuleDispatcher
    {
        private readonly IRulesStore store;
        private readonly IOpIdProvider ids;
        private readonly IReadOnlyDictionary<Type, IRegistration> registrations;
        private RootResolution activeRoot;

        internal RuleDispatcher(
            IRulesStore store,
            IOpIdProvider ids,
            IDictionary<Type, IRegistration> registrations)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            this.registrations = new ReadOnlyDictionary<Type, IRegistration>(
                new Dictionary<Type, IRegistration>(registrations));
            Trace = new ResolutionTrace();
            Diagnostics = new ResolutionDiagnostics(Trace);
        }

        public RulesSnapshot Snapshot => store.Snapshot;
        public ResolutionTrace Trace { get; }
        public ResolutionDiagnostics Diagnostics { get; }

        public async ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));
            if (activeRoot != null)
                throw new InvalidOperationException("A root operation cannot interleave with an active resolution.");

            IRegistration registration = RequireRegistration(op.GetType(), typeof(TResult));
            if (registration.Policy != InvocationPolicy.ExternalAllowed)
                throw new InvalidOperationException(
                    $"{op.GetType().Name} is nested-only and cannot be externally dispatched.");

            OpId rootId = ids.Next();
            RootResolution resolution = new RootResolution(rootId);
            activeRoot = resolution;
            try
            {
                return await DispatchCore(op, registration, resolution, rootId, null, null);
            }
            finally
            {
                activeRoot = null;
            }
        }

        internal async ValueTask<OpResult<TResult>> DispatchNested<TResult>(
            IRuleOp<TResult> op,
            OpId parentId)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));
            if (activeRoot == null)
                throw new InvalidOperationException("Nested dispatch requires an active root resolution.");

            RootResolution resolution = activeRoot;
            resolution.ReserveChild(parentId);
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
                resolution.ReleaseChild(parentId);
            }
        }

        internal ReductionResult<TResult> Reduce<TOp, TResult>(
            OpFrame<TOp> frame,
            IOpReducer<TOp, TResult> reducer,
            RuleSource source)
            where TOp : IRuleOp<TResult>
        {
            return store.Reduce(
                new ReductionContext<TOp>(frame.Op, frame.Id, frame.RootId, source),
                reducer);
        }

        private async ValueTask<OpResult<TResult>> DispatchCore<TResult>(
            IRuleOp<TResult> op,
            IRegistration registration,
            RootResolution resolution,
            OpId rootId,
            OpId? parentId,
            OpId? causeId)
        {
            OpId id = parentId.HasValue ? ids.Next() : rootId;
            int firstFact = resolution.Facts.Count;
            IFrameInvocation invocation = registration.CreateInvocation(
                id, rootId, parentId, causeId, op, store.Snapshot);
            Trace.Add(invocation.FrameView);
            resolution.EnterFrame(id, rootId);
            try
            {
                object resultObject = await registration.Invoke(invocation, this);
                if (!(resultObject is OpResult<TResult> result))
                    throw new InvalidOperationException(
                        $"Resolver for {op.GetType().Name} returned an impossible result type.");

                IReadOnlyList<RuleFact> directFacts = Array.AsReadOnly(Array.Empty<RuleFact>());
                if (registration.IsReducer)
                {
                    directFacts = result.Facts;
                    foreach (RuleFact fact in directFacts)
                        resolution.AddFact(fact, id, rootId);
                }

                IReadOnlyList<RuleFact> subtreeFacts = Array.AsReadOnly(resolution.Facts
                    .Skip(firstFact)
                    .ToArray());
                OpResult<TResult> completed = result.WithFacts(subtreeFacts);
                Diagnostics.Complete(id, completed.Status, directFacts);
                return completed;
            }
            finally
            {
                resolution.ExitFrame(id, rootId);
            }
        }

        private IRegistration RequireRegistration(Type opType, Type resultType)
        {
            if (!registrations.TryGetValue(opType, out IRegistration registration))
                throw new InvalidOperationException($"No resolver is registered for {opType.Name}.");
            if (registration.ResultType != resultType)
                throw new InvalidOperationException(
                    $"Registration for {opType.Name} returns {registration.ResultType.Name}, not {resultType.Name}.");
            return registration;
        }

        private sealed class RootResolution
        {
            private readonly HashSet<OpId> activeFrames = new HashSet<OpId>();
            private readonly HashSet<OpId> parentsWithActiveChildren = new HashSet<OpId>();
            private readonly HashSet<FactId> factIds = new HashSet<FactId>();
            private readonly HashSet<RuleFact> factReferences =
                new HashSet<RuleFact>(ReferenceEqualityComparer<RuleFact>.Instance);

            public OpId RootId { get; }
            public List<RuleFact> Facts { get; } = new List<RuleFact>();

            public RootResolution(OpId rootId) => RootId = rootId;

            public void EnterFrame(OpId id, OpId rootId)
            {
                RequireCurrentRoot(rootId);
                if (!activeFrames.Add(id))
                    throw new InvalidOperationException($"Operation {id.Value} began executing more than once.");
            }

            public void ExitFrame(OpId id, OpId rootId)
            {
                RequireCurrentRoot(rootId);
                if (!activeFrames.Remove(id))
                    throw new InvalidOperationException($"Operation {id.Value} was not actively executing.");
            }

            public void ReserveChild(OpId parentId)
            {
                if (!activeFrames.Contains(parentId))
                {
                    throw new InvalidOperationException(
                        $"Operation context {parentId.Value} is not actively executing in the current root resolution.");
                }
                if (!parentsWithActiveChildren.Add(parentId))
                {
                    throw new InvalidOperationException(
                        $"Operation {parentId.Value} cannot begin an overlapping child dispatch. " +
                        "Await the active child before dispatching another.");
                }
            }

            public void ReleaseChild(OpId parentId)
            {
                if (!parentsWithActiveChildren.Remove(parentId))
                {
                    throw new InvalidOperationException(
                        $"Operation {parentId.Value} has no active child dispatch reservation.");
                }
            }

            public void AddFact(RuleFact fact, OpId sourceId, OpId rootId)
            {
                if (fact == null || !fact.IsStamped)
                    throw new InvalidOperationException("A reducer returned an unstamped Fact.");
                if (fact.SourceOpId != sourceId)
                    throw new InvalidOperationException("A reducer returned a Fact for a different source operation.");
                if (fact.RootOpId != rootId || rootId != RootId)
                    throw new InvalidOperationException("A reducer emitted a Fact across resolution roots.");
                if (!factIds.Add(fact.Id) || !factReferences.Add(fact))
                    throw new InvalidOperationException("A committed Fact was aggregated more than once.");
                Facts.Add(fact);
            }

            private void RequireCurrentRoot(OpId rootId)
            {
                if (rootId != RootId)
                    throw new InvalidOperationException("An operation frame crossed resolution roots.");
            }
        }
    }

    internal interface IRegistration
    {
        Type OpType { get; }
        Type ResultType { get; }
        InvocationPolicy Policy { get; }
        bool IsReducer { get; }
        IFrameInvocation CreateInvocation(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            IRuleOp op,
            RulesSnapshot snapshot);
        ValueTask<object> Invoke(IFrameInvocation invocation, RuleDispatcher dispatcher);
    }

    internal interface IFrameInvocation
    {
        IOpFrameView FrameView { get; }
    }

    internal sealed class FrameInvocation<TOp> : IFrameInvocation
        where TOp : IRuleOp
    {
        public OpFrame<TOp> Frame { get; }
        public IOpFrameView FrameView { get; }

        public FrameInvocation(OpFrame<TOp> frame)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            FrameView = new OpFrameView<TOp>(frame);
        }
    }

    internal abstract class Registration<TOp, TResult> : IRegistration
        where TOp : IRuleOp<TResult>
    {
        protected Registration(InvocationPolicy policy) => Policy = policy;

        public Type OpType => typeof(TOp);
        public Type ResultType => typeof(TResult);
        public InvocationPolicy Policy { get; }
        public abstract bool IsReducer { get; }

        public IFrameInvocation CreateInvocation(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            IRuleOp op,
            RulesSnapshot snapshot)
        {
            if (!(op is TOp typed))
                throw new InvalidOperationException(
                    $"Registration for {typeof(TOp).Name} received {op.GetType().Name}.");
            return new FrameInvocation<TOp>(new OpFrame<TOp>(
                id, rootId, parentId, causeId, Policy, typed, snapshot));
        }

        public abstract ValueTask<object> Invoke(
            IFrameInvocation invocation,
            RuleDispatcher dispatcher);

        protected static OpFrame<TOp> GetFrame(IFrameInvocation invocation)
        {
            if (invocation is FrameInvocation<TOp> typed)
                return typed.Frame;
            throw new InvalidOperationException("A resolver received an impossible frame type.");
        }
    }

    internal sealed class HandlerRegistration<TOp, TResult> : Registration<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        private readonly IOpHandler<TOp, TResult> handler;

        public HandlerRegistration(IOpHandler<TOp, TResult> handler, InvocationPolicy policy)
            : base(policy) => this.handler = handler;

        public override bool IsReducer => false;

        public override async ValueTask<object> Invoke(
            IFrameInvocation invocation,
            RuleDispatcher dispatcher)
        {
            OpFrame<TOp> frame = GetFrame(invocation);
            TResult value = await handler.Handle(frame, new OpContext(dispatcher, frame.Id));
            return OpResult<TResult>.Resolved(value);
        }
    }

    internal sealed class ReducerRegistration<TOp, TResult> : Registration<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        private readonly IOpReducer<TOp, TResult> reducer;
        private readonly RuleSource source;

        public ReducerRegistration(IOpReducer<TOp, TResult> reducer, RuleSource source)
            : base(InvocationPolicy.NestedOnly)
        {
            this.reducer = reducer;
            this.source = source;
        }

        public override bool IsReducer => true;

        public override ValueTask<object> Invoke(
            IFrameInvocation invocation,
            RuleDispatcher dispatcher)
        {
            OpFrame<TOp> frame = GetFrame(invocation);
            ReductionResult<TResult> reduced = dispatcher.Reduce(frame, reducer, source);
            OpResult<TResult> result = reduced.IsAccepted
                ? OpResult<TResult>.Resolved(reduced.Value)
                : OpResult<TResult>.Invalid(reduced.RejectionReason);
            return new ValueTask<object>(result.WithFacts(reduced.Facts));
        }
    }

    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance =
            new ReferenceEqualityComparer<T>();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
