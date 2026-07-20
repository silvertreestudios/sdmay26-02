using System;

namespace Game.Rules.Runtime
{
    /// <summary>Registers the nested-only active-effect lifecycle reducers.</summary>
    public static class ActiveEffectRuleDispatcherExtensions
    {
        private static readonly RuleSource LifecycleSource = RuleSource.FromSlug(
            "active-effect-lifecycle"
        );

        /// <summary>
        /// Adds typed create, update, expire, and remove reducers backed by one static registry.
        /// </summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="registry">
        /// The immutable registry used both for runtime extensions and exact effect-state validation.
        /// </param>
        /// <returns>The supplied builder for fluent composition.</returns>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        /// <remarks>
        /// All four operations are reducer registrations and therefore nested-only. Feature handlers
        /// authorize their use; presentation and encounter-clock code may only request work through
        /// those workflows. This method also attaches the same registry to the dispatcher so type
        /// validation cannot drift from active middleware and Fact-listener definitions.
        /// </remarks>
        public static RuleDispatcherBuilder UseActiveEffectRules(
            this RuleDispatcherBuilder builder,
            RuleRegistry registry
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            return builder
                .UseRuleRegistry(registry)
                .RegisterReducer<CreateActiveEffectOp, ActiveEffectCreationOutcome>(
                    new CreateActiveEffectReducer(registry),
                    LifecycleSource
                )
                .RegisterReducer<UpdateActiveEffectStateOp, ActiveEffectStateUpdateOutcome>(
                    new UpdateActiveEffectStateReducer(registry),
                    LifecycleSource
                )
                .RegisterReducer<ExpireActiveEffectOp, ActiveEffectExpirationOutcome>(
                    new ExpireActiveEffectReducer(),
                    LifecycleSource
                )
                .RegisterReducer<RemoveActiveEffectOp, ActiveEffectRemovalOutcome>(
                    new RemoveActiveEffectReducer(),
                    LifecycleSource
                );
        }
    }
}
