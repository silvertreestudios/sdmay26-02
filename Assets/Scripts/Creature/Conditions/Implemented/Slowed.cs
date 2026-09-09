using System;
using Game.Creature.Rules;
using UnityEngine;

/// <summary>Defines one supported value of the Slowed condition.</summary>
public sealed class Slowed : Condition
{
    private readonly int value;

    /// <summary>Creates a Slowed condition definition with a value from 1 through 3.</summary>
    /// <param name="value">The number of actions the condition removes during turn-resource regain.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is outside the supported range from 1 through 3.
    /// </exception>
    public Slowed(int value)
        : base("Slowed", _ => { })
    {
        if (value is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(value));
        this.value = value;
    }

    /// <inheritdoc/>
    public override void Apply(ConditionSource source, GameObject obj)
    {
        SlowedEncounterModule.Apply(obj, value);
        base.Apply(source, obj);
    }
}
