using System.Collections;
using UnityEngine;

public class CoroutineResult<T>
{
    public T Value;
}

public class CoroutineRunner : SingletonMonoBehaviour<CoroutineRunner>
{
    public static Coroutine Run(IEnumerator routine)
    {
        return GetInstance().StartCoroutine(routine);
    }
}