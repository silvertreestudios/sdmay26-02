using System;

namespace Game.Rules.Runtime
{
    /// <summary>Composes condition workflows over the shared active-effect lifecycle.</summary>
    public static class ConditionRuleDispatcherExtensions
    {
        private static readonly RuleSource LifecycleSource = RuleSource.FromSlug(
            "condition-lifecycle"
        );

        /// <summary>Adds condition application and source-cleanup workflows.</summary>
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
                .RegisterReducer<ApplyConditionOp, ConditionApplicationOutcome>(
                    new ApplyConditionReducer(registry),
                    LifecycleSource,
                    InvocationPolicy.ExternalAllowed
                )
                .RegisterReducer<CleanupConditionsFromSourceOp, ConditionCleanupOutcome>(
                    new CleanupConditionsFromSourceReducer(),
                    LifecycleSource,
                    InvocationPolicy.ExternalAllowed
                )
                .RegisterReducer<CommitMidTurnStunnedLossOp, MidTurnStunnedLossOutcome>(
                    new CommitMidTurnStunnedLossReducer(),
                    ConditionTurnResourceRules.Source,
                    InvocationPolicy.ExternalAllowed
                );
        }
    }
}
