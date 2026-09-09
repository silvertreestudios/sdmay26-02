using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Owns the legacy Unity-backed Slowed contribution to turn resource regain.</summary>
    internal sealed class SlowedEncounterModule : IUnityCombatantEnrollmentModule
    {
        internal static readonly RuleDefinitionId DefinitionId = new("slowed-turn-resources");
        private static readonly RuleSource Source = RuleSource.FromSlug("slowed");

        internal static void DefineRuleBindings(
            RuleRegistryBuilder builder,
            UnityCombatRulesBridge owner
        ) =>
            builder
                .Define(DefinitionId)
                .Middleware<CalculateTurnResourcesOp, TurnResourceContribution>(
                    RuleLifecyclePhase.Transformation,
                    new TurnResourceMiddleware(owner)
                );

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) =>
            builder.AddRuleBindings(
                new[]
                {
                    new ActiveRuleBinding(
                        new BindingId($"slowed-turn-resources:{builder.CreatureId.Value}"),
                        DefinitionId,
                        builder.CreatureId,
                        default,
                        Source,
                        0
                    ),
                }
            );

        private sealed class TurnResourceMiddleware
            : IOpMiddleware<CalculateTurnResourcesOp, TurnResourceContribution>
        {
            private readonly UnityCombatRulesBridge owner;

            internal TurnResourceMiddleware(UnityCombatRulesBridge owner) => this.owner = owner;

            /// <inheritdoc/>
            public async ValueTask<OpResult<TurnResourceContribution>> Invoke(
                OpFrame<CalculateTurnResourcesOp> frame,
                OpMiddlewareContext context,
                OpNext<TurnResourceContribution> next
            )
            {
                OpResult<TurnResourceContribution> result = await next();
                if (
                    context.Binding.Owner != frame.Op.Turn.Actor
                    || result is not ResolvedOpResult<TurnResourceContribution>
                )
                    return result;
                return OpResult<TurnResourceContribution>.Resolved(
                    new TurnResourceContribution(
                        checked(
                            (int)
                                owner.GetController(frame.Op.Turn.Actor).CalculateTurnStartActions()
                        )
                    )
                );
            }
        }
    }
}
