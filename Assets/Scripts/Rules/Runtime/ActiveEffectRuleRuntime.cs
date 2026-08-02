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
        /// Adds typed creation, adoption, update, expiration, and removal reducers.
        /// </summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="registry">
        /// The immutable registry used for runtime extensions and creation-time definition validation.
        /// </param>
        /// <returns>The supplied builder for fluent composition.</returns>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        /// <remarks>
        /// Lifecycle mutations are nested-only so feature handlers authorize them; prepared-state
        /// adoption is an explicit external enrollment boundary. This method also attaches the same
        /// registry to the dispatcher so an effect cannot create or adopt a binding for a definition
        /// unavailable to runtime extensions.
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
                .RegisterReducer<AdoptActiveEffectRegistrationsOp, ActiveEffectAdoptionOutcome>(
                    new AdoptActiveEffectRegistrationsReducer(registry),
                    LifecycleSource,
                    InvocationPolicy.ExternalAllowed
                )
                .RegisterReducer<UpdateActiveEffectStateOp, ActiveEffectStateUpdateOutcome>(
                    new UpdateActiveEffectStateReducer(),
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
