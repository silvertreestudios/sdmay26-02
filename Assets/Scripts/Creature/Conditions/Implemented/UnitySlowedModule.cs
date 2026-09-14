using System;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>Owns Slowed's Unity combatant enrollment.</summary>
    internal sealed class UnitySlowedModule : IUnityCombatantEnrollmentModule
    {
        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) =>
            builder.AddRuleBindings(new[] { SlowedRules.CreateBinding(builder.CreatureId) });
    }
}

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
            new ConditionState(SlowedRules.ConditionId, value),
            SlowedRules.Source,
            source
        );
    }
}
