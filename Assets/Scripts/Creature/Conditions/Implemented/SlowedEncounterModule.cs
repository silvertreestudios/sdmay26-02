using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Owns only Slowed's contribution to turn-resource regain.</summary>
    internal sealed class SlowedEncounterModule : IUnityCombatantEnrollmentModule
    {
        internal static readonly ConditionId ConditionId = new("Slowed");
        internal static readonly RuleDefinitionId DefinitionId = new("slowed-turn-resources");

        internal static void DefineRuleBindings(RuleRegistryBuilder builder) =>
            builder
                .Define(DefinitionId)
                .Middleware<CalculateTurnResourcesOp, TurnResourceContribution>(
                    RuleLifecyclePhase.Transformation,
                    new TurnResourceMiddleware()
                );

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) =>
            builder.AddRuleBindings(
                new[]
                {
                    // One condition consumer per creature, not one penalty per application.
                    // No effect is associated with this always-available calculation rule.
                    new ActiveRuleBinding(
                        BindingId(builder.CreatureId),
                        DefinitionId,
                        builder.CreatureId,
                        null,
                        RuleSource.FromSlug("slowed"),
                        0
                    ),
                }
            );

        internal static BindingId BindingId(CreatureId actor) =>
            new($"slowed-binding:{actor.Value}");

        private sealed class TurnResourceMiddleware
            : IOpMiddleware<CalculateTurnResourcesOp, TurnResourceContribution>
        {
            public async ValueTask<OpResult<TurnResourceContribution>> Invoke(
                OpFrame<CalculateTurnResourcesOp> frame,
                OpMiddlewareContext context,
                OpNext<TurnResourceContribution> next
            )
            {
                OpResult<TurnResourceContribution> result = await next();
                if (
                    context.Binding.Owner != frame.Op.Turn.Actor
                    || result is not ResolvedOpResult<TurnResourceContribution> resolved
                )
                    return result;
                int value = ConditionRules.GetValue(
                    context.Snapshot,
                    frame.Op.Turn.Actor,
                    ConditionId
                );
                return OpResult<TurnResourceContribution>.Resolved(
                    new TurnResourceContribution(Math.Max(0, resolved.Value.Actions - value))
                );
            }
        }
    }
}
