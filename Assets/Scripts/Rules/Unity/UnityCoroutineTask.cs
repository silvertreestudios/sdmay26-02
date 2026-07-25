using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Unity
{
    /// <summary>Bridges a Unity coroutine into an awaitable rules-observer boundary.</summary>
    internal static class UnityCoroutineTask
    {
        internal static ValueTask Run(IEnumerator routine)
        {
            if (routine == null)
                throw new ArgumentNullException(nameof(routine));

            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            CoroutineRunner.Run(Complete(routine, completion));
            return new ValueTask(completion.Task);
        }

        private static IEnumerator Complete(
            IEnumerator routine,
            TaskCompletionSource<bool> completion
        )
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                bool hasNext;
                object yielded = null;
                try
                {
                    hasNext = current.MoveNext();
                    if (hasNext)
                        yielded = current.Current;
                }
                catch (Exception exception)
                {
                    completion.SetException(DisposeAfterFailure(stack, exception));
                    yield break;
                }

                if (!hasNext)
                {
                    stack.Pop();
                    Exception disposalFailure = null;
                    try
                    {
                        (current as IDisposable)?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        disposalFailure = exception;
                    }
                    if (disposalFailure != null)
                    {
                        completion.SetException(DisposeAfterFailure(stack, disposalFailure));
                        yield break;
                    }
                    continue;
                }
                if (yielded is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return yielded;
            }

            completion.SetResult(true);
        }

        private static Exception DisposeAfterFailure(Stack<IEnumerator> stack, Exception original)
        {
            Exception failure = original;
            // Iterator Dispose executes pending finally blocks. Unwind every parent so projection
            // failures cannot leave animation or action lifecycle state latched on.
            while (stack.Count > 0)
            {
                try
                {
                    (stack.Pop() as IDisposable)?.Dispose();
                }
                catch (Exception disposalFailure)
                {
                    failure = new AggregateException(failure, disposalFailure);
                }
            }
            return failure;
        }
    }
}
