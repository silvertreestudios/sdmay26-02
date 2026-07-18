using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
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
            // The first selected middleware is the outermost wrapper. Reverse phase nesting makes
            // the returned result settle in semantic phase order, leaving Observation last so it
            // sees every transformation and reaction applied by inner middleware.
            int phase = right.Registration.Phase.CompareTo(left.Registration.Phase);
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

            CallbackWorkCoordinator work = new CallbackWorkCoordinator();
            MiddlewareContinuation<TResult> continuation =
                new MiddlewareContinuation<TResult>(next, work);
            OpMiddlewareContext context = OpMiddlewareContext.Create(
                dispatcher,
                typed.Frame.Id,
                binding,
                work);
            OpResult<TResult> result;
            try
            {
                result = await middleware.Invoke(
                    typed.Frame,
                    context,
                    continuation.Invoke);
            }
            catch (Exception callbackException)
            {
                await CallbackFailure.AwaitCleanupPreservingPrimary(
                    callbackException,
                    work.CompleteInvocation(
                        "A middleware callback completed more than once."));
                throw;
            }

            CallbackWorkCompletion completion = await work.CompleteInvocation(
                "A middleware callback completed more than once.");
            if (completion == CallbackWorkCompletion.UnconsumedMiddlewareContinuation)
            {
                throw new InvalidOperationException(
                    $"Middleware for {typeof(TOp).Name} returned before awaiting its continuation.");
            }
            if (completion == CallbackWorkCompletion.UnconsumedDispatch)
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
        private readonly Func<ValueTask<object>> next;
        private readonly CallbackWorkCoordinator work;

        public MiddlewareContinuation(
            Func<ValueTask<object>> next,
            CallbackWorkCoordinator work)
        {
            this.next = next ?? throw new ArgumentNullException(nameof(next));
            this.work = work ?? throw new ArgumentNullException(nameof(work));
        }

        public ValueTask<OpResult<TResult>> Invoke() =>
            work.StartContinuation(InvokeNext);

        private async ValueTask<OpResult<TResult>> InvokeNext()
        {
            object value = await next();
            if (!(value is OpResult<TResult> typed))
                throw new InvalidOperationException(
                    "Middleware continuation returned an impossible result type.");
            return typed;
        }
    }
}
