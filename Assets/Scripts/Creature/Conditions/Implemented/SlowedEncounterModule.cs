using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Owns the transitional Unity action contribution for Slowed at turn start.</summary>
    internal sealed class SlowedEncounterModule : IUnityEncounterTurnStartModule
    {
        private readonly UnityCombatRulesBridge owner;

        internal SlowedEncounterModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        /// <inheritdoc/>
        public IEncounterTurnStartAdapter CreateTurnStartAdapter() => new TurnStartAdapter(owner);

        private sealed class TurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityCombatRulesBridge owner;

            internal TurnStartAdapter(UnityCombatRulesBridge owner) => this.owner = owner;

            /// <inheritdoc/>
            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            ) =>
                new(
                    new TurnStartContribution(
                        checked((int)owner.GetController(context.Actor).CalculateTurnStartActions())
                    )
                );
        }
    }
}
