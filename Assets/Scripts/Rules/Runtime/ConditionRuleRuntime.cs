using System;

namespace Game.Rules.Runtime
{
    internal static class ConditionRuleDispatcherExtensions
    {
        private static readonly RuleSource LifecycleSource = RuleSource.FromSlug(
            "condition-lifecycle"
        );

        internal static RuleDispatcherBuilder UseConditionRules(
            this RuleDispatcherBuilder builder,
            RuleRegistry registry
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            return builder
                .RegisterReducer<CreateConditionOp, ConditionCreationOutcome>(
                    new CreateConditionReducer(registry),
                    LifecycleSource
                )
                .RegisterReducer<UpdateConditionStateOp, ConditionStateUpdateOutcome>(
                    new UpdateConditionStateReducer(),
                    LifecycleSource
                )
                .RegisterReducer<ExpireConditionOp, ConditionExpirationOutcome>(
                    new ExpireConditionReducer(),
                    LifecycleSource
                )
                .RegisterReducer<RemoveConditionOp, ConditionRemovalOutcome>(
                    new RemoveConditionReducer(),
                    LifecycleSource
                );
        }
    }
}
