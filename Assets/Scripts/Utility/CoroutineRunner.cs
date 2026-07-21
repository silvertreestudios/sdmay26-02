using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using UnityEngine;

public class CoroutineResult<T>
{
    public T Value;
}

public class CoroutineRunner : SingletonMonoBehaviour<CoroutineRunner>
{
    /// <summary>Yields until an awaited rules mutation has completely settled.</summary>
    /// <param name="operation">The owned task-like operation to observe.</param>
    public static IEnumerator Await(ValueTask operation)
    {
        Task task = operation.AsTask();
        while (!task.IsCompleted)
            yield return null;
        ThrowIfFailed(task);
    }

    /// <summary>Yields until an awaited operation settles and captures its result.</summary>
    /// <typeparam name="T">The completed result type.</typeparam>
    /// <param name="operation">The owned task-like operation to observe.</param>
    /// <param name="result">The result holder populated before this coroutine completes.</param>
    public static IEnumerator Await<T>(ValueTask<T> operation, CoroutineResult<T> result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        Task task = Capture(operation, result);
        while (!task.IsCompleted)
            yield return null;
        ThrowIfFailed(task);
    }

    /// <summary>Yields until an awaited operation settles when its result is not needed.</summary>
    /// <typeparam name="T">The completed result type.</typeparam>
    /// <param name="operation">The owned task-like operation to observe.</param>
    public static IEnumerator Await<T>(ValueTask<T> operation)
    {
        Task task = Capture(operation);
        while (!task.IsCompleted)
            yield return null;
        ThrowIfFailed(task);
    }

    public static Coroutine Run(IEnumerator routine)
    {
        return GetInstance().StartCoroutine(routine);
    }

    public static Coroutine Run<T>(Func<T, IEnumerator> routine, T data)
    {
        return GetInstance().StartCoroutine(routine(data));
    }

    private static async Task Capture<T>(ValueTask<T> operation, CoroutineResult<T> result) =>
        result.Value = await operation;

    private static async Task Capture<T>(ValueTask<T> operation) => await operation;

    private static void ThrowIfFailed(Task task)
    {
        if (task.IsCanceled)
            throw new TaskCanceledException(task);
        if (!task.IsFaulted)
            return;
        Exception failure = task.Exception.InnerException ?? task.Exception;
        ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
