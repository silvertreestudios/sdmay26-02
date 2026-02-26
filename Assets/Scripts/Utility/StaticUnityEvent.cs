using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A static UnityEvent. Derive to add functionality
/// </summary>
/// <typeparam name="S">The name of the derived class</typeparam>
/// <typeparam name="T">The types of the parameters to the event</typeparam>
public class StaticUnityEvent<S, T> where S : new()
{
    /// <summary>
    /// The internal UnityEvent
    /// </summary>
    static UnityEvent<T> Event = new();

    /// <summary>
    /// Adds a listener to the event
    /// </summary>
    /// <param name="listener"></param>
    public static void AddListener(UnityAction<T> listener){ Event.AddListener(listener); }

    /// <summary>
    /// Removes a listener from the event
    /// </summary>
    /// <param name="listener"></param>
    public static void RemoveListener(UnityAction<T> listener) { Event.RemoveListener(listener); }

    /// <summary>
    /// Removes all listeners from the event
    /// </summary>
    public static void RemoveAllListeners() { Event.RemoveAllListeners(); }

    /// <summary>
    /// Invokes the Event
    /// </summary>
    /// <param name="data">passed to the listeners</param>
    public static void Invoke(T data) { Event.Invoke(data); }
}

/// <summary>
/// A static UnityEvent. Derive to add functionality
/// </summary>
/// <typeparam name="T"></typeparam>
public class StaticUnityEvent<T> where T : new()
{
    /// <summary>
    /// The internal UnityEvent
    /// </summary>
    static UnityEvent Event = new();

    /// <summary>
    /// Adds a listener to the event
    /// </summary>
    /// <param name="listener"></param>
    public static void AddListener(UnityAction listener) { Event.AddListener(listener); }

    /// <summary>
    /// Removes a listener from the event
    /// </summary>
    /// <param name="listener"></param>
    public static void RemoveListener(UnityAction listener) { Event.RemoveListener(listener); }

    /// <summary>
    /// Removes all listeners from the event
    /// </summary>
    public static void RemoveAllListeners() { Event.RemoveAllListeners(); }

    /// <summary>
    /// Invokes the Event
    /// </summary>
    public static void Invoke() { Event.Invoke(); }
}