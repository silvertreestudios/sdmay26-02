using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A static event that resolves Coroutine listeners. Derive to add functionality
/// </summary>
/// <typeparam name="S">The name of the derived class</typeparam>
/// <typeparam name="T">The types of the parameters to the event</typeparam>
public class StaticEventCoroutine<S, T> where S : new()
{
    // Type for listeners
    public delegate IEnumerator Listener(T data);

    // Listener list
    protected static List<Listener> Listeners = new();

    /// <summary>
    /// Adds a listener to the event
    /// </summary>
    /// <param name="listener"></param>
    public static void AddListener(Listener listener){ Listeners.Add(listener); }

    /// <summary>
    /// Removes a listener from the event
    /// </summary>
    /// <param name="listener"></param>
    public static void RemoveListener(Listener listener) { Listeners.Remove(listener); }

    /// <summary>
    /// Removes all listeners from the event
    /// </summary>
    public static void RemoveAllListeners() { Listeners.Clear(); }

    /// <summary>
    /// Invokes the Event
    /// </summary>
    /// <param name="data">passed to the listeners</param>
    public static IEnumerator Invoke(T data) { foreach(Listener l in Listeners) yield return l(data); }
}

/// <summary>
/// A static event that resolves Coroutine listeners. Derive to add functionality
/// </summary>
/// <typeparam name="T"></typeparam>
public class StaticEventCoroutine<T> where T : new()
{
    // Type for listeners
    public delegate IEnumerator Listener();

    // Listener list
    protected static List<Listener> Listeners = new();

    /// <summary>
    /// Adds a listener to the event
    /// </summary>
    /// <param name="listener"></param>
    public static void AddListener(Listener listener) { Listeners.Add(listener); }

    /// <summary>
    /// Removes a listener from the event
    /// </summary>
    /// <param name="listener"></param>
    public static void RemoveListener(Listener listener) { Listeners.Remove(listener); }

    /// <summary>
    /// Removes all listeners from the event
    /// </summary>
    public static void RemoveAllListeners() { Listeners.Clear(); }

    /// <summary>
    /// Invokes the Event
    /// </summary>
    public static IEnumerator Invoke() { foreach(Listener l in Listeners) yield return l(); }
}