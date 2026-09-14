using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Owns Slowed's identifiers, binding, and turn-resource mechanics.</summary>
    public static class SlowedRules
    {
        /// <summary>Gets the canonical slug used for Slowed rule sources.</summary>
        public const string Slug = "slowed";

        /// <summary>Gets the canonical condition identifier used by Slowed applications.</summary>
        public static ConditionId ConditionId { get; } = new("Slowed");

        /// <summary>Gets the stable definition that adjusts turn-resource regain.</summary>
        public static RuleDefinitionId DefinitionId { get; } =
            new RuleDefinitionId("slowed-turn-resources");

        /// <summary>Gets the rule source shared by Slowed applications and bindings.</summary>
        public static RuleSource Source { get; } = RuleSource.FromSlug(Slug);

        /// <summary>Defines Slowed's turn-resource middleware in the explicit rule registry.</summary>
        /// <param name="builder">The production rule registry builder.</param>
        public static void DefineRuleBinding(RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder
                .Define(DefinitionId)
                .Middleware<CalculateTurnResourcesOp, TurnResourceContribution>(
                    RuleLifecyclePhase.Transformation,
                    new TurnResourceMiddleware()
                );
        }

        /// <summary>Creates the single Slowed calculation binding for an encounter combatant.</summary>
        /// <remarks>
        /// The binding is independent of condition applications so multiple applications contribute
        /// only their highest active value through <see cref="ConditionRules.GetValue"/>.
        /// </remarks>
        /// <param name="owner">The combatant whose turn resources the binding adjusts.</param>
        /// <returns>An enabled binding with no associated active effect.</returns>
        public static ActiveRuleBinding CreateBinding(CreatureId owner) =>
            new ActiveRuleBinding(
                new BindingId($"slowed-binding:{owner.Value}"),
                DefinitionId,
                owner,
                default,
                Source,
                0
            );

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
