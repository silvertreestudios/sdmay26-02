using System;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using UnityEngine;

/// <summary>Defines one supported value of the Slowed condition.</summary>
public sealed class Slowed : Condition
{
    private readonly int value;

    /// <summary>Creates a Slowed condition definition with a positive value.</summary>
    /// <param name="value">The number of actions the condition removes during turn-resource regain.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is not positive.
    /// </exception>
    public Slowed(int value)
        : base("Slowed", _ => { })
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(nameof(value));
        this.value = value;
    }

    /// <inheritdoc/>
    public override void Apply(ConditionSource source, GameObject obj)
    {
        ConditionEncounterModule.Apply(
            obj,
            new ConditionState(SlowedEncounterModule.ConditionId, value),
            RuleSource.FromSlug("slowed"),
            source
        );
    }
}
