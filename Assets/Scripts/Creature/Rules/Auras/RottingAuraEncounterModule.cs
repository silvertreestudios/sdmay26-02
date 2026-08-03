using System;
using System.Collections.Generic;
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
                    async (target, results) =>
                    {
                        CreatureId targetId = owner.GetCreatureId(target);
                        RuleSource source = RuleSource.FromSlug(RottingAuraRule.RuleSlug);
                        List<HealthBatchChange> changes = results
                            .Select(result => new HealthBatchChange(
                                HealthBatchChangeKind.Damage,
                                targetId,
                                Math.Max(0, result.AppliedDamage),
                                owner.AllocateHealthOrigin(source),
                                source
                            ))
                            .ToList();
                        return await context.CommitFinalDamageBatchAndCompleteAdapter(
                            changes,
                            current
                        );
                    },
                    target =>
                    {
                        CreatureId targetId = owner.GetCreatureId(target);
                        if (!context.Snapshot.Health.TryGet(targetId, out HealthState health))
                            throw new InvalidOperationException(
                                "An aura target has no authoritative health state."
                            );
                        return health;
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
