using System;

namespace Game.Rules.Runtime
{
    /// <summary>Composes condition workflows over the shared active-effect lifecycle.</summary>
    public static class ConditionRuleDispatcherExtensions
    {
        private static readonly RuleSource LifecycleSource = RuleSource.FromSlug(
            "condition-lifecycle"
        );

        /// <summary>Adds condition application, lifecycle, cleanup, and enrollment workflows.</summary>
        public static RuleDispatcherBuilder UseConditionRules(
            this RuleDispatcherBuilder builder,
            RuleRegistry registry
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            return builder
                .RegisterHandler<ApplyConditionOp, ConditionCreationOutcome>(
                    new ApplyConditionHandler()
                )
                .RegisterReducer<AdoptConditionRegistrationsOp, ConditionAdoptionOutcome>(
                    new AdoptConditionRegistrationsReducer(registry),
                    LifecycleSource,
                    InvocationPolicy.ExternalAllowed
                )
                .RegisterReducer<CleanupConditionsFromSourceOp, ConditionCleanupOutcome>(
                    new CleanupConditionsFromSourceReducer(),
                    LifecycleSource,
                    InvocationPolicy.ExternalAllowed
                )
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
