using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A event that resolves Coroutine listeners. Derive to add functionality
/// </summary>
/// <typeparam name="T">The types of the parameters to the event</typeparam>
public class EventCoroutine<T>
{
    // Type for listeners
    public delegate IEnumerator Listener(T data);

    // Listener list
    protected List<Listener> Listeners = new();

    /// <summary>
    /// Adds a listener to the event
    /// </summary>
    /// <param name="listener"></param>
    public void AddListener(Listener listener)
    {
        Listeners.Add(listener);
    }

    /// <summary>
    /// Removes a listener from the event
    /// </summary>
    /// <param name="listener"></param>
    public void RemoveListener(Listener listener)
    {
        Listeners.Remove(listener);
    }

    /// <summary>
    /// Removes all listeners from the event
    /// </summary>
    public void RemoveAllListeners()
    {
        Listeners.Clear();
    }

    /// <summary>
    /// Invokes the Event
    /// </summary>
    /// <param name="data">passed to the listeners</param>
    public IEnumerator Invoke(T data)
    {
        foreach (Listener l in Listeners)
            yield return l(data);
    }
}

/// <summary>
/// A event that resolves Coroutine listeners. Derive to add functionality
/// </summary>
public class EventCoroutine
{
    // Type for listeners
    public delegate IEnumerator Listener();

    // Listener list
    protected List<Listener> Listeners = new();

    /// <summary>
    /// Adds a listener to the event
    /// </summary>
    /// <param name="listener"></param>
    public void AddListener(Listener listener)
    {
        Listeners.Add(listener);
    }

    /// <summary>
    /// Removes a listener from the event
    /// </summary>
    /// <param name="listener"></param>
    public void RemoveListener(Listener listener)
    {
        Listeners.Remove(listener);
    }

    /// <summary>
    /// Removes all listeners from the event
    /// </summary>
    public void RemoveAllListeners()
    {
        Listeners.Clear();
    }

    /// <summary>
    /// Invokes the Event
    /// </summary>
    public IEnumerator Invoke()
    {
        foreach (Listener l in Listeners)
            yield return l();
    }
}
