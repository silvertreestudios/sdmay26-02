using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Ref<T>
{
    public T Value;

    public Ref(T value)
    {
        Value = value;
    }
}

/// <summary>Triggered upon reseting action points</summary>
public class OnResetActionPoints : UnityEvent<Ref<uint>> {}

/// <summary>Triggered upon actions retrieval</summary>
public class OnGetActions : UnityEvent<List<EntityAction>> {}

/// <summary>Triggered upon movements retrieval</summary>
public class OnGetMovements : UnityEvent<List<EntityAction>> {}

/// <summary>Triggered upon reactions retrieval</summary>
public class OnGetReactions : UnityEvent<List<EntityAction>> {}
