using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Projects authoritative Slowed state into the transitional turn-start contribution.</summary>
    internal sealed class SlowedEncounterModule : IUnityEncounterTurnStartModule
    {
        /// <inheritdoc/>
        public IEncounterTurnStartAdapter CreateTurnStartAdapter() => new TurnStartAdapter();

        private sealed class TurnStartAdapter : IEncounterTurnStartAdapter
        {
            /// <inheritdoc/>
            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                int slowed = ConditionSelectors.TryGetSlowed(
                    context.Snapshot,
                    context.Actor,
                    out ConditionSelection<SlowedConditionState> selected
                )
                    ? selected.State.Value
                    : 0;
                return new ValueTask<TurnStartContribution>(
                    new TurnStartContribution(Math.Max(0, current.Actions - slowed))
                );
            }
        }
    }
}
