using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A static UnityEvent. Derive to add functionality
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class StaticUnityEvent<T>
{
    /// <summary>
    /// The internal UnityEvent
    /// </summary>
    static UnityEvent<T> Event;

    /// <summary>
    /// Adds a listener to the event
    /// </summary>
    /// <param name="listener"></param>
    public void AddListener(UnityAction<T> listener){ Event.AddListener(listener); }

    /// <summary>
    /// Removes a listener from the event
    /// </summary>
    /// <param name="listener"></param>
    public void RemoveListener(UnityAction<T> listener) { Event.RemoveListener(listener); }

    /// <summary>
    /// Removes all listeners from the event
    /// </summary>
    public void RemoveAllListeners() { Event.RemoveAllListeners(); }

    /// <summary>
    /// Invokes the Event
    /// </summary>
    /// <param name="data">passed to the listeners</param>
    public void Invoke(T data) { Event.Invoke(data); }
}