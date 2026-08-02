using System;

namespace Game.Rules.Runtime
{
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
        private readonly FrameActionState actionState;

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
        /// Gets the operation that caused this invocation, or <see langword="null"/> when the
        /// invocation has no causal operation.
        /// </summary>
        /// <remarks>
        /// Execution nesting and causal provenance are independent. A root frame has no
        /// <see cref="ParentId"/>, but it can have a cause when a committed Fact starts a
        /// listener-dispatched causal root.
        /// </remarks>
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

        /// <summary>
        /// Gets whether this frame represents a PF2e action with a frozen lifecycle profile.
        /// </summary>
        public bool IsAction => actionState.IsAction;

        /// <summary>
        /// Gets the trusted action identity used to resolve this frame's effective profile.
        /// </summary>
        /// <exception cref="InvalidOperationException">This frame does not represent an action.</exception>
        public ActionOpInfo ActionInfo => actionState.RequireInfo();

        /// <summary>
        /// Gets the effective profile frozen before this action was validated.
        /// </summary>
        /// <exception cref="InvalidOperationException">This frame does not represent an action.</exception>
        /// <remarks>
        /// Non-action operations expose an explicit <see cref="IsAction"/> state instead of a nullable
        /// placeholder. Action validators, lifecycle middleware, and handlers therefore cannot observe
        /// a partially initialized action frame.
        /// </remarks>
        public ActionProfile ActionProfile => actionState.RequireProfile();

        internal OpFrame(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            InvocationPolicy invocationPolicy,
            TOp op,
            RulesSnapshot startSnapshot,
            FrameActionState actionState
        )
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
            this.actionState = actionState ?? throw new ArgumentNullException(nameof(actionState));
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
        private bool isExhausted;

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
            if (isExhausted)
                throw new InvalidOperationException("The operation ID sequence is exhausted.");
            long value = next;
            if (next == long.MaxValue)
                isExhausted = true;
            else
                next++;
            return new OpId(value);
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
        bool IsAction { get; }
        ActionOpInfo ActionInfo { get; }
        ActionProfile ActionProfile { get; }
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
        public bool IsAction => frame.IsAction;
        public ActionOpInfo ActionInfo => frame.ActionInfo;
        public ActionProfile ActionProfile => frame.ActionProfile;
    }
}
