using System;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Owns the transitional Unity adapter for Rotting Aura turn-start resolution.</summary>
    internal sealed class RottingAuraEncounterModule : IUnityEncounterTurnStartModule
    {
        private readonly UnityCombatRulesBridge owner;

        internal RottingAuraEncounterModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        /// <inheritdoc/>
        public IEncounterTurnStartAdapter CreateTurnStartAdapter() => new TurnStartAdapter(owner);

        private sealed class TurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityCombatRulesBridge owner;

            internal TurnStartAdapter(UnityCombatRulesBridge owner) => this.owner = owner;

            /// <inheritdoc/>
            public async ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                EncounterState encounter = context.Snapshot.Encounters[context.Encounter];
                ActionController actor = owner.GetController(context.Actor);
                ActionController[] combatants = encounter
                    .Roster.Where(entry => owner.GetHealth(entry.Creature).Current > 0)
                    .Select(entry => owner.GetController(entry.Creature))
                    .ToArray();
                await CreatureAuraResolver.ApplyTurnStartAurasAwaited(
                    actor,
                    combatants,
                    owner.CurrentTiles,
                    async (target, amount, source) =>
                    {
                        CreatureId targetId = owner.GetCreatureId(target);
                        return await context.ApplyFinalDamage(
                            targetId,
                            amount,
                            owner.AllocateHealthOrigin(source),
                            source
                        );
                    },
                    target =>
                    {
                        CreatureId targetId = owner.GetCreatureId(target);
                        if (!context.Snapshot.Health.TryGet(targetId, out HealthState health))
                            throw new InvalidOperationException(
                                "An aura target has no authoritative health state."
                            );
                        return health.Current > 0;
                    },
                    result =>
                    {
                        RottingAuraRule.Present(result);
                        return default;
                    }
                );
                return current;
            }
        }
    }
}
