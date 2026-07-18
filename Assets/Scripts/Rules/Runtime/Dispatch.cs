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
    /// Marks a value as an operation that can participate in the rules dispatch pipeline.
    /// </summary>
    /// <remarks>
    /// This non-generic contract lets dispatcher infrastructure track operations without
    /// discarding the result type carried by <see cref="IRuleOp{TResult}"/>.
    /// </remarks>
    public interface IRuleOp
    {
    }

    /// <summary>
    /// Defines a rules operation that resolves to a value of <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">
    /// The value produced when the operation resolves successfully.
    /// </typeparam>
    public interface IRuleOp<TResult> : IRuleOp
    {
    }

    /// <summary>
    /// Handles a typed operation and may coordinate nested operations through the supplied context.
    /// </summary>
    /// <typeparam name="TOp">The concrete operation type handled by this implementation.</typeparam>
    /// <typeparam name="TResult">The successful result produced by the operation.</typeparam>
    public interface IOpHandler<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        /// <summary>
        /// Handles one engine-owned operation frame.
        /// </summary>
        /// <param name="frame">
        /// Immutable identity, provenance, and starting-state information for this invocation.
        /// </param>
        /// <param name="context">
        /// Handler-scoped access to current rules state, tracing, and nested dispatch.
        /// Nested dispatches must be awaited before this method returns.
        /// </param>
        /// <returns>
        /// A task-like value containing the successful operation result. The dispatcher wraps
        /// the value and any facts committed by the operation subtree in an <see cref="OpResult{TResult}"/>.
        /// </returns>
        ValueTask<TResult> Handle(OpFrame<TOp> frame, OpContext context);
    }

    /// <summary>
    /// Specifies where a registered operation may begin execution.
    /// </summary>
    public enum InvocationPolicy
    {
        /// <summary>
        /// The operation may be dispatched as a root operation or as a nested child.
        /// </summary>
        ExternalAllowed,

        /// <summary>
        /// The operation may be dispatched only from an active <see cref="OpContext"/>.
        /// </summary>
        NestedOnly
    }

    /// <summary>
    /// Describes how an operation resolution completed.
    /// </summary>
    public enum OpStatus
    {
        /// <summary>
        /// The operation completed successfully and produced a value.
        /// </summary>
        Resolved,

        /// <summary>
        /// The operation was evaluated but could not produce a valid result.
        /// </summary>
        Invalid,

        /// <summary>
        /// The operation stopped because runtime behavior interrupted its normal resolution.
        /// </summary>
        Interrupted,

        /// <summary>
        /// The operation was cancelled before normal resolution completed.
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// Defines the action-lifecycle metadata slot carried by operation frames.
    /// </summary>
    /// <remarks>
    /// The typed dispatch runtime currently leaves this slot empty. Keeping the placeholder in the
    /// frame contract allows action-specific metadata to be added without replacing frame APIs.
    /// </remarks>
    public sealed class ActionProfile
    {
        internal ActionProfile()
        {
        }
    }

    /// <summary>
    /// Provides the common contract for every structurally distinct operation outcome.
    /// </summary>
    /// <typeparam name="TResult">The value type produced by a resolved operation.</typeparam>
    /// <remarks>
    /// Each outcome is represented by one sealed derived type. This prevents callers from reading
    /// a successful value or invalid reason from an outcome that cannot contain it. Facts include
    /// commits made directly by the operation and by every nested descendant that completed within
    /// its frame, including commits retained by interrupted or cancelled outcomes.
    /// </remarks>
    public abstract class OpResult<TResult>
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts =
            Array.AsReadOnly(Array.Empty<RuleFact>());

        private protected OpResult(IReadOnlyList<RuleFact> facts)
        {
            Facts = facts ?? NoFacts;
        }

        /// <summary>
        /// Gets the final outcome category for diagnostics and compact status reporting.
        /// </summary>
        /// <remarks>
        /// Use the concrete result type when reading outcome-specific data. This value mirrors that
        /// type and is not a separate discriminator that controls the validity of another property.
        /// </remarks>
        public abstract OpStatus Status { get; }

        /// <summary>
        /// Gets the committed facts produced by this operation subtree in commit order.
        /// </summary>
        public IReadOnlyList<RuleFact> Facts { get; }

        /// <summary>
        /// Creates a resolved result with no attached facts.
        /// </summary>
        /// <param name="value">The value produced by the operation.</param>
        /// <returns>A resolved result. The dispatcher attaches subtree facts before returning it.</returns>
        public static ResolvedOpResult<TResult> Resolved(TResult value) =>
            new ResolvedOpResult<TResult>(value, NoFacts);

        /// <summary>
        /// Creates an invalid result with no attached facts.
        /// </summary>
        /// <param name="reason">A non-empty explanation suitable for diagnostics or callers.</param>
        /// <returns>An invalid operation result.</returns>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is empty or whitespace.</exception>
        public static InvalidOpResult<TResult> Invalid(string reason) =>
            new InvalidOpResult<TResult>(reason, NoFacts);

        /// <summary>
        /// Creates a result indicating that runtime behavior interrupted the operation.
        /// </summary>
        /// <returns>An interrupted operation result.</returns>
        public static InterruptedOpResult<TResult> Interrupted() =>
            new InterruptedOpResult<TResult>(NoFacts);

        /// <summary>
        /// Creates a result indicating that the operation was cancelled.
        /// </summary>
        /// <returns>A cancelled operation result.</returns>
        public static CancelledOpResult<TResult> Cancelled() =>
            new CancelledOpResult<TResult>(NoFacts);

        internal abstract OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts);
    }

    /// <summary>
    /// Represents an operation that legally resolved and produced a value.
    /// </summary>
    /// <typeparam name="TResult">The type of the resolved value.</typeparam>
    public sealed class ResolvedOpResult<TResult> : OpResult<TResult>
    {
        internal ResolvedOpResult(TResult value, IReadOnlyList<RuleFact> facts)
            : base(facts)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Resolved;

        /// <summary>
        /// Gets the value produced by the resolved operation.
        /// </summary>
        public TResult Value { get; }

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new ResolvedOpResult<TResult>(Value, facts);
    }

    /// <summary>
    /// Represents an operation that could not legally begin or produce a resolved value.
    /// </summary>
    /// <typeparam name="TResult">The value type the operation would produce if resolved.</typeparam>
    public sealed class InvalidOpResult<TResult> : OpResult<TResult>
    {
        internal InvalidOpResult(string reason, IReadOnlyList<RuleFact> facts)
            : base(facts)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("An invalid result requires a reason.", nameof(reason));

            Reason = reason;
        }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Invalid;

        /// <summary>
        /// Gets the explanation of why the operation was invalid.
        /// </summary>
        public string Reason { get; }

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new InvalidOpResult<TResult>(Reason, facts);
    }

    /// <summary>
    /// Represents an operation that legally began but was disrupted before normal resolution.
    /// </summary>
    /// <typeparam name="TResult">The value type the operation would produce if resolved.</typeparam>
    public sealed class InterruptedOpResult<TResult> : OpResult<TResult>
    {
        internal InterruptedOpResult(IReadOnlyList<RuleFact> facts)
            : base(facts)
        {
        }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Interrupted;

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new InterruptedOpResult<TResult>(facts);
    }

    /// <summary>
    /// Represents an explicitly cancelled operation that did not complete normal resolution.
    /// </summary>
    /// <typeparam name="TResult">The value type the operation would produce if resolved.</typeparam>
    public sealed class CancelledOpResult<TResult> : OpResult<TResult>
    {
        internal CancelledOpResult(IReadOnlyList<RuleFact> facts)
            : base(facts)
        {
        }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Cancelled;

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new CancelledOpResult<TResult>(facts);
    }

    /// <summary>
    /// Provides immutable, dispatcher-owned identity and provenance for one operation invocation.
    /// </summary>
    /// <typeparam name="TOp">The concrete operation stored in the frame.</typeparam>
    /// <remarks>
    /// A frame belongs to one root resolution. <see cref="ParentId"/> records execution nesting,
    /// while <see cref="CauseId"/> records causal provenance so future dispatch features can
    /// distinguish the two relationships without changing handler contracts.
    /// </remarks>
    public sealed class OpFrame<TOp>
        where TOp : IRuleOp
    {
        /// <summary>
        /// Gets the unique identifier for this invocation.
        /// </summary>
        public OpId Id { get; }

        /// <summary>
        /// Gets the identifier of the root invocation that owns this frame.
        /// </summary>
        public OpId RootId { get; }

        /// <summary>
        /// Gets the immediately enclosing operation, or <see langword="null"/> for a root frame.
        /// </summary>
        public OpId? ParentId { get; }

        /// <summary>
        /// Gets the operation that caused this invocation, or <see langword="null"/> for a root frame.
        /// </summary>
        public OpId? CauseId { get; }

        /// <summary>
        /// Gets the registration policy used to invoke this operation.
        /// </summary>
        public InvocationPolicy InvocationPolicy { get; }

        /// <summary>
        /// Gets the operation value being handled.
        /// </summary>
        public TOp Op { get; }

        /// <summary>
        /// Gets the immutable rules snapshot captured immediately before this frame began.
        /// </summary>
        public RulesSnapshot StartSnapshot { get; }

#nullable enable annotations
        /// <summary>
        /// Gets reserved action-lifecycle data, or <see langword="null"/> until action profiles are implemented.
        /// </summary>
        public ActionProfile? ActionProfile { get; }
#nullable restore annotations

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

    /// <summary>
    /// Supplies unique operation identifiers to a <see cref="RuleDispatcher"/>.
    /// </summary>
    public interface IOpIdProvider
    {
        /// <summary>
        /// Returns the next non-empty operation identifier.
        /// </summary>
        /// <returns>An identifier that has not previously been returned by this provider.</returns>
        OpId Next();
    }

    /// <summary>
    /// Generates deterministic, monotonically increasing operation identifiers.
    /// </summary>
    /// <remarks>
    /// This provider is intended for deterministic runtime behavior and tests. A provider instance
    /// is consumed by one dispatcher and is not synchronized for concurrent direct access.
    /// </remarks>
    public sealed class SequentialOpIdProvider : IOpIdProvider
    {
        private long next;

        /// <summary>
        /// Initializes a sequence at the specified positive value.
        /// </summary>
        /// <param name="firstValue">The value returned by the first call to <see cref="Next"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="firstValue"/> is zero or negative.
        /// </exception>
        public SequentialOpIdProvider(long firstValue = 1)
        {
            if (firstValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(firstValue));
            next = firstValue;
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">The sequence has no remaining positive values.</exception>
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

    /// <summary>
    /// Stores immutable operation frames and exposes execution and causal ancestry queries.
    /// </summary>
    /// <remarks>
    /// A dispatcher appends each frame before invoking its resolver. Frames remain available for
    /// the lifetime of the trace, so diagnostics and later handlers can inspect completed roots.
    /// Ancestry queries are strict: an operation is not considered its own ancestor or cause.
    /// </remarks>
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

        /// <summary>
        /// Determines whether a frame with the specified identifier has been recorded.
        /// </summary>
        /// <param name="id">The operation identifier to look up.</param>
        /// <returns><see langword="true"/> when the trace contains the frame; otherwise, <see langword="false"/>.</returns>
        public bool Exists(OpId id) => frames.ContainsKey(id);

        /// <summary>
        /// Gets a recorded frame and verifies its concrete operation type.
        /// </summary>
        /// <typeparam name="TOp">The operation type expected by the caller.</typeparam>
        /// <param name="id">The identifier of the frame to retrieve.</param>
        /// <returns>The recorded frame with its original typed operation.</returns>
        /// <exception cref="InvalidOperationException">
        /// No frame has the identifier, or the recorded operation is not <typeparamref name="TOp"/>.
        /// </exception>
        public OpFrame<TOp> Get<TOp>(OpId id)
            where TOp : IRuleOp
        {
            IOpFrameView view = Require(id);
            if (view.TypedFrame is OpFrame<TOp> typed)
                return typed;
            throw new InvalidOperationException(
                $"Operation {id.Value} is {view.OpType.Name}, not {typeof(TOp).Name}.");
        }

        /// <summary>
        /// Determines whether an operation is nested beneath another operation.
        /// </summary>
        /// <param name="candidateId">The possible descendant.</param>
        /// <param name="ancestorId">The possible strict ancestor.</param>
        /// <returns>
        /// <see langword="true"/> when following parent links from the candidate reaches the ancestor;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the ancestry contains a cycle.
        /// </exception>
        public bool IsDescendantOf(OpId candidateId, OpId ancestorId) =>
            Follows(candidateId, ancestorId, frame => frame.ParentId);

        /// <summary>
        /// Determines whether an operation's causal chain contains another operation.
        /// </summary>
        /// <param name="candidateId">The operation whose causal chain is inspected.</param>
        /// <param name="causeId">The possible strict cause.</param>
        /// <returns>
        /// <see langword="true"/> when following cause links reaches <paramref name="causeId"/>;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the causal chain contains a cycle.
        /// </exception>
        public bool IsCausedBy(OpId candidateId, OpId causeId) =>
            Follows(candidateId, causeId, frame => frame.CauseId);

        /// <summary>
        /// Finds the closest parent-chain ancestor with the requested operation type.
        /// </summary>
        /// <typeparam name="TOp">The ancestor operation type to find.</typeparam>
        /// <param name="candidateId">The operation where the search begins.</param>
        /// <returns>The nearest matching ancestor frame, or <see langword="null"/> when none exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the ancestry contains a cycle.
        /// </exception>
        public OpFrame<TOp> FindNearestAncestor<TOp>(OpId candidateId)
            where TOp : IRuleOp =>
            FindFollowing<TOp>(candidateId, frame => frame.ParentId);

        /// <summary>
        /// Finds the closest causal-chain ancestor with the requested operation type.
        /// </summary>
        /// <typeparam name="TOp">The causing operation type to find.</typeparam>
        /// <param name="candidateId">The operation where the search begins.</param>
        /// <returns>The nearest matching causal frame, or <see langword="null"/> when none exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the causal chain contains a cycle.
        /// </exception>
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

    /// <summary>
    /// Produces a human-readable view of traced operations, completion states, and direct facts.
    /// </summary>
    /// <remarks>
    /// Diagnostics are intended for logs and debugging rather than machine parsing. The compact
    /// representation is generated from the associated <see cref="ResolutionTrace"/> on demand.
    /// </remarks>
    public sealed class ResolutionDiagnostics
    {
        private readonly Dictionary<OpId, DiagnosticCompletion> completions =
            new Dictionary<OpId, DiagnosticCompletion>();
        private readonly ResolutionTrace trace;

        internal ResolutionDiagnostics(ResolutionTrace trace) =>
            this.trace = trace ?? throw new ArgumentNullException(nameof(trace));

        /// <summary>
        /// Gets an indented operation tree ordered by operation identifier.
        /// </summary>
        /// <remarks>
        /// Completed operations include their status and directly emitted facts. An operation that
        /// is still executing appears without a completion suffix.
        /// </remarks>
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

    /// <summary>
    /// Exposes callback-scoped rules state, trace data, and nested dispatch.
    /// </summary>
    /// <remarks>
    /// The dispatcher owns the context. It is valid only while the handler or middleware callback
    /// that received it is actively executing. Each callback may have at most one child dispatch
    /// in flight and must await that child before returning or starting another child. For a
    /// middleware callback, its child dispatch and continuation share that in-flight slot and must
    /// be awaited sequentially.
    /// </remarks>
    public sealed class OpContext
    {
        private readonly RuleDispatcher dispatcher;
        private readonly OpId parentId;
        private readonly ActiveRuleBinding activeBinding;
        private readonly CallbackWorkCoordinator work;

        internal OpContext(
            RuleDispatcher dispatcher,
            OpId parentId,
            ActiveRuleBinding activeBinding = null,
            CallbackWorkCoordinator work = null)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.parentId = parentId;
            this.activeBinding = activeBinding;
            this.work = work ?? new CallbackWorkCoordinator();
        }

#nullable enable annotations
        /// <summary>
        /// Gets the active binding authorizing a middleware invocation, or <see langword="null"/>
        /// when the context belongs to the operation's ordinary handler.
        /// </summary>
        public ActiveRuleBinding? ActiveBinding
        {
            get
            {
                RequireActive();
                return activeBinding;
            }
        }

        /// <summary>
        /// Gets the active binding's stable rule source, or <see langword="null"/> for an ordinary handler.
        /// </summary>
        public RuleSource? Source
        {
            get
            {
                RequireActive();
                return activeBinding?.Source;
            }
        }
#nullable restore annotations

        /// <summary>
        /// Gets the latest committed rules snapshot.
        /// </summary>
        public RulesSnapshot Snapshot
        {
            get
            {
                RequireActive();
                return dispatcher.Snapshot;
            }
        }

        /// <summary>
        /// Gets the dispatcher's trace, including the current frame and previously recorded frames.
        /// </summary>
        public ResolutionTrace Trace
        {
            get
            {
                RequireActive();
                return dispatcher.Trace;
            }
        }

        /// <summary>
        /// Dispatches a child operation under the callback that owns this context.
        /// </summary>
        /// <typeparam name="TResult">The successful result type of the child operation.</typeparam>
        /// <param name="op">The child operation to resolve.</param>
        /// <returns>A task-like value containing the child's status, value, and subtree facts.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="op"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The context is no longer active, another callback-owned operation is in flight, or no
        /// compatible resolver is registered.
        /// </exception>
        public ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            return work.StartChild(
                () => dispatcher.DispatchNested(op, parentId),
                "An operation context is not actively executing after its callback returns.",
                $"Operation {parentId.Value} cannot begin an overlapping child dispatch. " +
                "Await the active child before dispatching another.",
                $"Operation {parentId.Value} cannot begin an overlapping child dispatch while " +
                "its middleware continuation is active. Await the continuation before " +
                "dispatching a child.");
        }

        internal async ValueTask<bool> CompleteInvocation() =>
            await work.CompleteInvocation(
                "An operation context completed more than once.") != null;

        private void RequireActive() => work.RequireActive(
            "An operation context cannot be used after its callback returns.");
    }

    /// <summary>
    /// Configures the one-to-one mapping from concrete operation types to handlers or reducers.
    /// </summary>
    /// <remarks>
    /// Each concrete operation type may have exactly one resolver. Building a dispatcher copies
    /// the current registrations so later builder changes do not mutate an existing dispatcher.
    /// </remarks>
    public sealed class RuleDispatcherBuilder
    {
        private readonly Dictionary<Type, IRegistration> registrations =
            new Dictionary<Type, IRegistration>();
        private readonly IRulesStore store;
        private readonly IOpIdProvider ids;
        private RuleRegistry ruleRegistry = RuleRegistry.Empty;

        /// <summary>
        /// Initializes a dispatcher builder with its rules store and operation ID source.
        /// </summary>
        /// <param name="store">The store used for snapshots and reducer commits.</param>
        /// <param name="ids">
        /// The identifier provider, or <see langword="null"/> to use a new
        /// <see cref="SequentialOpIdProvider"/>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
        public RuleDispatcherBuilder(IRulesStore store, IOpIdProvider ids = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ids = ids ?? new SequentialOpIdProvider();
        }

        /// <summary>
        /// Registers an asynchronous handler for a concrete operation type.
        /// </summary>
        /// <typeparam name="TOp">The operation handled by <paramref name="handler"/>.</typeparam>
        /// <typeparam name="TResult">The successful result returned by the handler.</typeparam>
        /// <param name="handler">The handler instance invoked for the operation.</param>
        /// <param name="policy">Whether the operation may begin as a root dispatch.</param>
        /// <returns>This builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><typeparamref name="TOp"/> already has a resolver.</exception>
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

        /// <summary>
        /// Registers a transactional reducer as a nested-only resolver.
        /// </summary>
        /// <typeparam name="TOp">The operation reduced by <paramref name="reducer"/>.</typeparam>
        /// <typeparam name="TResult">The accepted value produced by the reducer.</typeparam>
        /// <param name="reducer">The reducer that validates and stages state changes and facts.</param>
        /// <param name="source">The rule source stamped onto facts committed by this reducer.</param>
        /// <returns>This builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="reducer"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="source"/> is empty.</exception>
        /// <exception cref="InvalidOperationException"><typeparamref name="TOp"/> already has a resolver.</exception>
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

        /// <summary>
        /// Selects the immutable rule registry used for binding-controlled middleware and Fact listeners.
        /// </summary>
        /// <param name="registry">The static registry to validate and attach to the dispatcher.</param>
        /// <returns>This builder so configuration can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// The registry stores static definitions only. Which registrations participate is decided
        /// from <see cref="RulesSnapshot.RuleBindings"/> each time rules work is resolved.
        /// </remarks>
        public RuleDispatcherBuilder UseRuleRegistry(RuleRegistry registry)
        {
            ruleRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            return this;
        }

        /// <summary>
        /// Builds a dispatcher from a snapshot of the current registrations.
        /// </summary>
        /// <returns>A dispatcher that owns its registration map, trace, and diagnostics.</returns>
        public RuleDispatcher Build()
        {
            ruleRegistry.ValidateResolvers(registrations);
            return new RuleDispatcher(store, ids, registrations, ruleRegistry);
        }

        private void Add(IRegistration registration)
        {
            if (registrations.ContainsKey(registration.OpType))
                throw new InvalidOperationException(
                    $"A resolver is already registered for {registration.OpType.Name}.");
            registrations.Add(registration.OpType, registration);
        }
    }

    /// <summary>
    /// Resolves typed rules operations while preserving frame provenance, committed facts, and diagnostics.
    /// </summary>
    /// <remarks>
    /// Root resolutions are serialized: a dispatcher rejects a second root while one is active.
    /// Handlers may dispatch nested children through <see cref="OpContext"/>, but each active frame
    /// may own only one child at a time and must await it. Committed-Fact listeners finish before
    /// the caller regains root ownership; listener-dispatched work runs as serialized causal roots.
    /// Trace and diagnostic history accumulate for the lifetime of the dispatcher.
    /// </remarks>
    public sealed class RuleDispatcher
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts =
            Array.AsReadOnly(Array.Empty<RuleFact>());
        private readonly object gate = new object();
        private readonly IRulesStore store;
        private readonly IOpIdProvider ids;
        private readonly IReadOnlyDictionary<Type, IRegistration> registrations;
        private readonly RuleRegistry ruleRegistry;
        private RootResolution activeRoot;

        internal RuleDispatcher(
            IRulesStore store,
            IOpIdProvider ids,
            IDictionary<Type, IRegistration> registrations,
            RuleRegistry ruleRegistry)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            this.registrations = new ReadOnlyDictionary<Type, IRegistration>(
                new Dictionary<Type, IRegistration>(registrations));
            this.ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
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
        /// Another root is active, the operation is nested-only, no compatible resolver is registered,
        /// or a handler violates nested-dispatch ownership.
        /// </exception>
        /// <remarks>
        /// Resolver, middleware, and post-commit listener exceptions propagate to the caller. State
        /// already committed by a reducer is not rolled back. If resolution fails after a commit,
        /// listeners receive the durable Facts before the resolution exception is rethrown. If that
        /// notification also fails, an <see cref="AggregateException"/> reports the resolution
        /// exception first and the notification exception second. The dispatcher then releases root
        /// ownership so a later independent root may be dispatched.
        /// </remarks>
        public async ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op)
        {
            if (op == null)
                throw new ArgumentNullException(nameof(op));

            RootResolution resolution = new RootResolution();
            try
            {
                lock (gate)
                {
                    if (activeRoot != null)
                    {
                        throw new InvalidOperationException(
                            "A root operation cannot interleave with an active resolution.");
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
                        activeRoot = null;
                }
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
                if (activeRoot == null)
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
                if (activeRoot == null || activeRoot.RootId != committedRootId)
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

        internal void CaptureCommittedFacts(
            IFrameInvocation invocation,
            IReadOnlyList<RuleFact> facts)
        {
            if (invocation == null)
                throw new ArgumentNullException(nameof(invocation));
            if (facts == null)
                throw new ArgumentNullException(nameof(facts));

            lock (gate)
            {
                if (activeRoot == null || activeRoot.RootId != invocation.FrameView.RootId)
                    throw new InvalidOperationException("Reducer Facts crossed resolution root ownership.");

                // Reducer Facts enter the root batch at the store commit point. Middleware may
                // replace the structural result or commit later children while it unwinds, neither
                // of which may discard or reorder an already committed Fact. Root aggregation also
                // retains the source frame's frozen listener selection for later notification.
                invocation.CaptureDirectFacts(facts);
                foreach (RuleFact fact in facts)
                {
                    activeRoot.AddFact(
                        fact,
                        invocation.FrameView.Id,
                        invocation.FrameView.RootId);
                }
            }
        }

        private async ValueTask<OpResult<TResult>> DispatchRoot<TResult>(
            IRuleOp<TResult> op,
            IRegistration registration,
            RootResolution resolution,
            OpId rootId,
            OpId? causeId)
        {
            OpResult<TResult> result;
            try
            {
                result = await DispatchCore(
                    op,
                    registration,
                    resolution,
                    rootId,
                    null,
                    causeId);
            }
            catch (Exception resolutionException)
            {
                IReadOnlyList<CommittedFactRecord> committedFacts =
                    SnapshotCommittedFacts(resolution, rootId);
                if (committedFacts.Count == 0)
                    throw;

                try
                {
                    await NotifyFactListeners(rootId, committedFacts);
                }
                catch (Exception notificationException)
                {
                    throw new AggregateException(
                        "Operation resolution and post-commit Fact notification both failed.",
                        resolutionException,
                        notificationException);
                }

                throw;
            }

            if (result.Status != OpStatus.Invalid && result.Facts.Count > 0)
            {
                await NotifyFactListeners(
                    rootId,
                    SnapshotCommittedFacts(resolution, rootId));
            }
            return result;
        }

        private IReadOnlyList<CommittedFactRecord> SnapshotCommittedFacts(
            RootResolution resolution,
            OpId rootId)
        {
            lock (gate)
            {
                RequireActiveResolution(resolution);
                if (resolution.RootId != rootId)
                    throw new InvalidOperationException(
                        "Committed Facts crossed resolution root ownership.");
                return Array.AsReadOnly(resolution.CommittedFacts.ToArray());
            }
        }

        private async ValueTask NotifyFactListeners(
            OpId rootId,
            IReadOnlyList<CommittedFactRecord> committedFacts)
        {
            if (committedFacts.Any(committed =>
                committed == null || committed.Fact == null ||
                !committed.Fact.IsStamped || committed.Fact.RootOpId != rootId))
            {
                throw new InvalidOperationException("A completed root contains a Fact from another resolution batch.");
            }

            IReadOnlyList<FactListenerDelivery> deliveries =
                ruleRegistry.BuildFactListenerDeliveries(rootId, committedFacts);
            foreach (FactListenerDelivery delivery in deliveries)
            {
                if (delivery.Registration.IsBatch)
                {
                    if (ruleRegistry.IsActive(store.Snapshot, delivery.Binding))
                    {
                        await InvokeFactListener(
                            delivery,
                            delivery.Facts,
                            delivery.RootId);
                    }
                    continue;
                }

                foreach (RuleFact fact in delivery.Facts)
                {
                    if (!ruleRegistry.IsActive(store.Snapshot, delivery.Binding))
                        break;
                    await InvokeFactListener(
                        delivery,
                        Array.AsReadOnly(new[] { fact }),
                        fact.SourceOpId);
                }
            }
        }

        private async ValueTask InvokeFactListener(
            FactListenerDelivery delivery,
            IReadOnlyList<RuleFact> facts,
            OpId causeId)
        {
            FactContext context = new FactContext(
                this,
                delivery.Binding,
                delivery.RootId,
                causeId);
            ValueTask listenerInvocation;
            try
            {
                listenerInvocation = delivery.Registration.Invoke(
                    delivery.Binding,
                    delivery.RootId,
                    facts,
                    context);
            }
            catch
            {
                await context.CompleteInvocation();
                throw;
            }

            try
            {
                await listenerInvocation;
            }
            catch
            {
                await context.CompleteInvocation();
                throw;
            }

            if (await context.CompleteInvocation())
            {
                throw new InvalidOperationException(
                    $"Fact listener for {delivery.Registration.FactType.Name} returned before " +
                    "awaiting its causally linked dispatch.");
            }
        }

        private async ValueTask<OpResult<TResult>> DispatchCore<TResult>(
            IRuleOp<TResult> op,
            IRegistration registration,
            RootResolution resolution,
            OpId rootId,
            OpId? parentId,
            OpId? causeId)
        {
            OpId id;
            int firstFact;
            IFrameInvocation invocation;
            IReadOnlyList<BoundMiddlewareRegistration> middleware;
            IReadOnlyList<BoundFactListenerRegistration> factListeners;
            lock (gate)
            {
                RequireActiveResolution(resolution);
                id = parentId.HasValue ? ids.Next() : rootId;
                firstFact = resolution.Facts.Count;
                RulesSnapshot startSnapshot = store.Snapshot;
                invocation = registration.CreateInvocation(
                    id, rootId, parentId, causeId, op, startSnapshot);
                middleware = ruleRegistry.SelectMiddleware(
                    op.GetType(), typeof(TResult), startSnapshot);
                factListeners = ruleRegistry.SelectFactListeners(startSnapshot);
                Trace.Add(invocation.FrameView);
                resolution.EnterFrame(id, rootId, factListeners);
            }

            try
            {
                object resultObject;
                try
                {
                    resultObject = await InvokeWithMiddleware(
                        registration,
                        invocation,
                        middleware,
                        0);
                }
                catch
                {
                    await SettleActiveChild(resolution, id);
                    throw;
                }

                if (await SettleActiveChild(resolution, id))
                {
                    throw new InvalidOperationException(
                        $"Operation {id.Value} returned before awaiting its active child dispatch.");
                }

                if (!(resultObject is OpResult<TResult> result))
                    throw new InvalidOperationException(
                        $"Resolver for {op.GetType().Name} returned an impossible result type.");

                lock (gate)
                {
                    RequireActiveResolution(resolution);
                    IReadOnlyList<RuleFact> directFacts = invocation.DirectFacts;

                    int subtreeFactCount = resolution.Facts.Count - firstFact;
                    IReadOnlyList<RuleFact> subtreeFacts = NoFacts;
                    if (subtreeFactCount > 0)
                    {
                        RuleFact[] subtreeFactArray = new RuleFact[subtreeFactCount];
                        resolution.Facts.CopyTo(firstFact, subtreeFactArray, 0, subtreeFactCount);
                        subtreeFacts = Array.AsReadOnly(subtreeFactArray);
                    }
                    OpResult<TResult> completed = result.WithFacts(subtreeFacts);
                    Diagnostics.Complete(id, completed.Status, directFacts);
                    return completed;
                }
            }
            finally
            {
                lock (gate)
                    resolution.ExitFrame(id, rootId);
            }
        }

        private ValueTask<object> InvokeWithMiddleware(
            IRegistration registration,
            IFrameInvocation invocation,
            IReadOnlyList<BoundMiddlewareRegistration> middleware,
            int index)
        {
            while (index < middleware.Count &&
                !ruleRegistry.IsActive(store.Snapshot, middleware[index].Binding))
            {
                index++;
            }

            if (index >= middleware.Count)
                return registration.Invoke(invocation, this);

            BoundMiddlewareRegistration current = middleware[index];
            int nextIndex = index + 1;
            return current.Registration.Invoke(
                current.Binding,
                invocation,
                this,
                () => InvokeWithMiddleware(
                    registration,
                    invocation,
                    middleware,
                    nextIndex));
        }

        private async ValueTask<bool> SettleActiveChild(RootResolution resolution, OpId parentId)
        {
            Task settlement;
            lock (gate)
            {
                RequireActiveResolution(resolution);
                settlement = resolution.GetActiveChildSettlement(parentId);
                resolution.SealFrame(parentId);
            }

            if (settlement == null)
                return false;

            await settlement;
            return true;
        }

        private void RequireActiveResolution(RootResolution resolution)
        {
            if (!ReferenceEquals(activeRoot, resolution))
                throw new InvalidOperationException("An operation crossed resolution root ownership.");
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
            private readonly HashSet<OpId> sealedFrames = new HashSet<OpId>();
            private readonly Dictionary<OpId, ChildReservation> activeChildren =
                new Dictionary<OpId, ChildReservation>();
            private readonly Dictionary<OpId, IReadOnlyList<BoundFactListenerRegistration>>
                frameFactListeners =
                    new Dictionary<OpId, IReadOnlyList<BoundFactListenerRegistration>>();
            private readonly HashSet<FactId> factIds = new HashSet<FactId>();
            private readonly HashSet<RuleFact> factReferences =
                new HashSet<RuleFact>(ReferenceEqualityComparer<RuleFact>.Instance);

            public OpId RootId { get; private set; }
            public List<RuleFact> Facts { get; } = new List<RuleFact>();
            public List<CommittedFactRecord> CommittedFacts { get; } =
                new List<CommittedFactRecord>();

            public void Initialize(OpId rootId)
            {
                if (!RootId.IsEmpty)
                    throw new InvalidOperationException("A root resolution was initialized more than once.");
                if (rootId.IsEmpty)
                    throw new ArgumentException("A root resolution requires an operation ID.", nameof(rootId));
                RootId = rootId;
            }

            public void EnterFrame(
                OpId id,
                OpId rootId,
                IReadOnlyList<BoundFactListenerRegistration> factListeners)
            {
                RequireCurrentRoot(rootId);
                if (!activeFrames.Add(id))
                    throw new InvalidOperationException($"Operation {id.Value} began executing more than once.");
                frameFactListeners.Add(
                    id,
                    factListeners ?? throw new ArgumentNullException(nameof(factListeners)));
            }

            public void ExitFrame(OpId id, OpId rootId)
            {
                RequireCurrentRoot(rootId);
                if (activeChildren.ContainsKey(id))
                {
                    throw new InvalidOperationException(
                        $"Operation {id.Value} cannot exit while its child dispatch is active.");
                }
                if (!activeFrames.Remove(id))
                    throw new InvalidOperationException($"Operation {id.Value} was not actively executing.");
                if (!frameFactListeners.Remove(id))
                    throw new InvalidOperationException($"Operation {id.Value} has no listener selection.");
                sealedFrames.Remove(id);
            }

            public ChildReservation ReserveChild(OpId parentId)
            {
                if (!activeFrames.Contains(parentId) || sealedFrames.Contains(parentId))
                {
                    throw new InvalidOperationException(
                        $"Operation context {parentId.Value} is not actively executing in the current root resolution.");
                }
                if (activeChildren.ContainsKey(parentId))
                {
                    throw new InvalidOperationException(
                        $"Operation {parentId.Value} cannot begin an overlapping child dispatch. " +
                        "Await the active child before dispatching another.");
                }

                ChildReservation reservation = new ChildReservation(parentId);
                activeChildren.Add(parentId, reservation);
                return reservation;
            }

            public void ReleaseChild(ChildReservation reservation)
            {
                if (reservation == null ||
                    !activeChildren.TryGetValue(reservation.ParentId, out ChildReservation active) ||
                    !ReferenceEquals(active, reservation))
                {
                    string owner = reservation == null
                        ? "<unknown>"
                        : reservation.ParentId.Value.ToString();
                    throw new InvalidOperationException(
                        $"Operation {owner} does not own its active child reservation.");
                }
                activeChildren.Remove(reservation.ParentId);
            }

            public Task GetActiveChildSettlement(OpId parentId) =>
                activeChildren.TryGetValue(parentId, out ChildReservation reservation)
                    ? reservation.Settlement
                    : null;

            public void SealFrame(OpId id)
            {
                if (!activeFrames.Contains(id))
                {
                    throw new InvalidOperationException(
                        $"Operation {id.Value} cannot stop accepting children in its current state.");
                }
                if (!sealedFrames.Add(id))
                    throw new InvalidOperationException($"Operation {id.Value} stopped executing more than once.");
            }

            public void AddFact(RuleFact fact, OpId sourceId, OpId rootId)
            {
                if (fact == null || !fact.IsStamped)
                    throw new InvalidOperationException("A reducer returned an unstamped Fact.");
                if (fact.SourceOpId != sourceId)
                    throw new InvalidOperationException("A reducer returned a Fact for a different source operation.");
                if (fact.RootOpId != rootId || rootId != RootId)
                    throw new InvalidOperationException("A reducer emitted a Fact across resolution roots.");
                if (!frameFactListeners.TryGetValue(
                    sourceId,
                    out IReadOnlyList<BoundFactListenerRegistration> eligibleListeners))
                {
                    throw new InvalidOperationException(
                        "A committed Fact has no source-frame listener selection.");
                }
                if (!factIds.Add(fact.Id) || !factReferences.Add(fact))
                    throw new InvalidOperationException("A committed Fact was aggregated more than once.");
                Facts.Add(fact);
                CommittedFacts.Add(new CommittedFactRecord(fact, eligibleListeners));
            }

            private void RequireCurrentRoot(OpId rootId)
            {
                if (rootId != RootId)
                    throw new InvalidOperationException("An operation frame crossed resolution roots.");
            }
        }

        private sealed class ChildReservation
        {
            private readonly TaskCompletionSource<bool> settled =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public OpId ParentId { get; }
            public Task Settlement => settled.Task;

            public ChildReservation(OpId parentId) => ParentId = parentId;

            public void Settle() => settled.TrySetResult(true);
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
        IReadOnlyList<RuleFact> DirectFacts { get; }
        void CaptureDirectFacts(IReadOnlyList<RuleFact> facts);
    }

    internal sealed class FrameInvocation<TOp> : IFrameInvocation
        where TOp : IRuleOp
    {
        private static readonly IReadOnlyList<RuleFact> NoDirectFacts =
            Array.AsReadOnly(Array.Empty<RuleFact>());
        private IReadOnlyList<RuleFact> directFacts = NoDirectFacts;

        public OpFrame<TOp> Frame { get; }
        public IOpFrameView FrameView { get; }
        public IReadOnlyList<RuleFact> DirectFacts => directFacts;

        public FrameInvocation(OpFrame<TOp> frame)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            FrameView = new OpFrameView<TOp>(frame);
        }

        public void CaptureDirectFacts(IReadOnlyList<RuleFact> facts)
        {
            if (!ReferenceEquals(directFacts, NoDirectFacts))
                throw new InvalidOperationException("A resolver captured its direct Facts more than once.");
            directFacts = facts ?? throw new ArgumentNullException(nameof(facts));
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
            OpContext context = new OpContext(dispatcher, frame.Id);
            TResult value;
            try
            {
                value = await handler.Handle(frame, context);
            }
            catch
            {
                await context.CompleteInvocation();
                throw;
            }

            if (await context.CompleteInvocation())
            {
                throw new InvalidOperationException(
                    $"Operation {frame.Id.Value} returned before awaiting its active child dispatch.");
            }
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
            dispatcher.CaptureCommittedFacts(invocation, reduced.Facts);
            OpResult<TResult> result = reduced.IsAccepted
                ? OpResult<TResult>.Resolved(reduced.Value)
                : OpResult<TResult>.Invalid(reduced.RejectionReason);
            return new ValueTask<object>(result.WithFacts(reduced.Facts));
        }
    }

    /// <summary>
    /// Exposes completion and failure state for a callback-owned asynchronous result.
    /// </summary>
    /// <remarks>
    /// Completion alone does not release ownership. The callback scope remains responsible for
    /// this source until the returned task-like value consumes its result.
    /// </remarks>
    internal interface IOwnedValueTaskSource
    {
        Task Completion { get; }

        void ThrowIfFailed();
    }

    /// <summary>
    /// Identifies which callback operation owns a <see cref="CallbackWorkCoordinator"/>.
    /// </summary>
    internal enum CallbackWorkKind
    {
        ChildDispatch,
        MiddlewareContinuation
    }

    /// <summary>
    /// Owns one callback's lifetime and its single in-flight asynchronous work slot.
    /// </summary>
    /// <remarks>
    /// Middleware shares one instance between its continuation and binding-scoped
    /// <see cref="OpContext"/>. Acquiring either kind of work is therefore atomic, and a rejected
    /// overlap cannot start or replace the work already in progress. Ownership ends only when the
    /// returned <see cref="ValueTask{TResult}"/> consumes its result; mere operation completion does
    /// not permit another dispatch or continuation. Rejecting a first continuation attempt while
    /// a child owns the slot does not consume the callback's one-continuation allowance.
    /// </remarks>
    internal sealed class CallbackWorkCoordinator
    {
        private readonly object gate = new object();
        private bool isActive = true;
        private bool continuationWasInvoked;
        private IOwnedValueTaskSource activeWork;
        private CallbackWorkKind activeKind;

        internal ValueTask<TResult> StartChild<TResult>(
            Func<ValueTask<TResult>> operation,
            string inactiveMessage,
            string childOverlapMessage,
            string continuationOverlapMessage) =>
            Start(
                operation,
                inactiveMessage,
                childOverlapMessage,
                continuationOverlapMessage);

        internal ValueTask<TResult> StartContinuation<TResult>(
            Func<ValueTask<TResult>> operation)
        {
            OwnedValueTaskSource<TResult> invocation;
            lock (gate)
            {
                if (!isActive)
                {
                    throw new InvalidOperationException(
                        "Middleware cannot continue after its callback returns.");
                }
                if (continuationWasInvoked)
                {
                    throw new InvalidOperationException(
                        "Middleware may invoke its continuation at most once.");
                }
                if (activeWork != null)
                {
                    throw new InvalidOperationException(
                        "Middleware cannot invoke its continuation while a child dispatch is active. " +
                        "Await the active child before continuing.");
                }

                continuationWasInvoked = true;
                invocation = Own(CallbackWorkKind.MiddlewareContinuation, operation);
            }

            invocation.Start();
            return invocation.AsValueTask();
        }

        internal void RequireActive(string inactiveMessage)
        {
            lock (gate)
            {
                if (!isActive)
                    throw new InvalidOperationException(inactiveMessage);
            }
        }

        internal async ValueTask<CallbackWorkKind?> CompleteInvocation(
            string duplicateCompletionMessage)
        {
            IOwnedValueTaskSource pending;
            CallbackWorkKind pendingKind;
            lock (gate)
            {
                if (!isActive)
                    throw new InvalidOperationException(duplicateCompletionMessage);
                isActive = false;
                pending = activeWork;
                pendingKind = activeKind;
            }

            if (pending == null)
                return null;

            // The active slot is cleared only by ValueTask.GetResult. Capturing it while closing
            // the callback therefore records a contract violation even when ignored work already
            // completed synchronously or a retained result is consumed after the callback returns.
            await pending.Completion;
            pending.ThrowIfFailed();
            return pendingKind;
        }

        private ValueTask<TResult> Start<TResult>(
            Func<ValueTask<TResult>> operation,
            string inactiveMessage,
            string childOverlapMessage,
            string continuationOverlapMessage)
        {
            OwnedValueTaskSource<TResult> owned;
            lock (gate)
            {
                if (!isActive)
                    throw new InvalidOperationException(inactiveMessage);
                if (activeWork != null)
                {
                    throw new InvalidOperationException(
                        activeKind == CallbackWorkKind.MiddlewareContinuation
                            ? continuationOverlapMessage
                            : childOverlapMessage);
                }

                owned = Own(CallbackWorkKind.ChildDispatch, operation);
            }

            owned.Start();
            return owned.AsValueTask();
        }

        private OwnedValueTaskSource<TResult> Own<TResult>(
            CallbackWorkKind kind,
            Func<ValueTask<TResult>> operation)
        {
            OwnedValueTaskSource<TResult> owned =
                new OwnedValueTaskSource<TResult>(operation, Release);
            activeKind = kind;
            activeWork = owned;
            return owned;
        }

        private void Release(IOwnedValueTaskSource work)
        {
            lock (gate)
            {
                if (ReferenceEquals(activeWork, work))
                    activeWork = null;
            }
        }
    }

    /// <summary>
    /// Adapts asynchronous work into a single-consumption <see cref="ValueTask{TResult}"/> whose
    /// owner is released only when the caller consumes the result.
    /// </summary>
    /// <typeparam name="TResult">The result returned by the owned asynchronous work.</typeparam>
    /// <remarks>
    /// Callback APIs use this source to distinguish an awaited result from work that merely
    /// completed before the callback returned. The separate completion task lets callback shutdown
    /// wait for ignored work and propagate its failure without treating completion as consumption.
    /// </remarks>
    internal sealed class OwnedValueTaskSource<TResult> :
        IOwnedValueTaskSource,
        IValueTaskSource<TResult>
    {
        private readonly Func<ValueTask<TResult>> operation;
        private readonly Action<IOwnedValueTaskSource> release;
        private readonly TaskCompletionSource<bool> completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private ManualResetValueTaskSourceCore<TResult> source;
        private ExceptionDispatchInfo failure;
        private int consumptionStarted;
        private int wasStarted;

        public OwnedValueTaskSource(
            Func<ValueTask<TResult>> operation,
            Action<IOwnedValueTaskSource> release)
        {
            this.operation = operation ?? throw new ArgumentNullException(nameof(operation));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
            source.RunContinuationsAsynchronously = true;
        }

        public Task Completion => completion.Task;

        public ValueTask<TResult> AsValueTask() =>
            new ValueTask<TResult>(this, source.Version);

        public void Start()
        {
            if (Interlocked.Exchange(ref wasStarted, 1) != 0)
                throw new InvalidOperationException("Owned asynchronous work cannot start more than once.");

            _ = Run();
        }

        public void ThrowIfFailed() => failure?.Throw();

        public TResult GetResult(short token)
        {
            if (Interlocked.Exchange(ref consumptionStarted, 1) != 0)
                throw new InvalidOperationException(
                    "An owned asynchronous result may be consumed only once.");

            try
            {
                return source.GetResult(token);
            }
            finally
            {
                // GetResult is the ValueTask consumption boundary. Awaiter registration and work
                // completion are insufficient because both can occur before a callback returns.
                release(this);
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
                source.SetResult(await operation());
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
