using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Accumulates observer failures without allocating on the successful delivery path.
    /// </summary>
    /// <remarks>
    /// The first failure preserves its original stack when rethrown. A list is created only when
    /// a second failure requires an <see cref="AggregateException"/>.
    /// </remarks>
    internal abstract class ObserverFailureState
    {
        public static ObserverFailureState CreateEmpty(string aggregateMessage) =>
            new EmptyFailureState(aggregateMessage);

        public abstract ObserverFailureState Add(Exception exception);
        public abstract void ThrowIfAny();

        private sealed class EmptyFailureState : ObserverFailureState
        {
            private readonly string aggregateMessage;

            public EmptyFailureState(string aggregateMessage)
            {
                this.aggregateMessage =
                    aggregateMessage ?? throw new ArgumentNullException(nameof(aggregateMessage));
            }

            public override ObserverFailureState Add(Exception exception) =>
                new SingleFailureState(aggregateMessage, exception);

            public override void ThrowIfAny() { }
        }

        private sealed class SingleFailureState : ObserverFailureState
        {
            private readonly string aggregateMessage;
            private readonly Exception failure;

            public SingleFailureState(string aggregateMessage, Exception failure)
            {
                this.aggregateMessage = aggregateMessage;
                this.failure = failure;
            }

            public override ObserverFailureState Add(Exception exception) =>
                new MultipleFailureState(aggregateMessage, failure, exception);

            public override void ThrowIfAny() => ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private sealed class MultipleFailureState : ObserverFailureState
        {
            private readonly string aggregateMessage;
            private readonly List<Exception> failures;

            public MultipleFailureState(string aggregateMessage, Exception first, Exception second)
            {
                this.aggregateMessage = aggregateMessage;
                failures = new List<Exception> { first, second };
            }

            public override ObserverFailureState Add(Exception exception)
            {
                failures.Add(exception);
                return this;
            }

            public override void ThrowIfAny()
            {
                throw new AggregateException(aggregateMessage, failures);
            }
        }
    }
}
