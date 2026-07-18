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
    /// Provides the state, trace, and nested-dispatch services shared by operation callbacks.
    /// </summary>
    /// <remarks>
    /// The dispatcher owns each concrete context. It is valid only while the callback that received
    /// it is actively executing. Each callback may have at most one child dispatch in flight and
    /// must await that child before returning or starting another child. Middleware child dispatch
    /// and continuation work share the same slot and must be awaited sequentially. If a callback
    /// fails while unconsumed work also fails during cleanup, dispatch reports both failures in an
    /// <see cref="AggregateException"/>, with the callback failure first.
    /// </remarks>
    public abstract class OpCallbackContext
    {
        private readonly RuleDispatcher dispatcher;
        private readonly OpId parentId;
        private readonly CallbackWorkCoordinator work;

        internal OpCallbackContext(
            RuleDispatcher dispatcher,
            OpId parentId,
            CallbackWorkCoordinator work)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.parentId = parentId;
            this.work = work ?? throw new ArgumentNullException(nameof(work));
        }

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

            return work.StartDispatch(
                () => dispatcher.DispatchNested(op, parentId),
                "An operation context is not actively executing after its callback returns.",
                $"Operation {parentId.Value} cannot begin an overlapping child dispatch. " +
                "Await the active child before dispatching another.",
                $"Operation {parentId.Value} cannot begin an overlapping child dispatch while " +
                "its middleware continuation is active. Await the continuation before " +
                "dispatching a child.");
        }

        internal ValueTask<CallbackWorkCompletion> CompleteInvocation() =>
            work.CompleteInvocation("An operation context completed more than once.");

        internal void RequireActive() => work.RequireActive(
            "An operation context cannot be used after its callback returns.");
    }

    /// <summary>
    /// Provides handler-scoped access to rules state, trace data, and nested dispatch.
    /// </summary>
    /// <remarks>
    /// Handler contexts carry no active-rule authority. The dispatcher creates one context for each
    /// handler invocation and closes it when that handler returns.
    /// </remarks>
    public sealed class OpHandlerContext : OpCallbackContext
    {
        private OpHandlerContext(
            RuleDispatcher dispatcher,
            OpId parentId,
            CallbackWorkCoordinator work)
            : base(dispatcher, parentId, work)
        {
        }

        internal static OpHandlerContext Create(RuleDispatcher dispatcher, OpId parentId) =>
            new OpHandlerContext(dispatcher, parentId, new CallbackWorkCoordinator());
    }

    /// <summary>
    /// Provides middleware-scoped rules services and the active binding authorizing the invocation.
    /// </summary>
    /// <remarks>
    /// The binding is required when the context is constructed, so middleware-only authority cannot
    /// be represented by an unbound context. Child dispatch and the middleware continuation share
    /// one callback-work slot and must be consumed sequentially.
    /// </remarks>
    public sealed class OpMiddlewareContext : OpCallbackContext
    {
        private readonly ActiveRuleBinding binding;

        private OpMiddlewareContext(
            RuleDispatcher dispatcher,
            OpId parentId,
            ActiveRuleBinding binding,
            CallbackWorkCoordinator work)
            : base(dispatcher, parentId, work)
        {
            this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        /// <summary>
        /// Gets the active binding authorizing the current middleware invocation.
        /// </summary>
        public ActiveRuleBinding Binding
        {
            get
            {
                RequireActive();
                return binding;
            }
        }

        /// <summary>
        /// Gets the stable rule source associated with <see cref="Binding"/>.
        /// </summary>
        public RuleSource Source => Binding.Source;

        internal static OpMiddlewareContext Create(
            RuleDispatcher dispatcher,
            OpId parentId,
            ActiveRuleBinding binding,
            CallbackWorkCoordinator work) =>
            new OpMiddlewareContext(dispatcher, parentId, binding, work);
    }
}
