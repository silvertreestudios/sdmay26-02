using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>Seeds independent indefinite applications and projects them for later enrollment.</summary>
    /// <remarks>
    /// Rules never read this component while attached. Finite effects belong to encounter timing,
    /// not this between-encounter seed; restoring timed conditions from saves is not supported.
    /// </remarks>
    internal sealed class ConditionSeed : MonoBehaviour
    {
        private readonly List<(ConditionState State, RuleSource Source)> applications = new();

        internal IReadOnlyList<(ConditionState State, RuleSource Source)> Applications =>
            applications;

        internal void ApplyBeforeAttachment(ConditionState state, RuleSource source) =>
            applications.Add((state, source));

        internal void Project(IEnumerable<ActiveEffectInstance> effects)
        {
            applications.Clear();
            applications.AddRange(
                effects
                    .Where(effect => effect.Duration.Kind == EffectDurationKind.Indefinite)
                    .Select(effect => (effect.GetState<ConditionState>(), effect.Source))
            );
        }
    }
}
