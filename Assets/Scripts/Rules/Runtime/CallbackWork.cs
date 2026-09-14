using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Game.Rules.Runtime
{
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
    /// Preserves a callback failure when closing its scope also discovers failed unconsumed work.
    /// </summary>
    internal static class CallbackFailure
    {
        /// <summary>
        /// Waits for callback-owned cleanup without allowing its failure to hide the callback's
        /// original exception.
        /// </summary>
        /// <typeparam name="TResult">The cleanup operation's result type.</typeparam>
        /// <param name="callbackException">The exception thrown by the callback itself.</param>
        /// <param name="cleanup">The cleanup operation that settles any unconsumed callback work.</param>
        /// <returns>A task that completes after cleanup succeeds.</returns>
        /// <exception cref="AggregateException">
        /// Thrown when cleanup also fails. The callback exception is first and the cleanup exception
        /// is second so callers receive a stable primary-failure ordering.
        /// </exception>
        internal static async ValueTask AwaitCleanupPreservingPrimary<TResult>(
            Exception callbackException,
            ValueTask<TResult> cleanup
        )
        {
            try
            {
                await cleanup;
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Callback execution and cleanup of its unconsumed work both failed.",
                    callbackException,
                    cleanupException
                );
            }
        }
    }

    /// <summary>
    /// Identifies which callback operation owns a <see cref="CallbackWorkCoordinator"/>.
    /// </summary>
    internal enum CallbackWorkKind
    {
        Dispatch,
        MiddlewareContinuation,
    }

    /// <summary>
    /// Describes whether a callback returned with work that it had not consumed.
    /// </summary>
    internal enum CallbackWorkCompletion
    {
        NoUnconsumedWork,
        UnconsumedDispatch,
        UnconsumedMiddlewareContinuation,
    }

    /// <summary>
    /// Owns one callback's lifetime and its single in-flight asynchronous work slot.
    /// </summary>
    /// <remarks>
    /// Middleware shares one instance between its continuation and binding-scoped
    /// <see cref="OpMiddlewareContext"/>. Acquiring either kind of work is therefore atomic, and a rejected
    /// overlap cannot start or replace the work already in progress. Ownership ends only when the
    /// returned <see cref="ValueTask{TResult}"/> consumes its result; mere operation completion does
    /// not permit another dispatch or continuation. Rejecting a first continuation attempt while
    /// a child owns the slot does not consume the callback's one-continuation allowance.
    /// </remarks>
    internal sealed class CallbackWorkCoordinator
    {
        private readonly object gate = new object();
        private bool continuationWasInvoked;
        private WorkState state = IdleWorkState.Instance;

        private abstract class WorkState { }

        private sealed class IdleWorkState : WorkState
        {
            internal static readonly IdleWorkState Instance = new IdleWorkState();

            private IdleWorkState() { }
        }

        private sealed class RunningWorkState : WorkState
        {
            internal RunningWorkState(CallbackWorkKind kind, IOwnedValueTaskSource work)
            {
                Kind = kind;
                Work = work ?? throw new ArgumentNullException(nameof(work));
            }

            internal CallbackWorkKind Kind { get; }
            internal IOwnedValueTaskSource Work { get; }
        }

        private sealed class CompletedWorkState : WorkState
        {
            internal static readonly CompletedWorkState Instance = new CompletedWorkState();

            private CompletedWorkState() { }
        }

        internal ValueTask<TResult> StartDispatch<TResult>(
            Func<ValueTask<TResult>> operation,
            string inactiveMessage,
            string dispatchOverlapMessage,
            string continuationOverlapMessage
        ) => Start(operation, inactiveMessage, dispatchOverlapMessage, continuationOverlapMessage);

        internal ValueTask<TResult> StartContinuation<TResult>(Func<ValueTask<TResult>> operation)
        {
            OwnedValueTaskSource<TResult> invocation;
            lock (gate)
            {
                if (state is CompletedWorkState)
                {
                    throw new InvalidOperationException(
                        "Middleware cannot continue after its callback returns."
                    );
                }
                if (continuationWasInvoked)
                {
                    throw new InvalidOperationException(
                        "Middleware may invoke its continuation at most once."
                    );
                }
                if (state is RunningWorkState)
                {
                    throw new InvalidOperationException(
                        "Middleware cannot invoke its continuation while a child dispatch is active. "
                            + "Await the active child before continuing."
                    );
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
                if (state is CompletedWorkState)
                    throw new InvalidOperationException(inactiveMessage);
            }
        }

        internal async ValueTask<CallbackWorkCompletion> CompleteInvocation(
            string duplicateCompletionMessage
        )
        {
            RunningWorkState pending;
            lock (gate)
            {
                if (state is CompletedWorkState)
                    throw new InvalidOperationException(duplicateCompletionMessage);
                if (state is IdleWorkState)
                {
                    state = CompletedWorkState.Instance;
                    return CallbackWorkCompletion.NoUnconsumedWork;
                }

                pending = (RunningWorkState)state;
                state = CompletedWorkState.Instance;
            }

            // The active slot is cleared only by ValueTask.GetResult. Capturing it while closing
            // the callback therefore records a contract violation even when ignored work already
            // completed synchronously or a retained result is consumed after the callback returns.
            await pending.Work.Completion;
            pending.Work.ThrowIfFailed();
            return pending.Kind == CallbackWorkKind.MiddlewareContinuation
                ? CallbackWorkCompletion.UnconsumedMiddlewareContinuation
                : CallbackWorkCompletion.UnconsumedDispatch;
        }

        private ValueTask<TResult> Start<TResult>(
            Func<ValueTask<TResult>> operation,
            string inactiveMessage,
            string dispatchOverlapMessage,
            string continuationOverlapMessage
        )
        {
            OwnedValueTaskSource<TResult> owned;
            lock (gate)
            {
                if (state is CompletedWorkState)
                    throw new InvalidOperationException(inactiveMessage);
                if (state is RunningWorkState running)
                {
                    throw new InvalidOperationException(
                        running.Kind == CallbackWorkKind.MiddlewareContinuation
                            ? continuationOverlapMessage
                            : dispatchOverlapMessage
                    );
                }

                owned = Own(CallbackWorkKind.Dispatch, operation);
            }

            owned.Start();
            return owned.AsValueTask();
        }

        private OwnedValueTaskSource<TResult> Own<TResult>(
            CallbackWorkKind kind,
            Func<ValueTask<TResult>> operation
        )
        {
            OwnedValueTaskSource<TResult> owned = new OwnedValueTaskSource<TResult>(
                operation,
                Release
            );
            state = new RunningWorkState(kind, owned);
            return owned;
        }

        private void Release(IOwnedValueTaskSource work)
        {
            lock (gate)
            {
                if (state is RunningWorkState running && ReferenceEquals(running.Work, work))
                {
                    state = IdleWorkState.Instance;
                }
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
    internal sealed class OwnedValueTaskSource<TResult>
        : IOwnedValueTaskSource,
            IValueTaskSource<TResult>
    {
        private readonly Func<ValueTask<TResult>> operation;
        private readonly Action<IOwnedValueTaskSource> release;
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private ManualResetValueTaskSourceCore<TResult> source;
        private ExceptionDispatchInfo failure;
        private int consumptionStarted;
        private int wasStarted;

        public OwnedValueTaskSource(
            Func<ValueTask<TResult>> operation,
            Action<IOwnedValueTaskSource> release
        )
        {
            this.operation = operation ?? throw new ArgumentNullException(nameof(operation));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
            source.RunContinuationsAsynchronously = true;
        }

        public Task Completion => completion.Task;

        public ValueTask<TResult> AsValueTask() => new ValueTask<TResult>(this, source.Version);

        public void Start()
        {
            if (Interlocked.Exchange(ref wasStarted, 1) != 0)
                throw new InvalidOperationException(
                    "Owned asynchronous work cannot start more than once."
                );

            _ = Run();
        }

        public void ThrowIfFailed() => failure?.Throw();

        public TResult GetResult(short token)
        {
            if (Interlocked.Exchange(ref consumptionStarted, 1) != 0)
                throw new InvalidOperationException(
                    "An owned asynchronous result may be consumed only once."
                );

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

        public ValueTaskSourceStatus GetStatus(short token) => source.GetStatus(token);

        public void OnCompleted(
            Action<object> continuation,
            object state,
            short token,
            ValueTaskSourceOnCompletedFlags flags
        ) => source.OnCompleted(continuation, state, token, flags);

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
}
