using System;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Owns the legacy Unity-backed Rotting Aura response to committed turn entry.</summary>
    internal sealed class RottingAuraEncounterModule : IUnityCombatantEnrollmentModule
    {
        internal static readonly RuleDefinitionId DefinitionId = new("rotting-aura-turn-entry");
        private static readonly RuleSource Source = RuleSource.FromSlug("rotting-aura");

        internal static void DefineRuleBindings(
            RuleRegistryBuilder builder,
            UnityCombatRulesBridge owner
        ) =>
            builder
                .Define(DefinitionId)
                .FactListener(RuleLifecyclePhase.Reaction, new TurnBeganListener(owner));

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) =>
            builder.AddRuleBindings(
                new[]
                {
                    new ActiveRuleBinding(
                        new BindingId($"rotting-aura-turn-entry:{builder.CreatureId.Value}"),
                        DefinitionId,
                        builder.CreatureId,
                        default,
                        Source,
                        0
                    ),
                }
            );

        private sealed class TurnBeganListener : IRuleFactListener<TurnBeganFact>
        {
            private readonly UnityCombatRulesBridge owner;

            internal TurnBeganListener(UnityCombatRulesBridge owner) => this.owner = owner;

            /// <inheritdoc/>
            public async ValueTask OnFactCommitted(TurnBeganFact fact, FactContext context)
            {
                if (context.Binding.Owner != fact.Turn.Actor)
                    return;
                EncounterState encounter = context.Snapshot.Encounters[fact.Turn.Encounter];
                if (!encounter.CurrentTurn.HasValue || encounter.CurrentTurn.Value != fact.Turn)
                    return;
                ActionController actor = owner.GetController(fact.Turn.Actor);
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
                        if (targetId != fact.Turn.Actor)
                            throw new InvalidOperationException(
                                "A turn-entry aura may damage only the acting creature."
                            );
                        OpResult<DamageOutcome> damage = await context.Dispatch(
                            new ApplyDamageOp(
                                targetId,
                                amount,
                                owner.AllocateHealthOrigin(source),
                                source
                            )
                        );
                        if (damage is ResolvedOpResult<DamageOutcome> resolved)
                            return resolved.Value;
                        throw new InvalidOperationException(
                            "Turn-entry aura damage did not resolve."
                        );
                    },
                    target =>
                    {
                        CreatureId targetId = owner.GetCreatureId(target);
                        if (!context.Snapshot.Health.TryGet(targetId, out HealthState health))
                            throw new InvalidOperationException(
                                "An aura target has no authoritative health state."
                            );
                        if (
                            !context.Snapshot.Encounters.TryGet(
                                fact.Turn.Encounter,
                                out EncounterState current
                            )
                            || current.Phase != EncounterPhase.Active
                            || !current.CurrentTurn.HasValue
                            || current.CurrentTurn.Value != fact.Turn
                        )
                            return false;
                        return health.Current > 0;
                    },
                    result =>
                    {
                        RottingAuraRule.Present(result);
                        return default;
                    }
                );
            }
        }
    }
}
