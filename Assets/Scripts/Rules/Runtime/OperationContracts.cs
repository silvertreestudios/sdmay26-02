using System.Threading.Tasks;

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
        ValueTask<TResult> Handle(OpFrame<TOp> frame, OpHandlerContext context);
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
        /// The operation may be dispatched only from an active <see cref="OpCallbackContext"/>.
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
}
