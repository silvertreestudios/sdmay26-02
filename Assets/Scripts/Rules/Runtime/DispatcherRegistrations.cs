using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Declares whether rule middleware may wrap a resolver registration.
    /// </summary>
    /// <remarks>
    /// This is registration metadata rather than operation-type knowledge. The rule registry
    /// rejects middleware that targets a disabled resolver so configured extensions cannot be
    /// silently skipped or bypass a mandatory engine transaction.
    /// </remarks>
    internal enum ResolverMiddlewarePolicy
    {
        /// <summary>
        /// Active rule middleware participates in normal phase order.
        /// </summary>
        Enabled,

        /// <summary>
        /// The resolver must execute without operation middleware.
        /// </summary>
        Disabled,
    }

    internal interface IRegistration
    {
        Type OpType { get; }
        Type ResultType { get; }
        InvocationPolicy Policy { get; }
        ResolverMiddlewarePolicy MiddlewarePolicy { get; }
        bool IsReducer { get; }
        IFrameInvocation CreateInvocation(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            IRuleOp op,
            RulesSnapshot snapshot,
            FrameActionState actionState
        );
        ValueTask<object> Invoke(IFrameInvocation invocation, RuleDispatcher dispatcher);
        object CreateInvalidResult(string reason);
        object CreateInterruptedResult();
        OpStatus GetResultStatus(object result);
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
        private static readonly IReadOnlyList<RuleFact> NoDirectFacts = Array.AsReadOnly(
            Array.Empty<RuleFact>()
        );
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
                throw new InvalidOperationException(
                    "A resolver captured its direct Facts more than once."
                );
            directFacts = facts ?? throw new ArgumentNullException(nameof(facts));
        }
    }

    internal abstract class Registration<TOp, TResult> : IRegistration
        where TOp : IRuleOp<TResult>
    {
        protected Registration(InvocationPolicy policy, ResolverMiddlewarePolicy middlewarePolicy)
        {
            Policy = policy;
            MiddlewarePolicy = middlewarePolicy;
        }

        public Type OpType => typeof(TOp);
        public Type ResultType => typeof(TResult);
        public InvocationPolicy Policy { get; }
        public ResolverMiddlewarePolicy MiddlewarePolicy { get; }
        public abstract bool IsReducer { get; }

        public IFrameInvocation CreateInvocation(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            IRuleOp op,
            RulesSnapshot snapshot,
            FrameActionState actionState
        )
        {
            if (!(op is TOp typed))
                throw new InvalidOperationException(
                    $"Registration for {typeof(TOp).Name} received {op.GetType().Name}."
                );
            return new FrameInvocation<TOp>(
                new OpFrame<TOp>(
                    id,
                    rootId,
                    parentId,
                    causeId,
                    Policy,
                    typed,
                    snapshot,
                    actionState
                )
            );
        }

        public object CreateInvalidResult(string reason) => OpResult<TResult>.Invalid(reason);

        public object CreateInterruptedResult() => OpResult<TResult>.Interrupted();

        public OpStatus GetResultStatus(object result)
        {
            if (result is OpResult<TResult> typed)
                return typed.Status;
            throw new InvalidOperationException(
                $"Resolver for {typeof(TOp).Name} returned an impossible result type."
            );
        }

        public abstract ValueTask<object> Invoke(
            IFrameInvocation invocation,
            RuleDispatcher dispatcher
        );

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
            : base(policy, ResolverMiddlewarePolicy.Enabled) => this.handler = handler;

        public override bool IsReducer => false;

        public override async ValueTask<object> Invoke(
            IFrameInvocation invocation,
            RuleDispatcher dispatcher
        )
        {
            OpFrame<TOp> frame = GetFrame(invocation);
            OpHandlerContext context = OpHandlerContext.Create(dispatcher, frame.Id);
            TResult value;
            try
            {
                value = await handler.Handle(frame, context);
            }
            catch (Exception callbackException)
            {
                await CallbackFailure.AwaitCleanupPreservingPrimary(
                    callbackException,
                    context.CompleteInvocation()
                );
                throw;
            }

            if (await context.CompleteInvocation() == CallbackWorkCompletion.UnconsumedDispatch)
            {
                throw new InvalidOperationException(
                    $"Operation {frame.Id.Value} returned before awaiting its active child dispatch."
                );
            }
            return OpResult<TResult>.Resolved(value);
        }
    }

    internal sealed class ReducerRegistration<TOp, TResult> : Registration<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        private readonly IOpReducer<TOp, TResult> reducer;
        private readonly RuleSource source;

        public ReducerRegistration(
            IOpReducer<TOp, TResult> reducer,
            RuleSource source,
            InvocationPolicy policy = InvocationPolicy.NestedOnly,
            ResolverMiddlewarePolicy middlewarePolicy = ResolverMiddlewarePolicy.Enabled
        )
            : base(policy, middlewarePolicy)
        {
            this.reducer = reducer;
            this.source = source;
        }

        public override bool IsReducer => true;

        public override async ValueTask<object> Invoke(
            IFrameInvocation invocation,
            RuleDispatcher dispatcher
        )
        {
            OpFrame<TOp> frame = GetFrame(invocation);
            RuleSource factSource = frame.Op is IRuleSourcedOp sourced ? sourced.Source : source;
            if (factSource.IsEmpty)
                throw new InvalidOperationException(
                    $"Reducer operation {typeof(TOp).Name} supplied an empty rule source."
                );
            ReductionResult<TResult> reduced = dispatcher.Reduce(frame, reducer, factSource);
            dispatcher.CaptureCommittedFacts(invocation, reduced.Facts);
            if (reduced.Facts.Count > 0)
            {
                await dispatcher.NotifyFactObservers(reduced.Facts, reduced.Snapshot);
            }
            OpResult<TResult> result = reduced.IsAccepted
                ? OpResult<TResult>.Resolved(reduced.Value)
                : OpResult<TResult>.Invalid(reduced.RejectionReason);
            return result.WithFacts(reduced.Facts);
        }
    }
}
