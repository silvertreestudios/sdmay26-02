using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Configures the one-to-one mapping from concrete operation types to handlers or reducers.
    /// </summary>
    /// <remarks>
    /// Each concrete operation type may have exactly one resolver. Building a dispatcher copies
    /// the current registrations so later builder changes do not mutate an existing dispatcher.
    /// </remarks>
    public sealed class RuleDispatcherBuilder
    {
        private readonly Dictionary<Type, IRegistration> registrations =
            new Dictionary<Type, IRegistration>();
        private readonly Dictionary<Type, List<IActionValidatorRegistration>> actionValidators =
            new Dictionary<Type, List<IActionValidatorRegistration>>();
        private readonly IRulesStore store;
        private readonly IOpIdProvider ids;
        private readonly IRollService rollService;
        private RuleRegistry ruleRegistry = RuleRegistry.Empty;
        private bool isRuleRegistryConfigured;
        private ActionRuntimeConfiguration actionRuntimeConfiguration =
            ActionRuntimeConfiguration.Unconfigured;

        /// <summary>
        /// Initializes a dispatcher builder with production roll and sequential operation-ID sources.
        /// </summary>
        /// <param name="store">The store used for snapshots and reducer commits.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
        public RuleDispatcherBuilder(IRulesStore store)
            : this(store, new RandomRollService(), new SequentialOpIdProvider()) { }

        /// <summary>
        /// Initializes a dispatcher builder with an explicit operation-ID source and production rolls.
        /// </summary>
        /// <param name="store">The store used for snapshots and reducer commits.</param>
        /// <param name="ids">The required operation identifier provider.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> or <paramref name="ids"/> is <see langword="null"/>.
        /// </exception>
        public RuleDispatcherBuilder(IRulesStore store, IOpIdProvider ids)
            : this(store, new RandomRollService(), ids) { }

        /// <summary>
        /// Initializes a dispatcher builder with an explicit roll source and sequential operation IDs.
        /// </summary>
        /// <param name="store">The store used for snapshots and reducer commits.</param>
        /// <param name="rollService">The required production, replay, or scripted roll source.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> or <paramref name="rollService"/> is <see langword="null"/>.
        /// </exception>
        public RuleDispatcherBuilder(IRulesStore store, IRollService rollService)
            : this(store, rollService, new SequentialOpIdProvider()) { }

        /// <summary>
        /// Initializes a dispatcher builder with explicit roll and operation-ID sources.
        /// </summary>
        /// <param name="store">The store used for snapshots and reducer commits.</param>
        /// <param name="rollService">The required production, replay, or scripted roll source.</param>
        /// <param name="ids">The required operation identifier provider.</param>
        /// <exception cref="ArgumentNullException">Any dependency is <see langword="null"/>.</exception>
        public RuleDispatcherBuilder(IRulesStore store, IRollService rollService, IOpIdProvider ids)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.rollService = rollService ?? throw new ArgumentNullException(nameof(rollService));
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }

        /// <summary>
        /// Registers an asynchronous handler for a concrete operation type.
        /// </summary>
        /// <typeparam name="TOp">The operation handled by <paramref name="handler"/>.</typeparam>
        /// <typeparam name="TResult">The successful result returned by the handler.</typeparam>
        /// <param name="handler">The handler instance invoked for the operation.</param>
        /// <param name="policy">Whether the operation may begin as a root dispatch.</param>
        /// <returns>This builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <typeparamref name="TOp"/> is a reserved <see cref="PromptChoiceOp{TChoice}"/> type or
        /// already has a resolver. Register prompt adapters through <see cref="UsePromptAdapter{TChoice}"/>.
        /// </exception>
        public RuleDispatcherBuilder RegisterHandler<TOp, TResult>(
            IOpHandler<TOp, TResult> handler,
            InvocationPolicy policy = InvocationPolicy.ExternalAllowed
        )
            where TOp : IRuleOp<TResult>
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            Add(new HandlerRegistration<TOp, TResult>(handler, policy));
            return this;
        }

        /// <summary>
        /// Registers a transactional reducer as a nested-only resolver.
        /// </summary>
        /// <typeparam name="TOp">The operation reduced by <paramref name="reducer"/>.</typeparam>
        /// <typeparam name="TResult">The accepted value produced by the reducer.</typeparam>
        /// <param name="reducer">The reducer that validates and stages state changes and facts.</param>
        /// <param name="source">The rule source stamped onto facts committed by this reducer.</param>
        /// <returns>This builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="reducer"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="source"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">
        /// <typeparamref name="TOp"/> is a reserved <see cref="PromptChoiceOp{TChoice}"/> type or
        /// already has a resolver. Register prompt adapters through <see cref="UsePromptAdapter{TChoice}"/>.
        /// </exception>
        public RuleDispatcherBuilder RegisterReducer<TOp, TResult>(
            IOpReducer<TOp, TResult> reducer,
            RuleSource source
        )
            where TOp : IRuleOp<TResult>
        {
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));
            if (source.IsEmpty)
                throw new ArgumentException(
                    "A reducer registration requires a rule source.",
                    nameof(source)
                );
            Add(new ReducerRegistration<TOp, TResult>(reducer, source));
            return this;
        }

        internal RuleDispatcherBuilder RegisterEngineReducer<TOp, TResult>(
            IOpReducer<TOp, TResult> reducer,
            RuleSource source
        )
            where TOp : IRuleOp<TResult>
        {
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));
            if (source.IsEmpty)
                throw new ArgumentException(
                    "An engine reducer registration requires a rule source.",
                    nameof(source)
                );
            Add(
                new ReducerRegistration<TOp, TResult>(
                    reducer,
                    source,
                    ResolverMiddlewarePolicy.Disabled
                )
            );
            return this;
        }

        /// <summary>
        /// Configures the mandatory action lifecycle with definition-backed profiles and the
        /// identity profile resolver.
        /// </summary>
        /// <param name="catalog">The immutable catalog used by action operations.</param>
        /// <returns>This builder so configuration can be chained.</returns>
        /// <remarks>
        /// Use this overload when no live rule currently changes action profiles. The identity
        /// resolver still freezes exactly one catalog profile per invocation.
        /// </remarks>
        public RuleDispatcherBuilder UseActionLifecycle(IActionCatalog catalog) =>
            UseActionLifecycle(catalog, IdentityActionProfileResolver.Instance);

        /// <summary>
        /// Configures the mandatory action lifecycle and live profile resolver.
        /// </summary>
        /// <param name="catalog">The immutable catalog used by action operations.</param>
        /// <param name="profileResolver">
        /// The pure resolver that applies live state-dependent profile changes once per invocation.
        /// </param>
        /// <returns>This builder so configuration can be chained.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="catalog"/> or <paramref name="profileResolver"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">Action lifecycle services were already configured.</exception>
        public RuleDispatcherBuilder UseActionLifecycle(
            IActionCatalog catalog,
            IActionProfileResolver profileResolver
        )
        {
            if (actionRuntimeConfiguration.IsConfigured)
                throw new InvalidOperationException(
                    "Action lifecycle services are already configured."
                );
            actionRuntimeConfiguration = ActionRuntimeConfiguration.Configure(
                catalog,
                profileResolver
            );
            return this;
        }

        /// <summary>
        /// Registers one pure validator for a concrete action operation.
        /// </summary>
        /// <typeparam name="TOp">The concrete action type to validate.</typeparam>
        /// <param name="validator">The validator invoked in registration order.</param>
        /// <returns>This builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="validator"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// <typeparamref name="TOp"/> does not derive from <see cref="ActionOp{TResult}"/>.
        /// </exception>
        public RuleDispatcherBuilder RegisterActionValidator<TOp>(IActionValidator<TOp> validator)
            where TOp : IRuleOp
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));
            if (!typeof(IActionOpMetadata).IsAssignableFrom(typeof(TOp)))
            {
                throw new InvalidOperationException(
                    $"{typeof(TOp).Name} is not an ActionOp and cannot use action validators."
                );
            }

            if (
                !actionValidators.TryGetValue(
                    typeof(TOp),
                    out List<IActionValidatorRegistration> registrationsForType
                )
            )
            {
                registrationsForType = new List<IActionValidatorRegistration>();
                actionValidators.Add(typeof(TOp), registrationsForType);
            }
            registrationsForType.Add(new ActionValidatorRegistration<TOp>(validator));
            return this;
        }

        /// <summary>
        /// Registers the engine-owned attack, check, save, and modifier-collection handlers with
        /// the standard pure snapshot selectors.
        /// </summary>
        /// <returns>This builder so configuration can be chained.</returns>
        public RuleDispatcherBuilder UseCheckResolution() =>
            UseCheckResolution(new RulesSelectors());

        /// <summary>
        /// Registers the engine-owned attack, check, save, and modifier-collection handlers.
        /// </summary>
        /// <param name="selectors">The pure selectors used to read base and current modifier inputs.</param>
        /// <returns>This builder so configuration can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="selectors"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Any engine-owned check operation already has a resolver.
        /// </exception>
        /// <remarks>
        /// These operations are nested-only so their <see cref="CheckSource"/> can be verified as a
        /// trusted ancestor. Feature handlers dispatch them instead of calling their resolvers directly.
        /// </remarks>
        public RuleDispatcherBuilder UseCheckResolution(IRulesSelectors selectors)
        {
            if (selectors == null)
                throw new ArgumentNullException(nameof(selectors));

            Type[] reservedTypes =
            {
                typeof(AttackCheckOp),
                typeof(SkillCheckOp),
                typeof(SavingThrowOp),
                typeof(CollectSkillCheckModifiersOp),
                typeof(CollectSavingThrowModifiersOp),
                typeof(CollectAttackModifiersOp),
            };
            foreach (Type reservedType in reservedTypes)
            {
                if (registrations.ContainsKey(reservedType))
                {
                    throw new InvalidOperationException(
                        $"{reservedType.Name} is reserved for the engine-owned check runtime."
                    );
                }
            }

            Add(
                new HandlerRegistration<AttackCheckOp, CheckOutcome>(
                    new AttackCheckHandler(),
                    InvocationPolicy.NestedOnly
                )
            );
            Add(
                new HandlerRegistration<SkillCheckOp, CheckOutcome>(
                    new SkillCheckHandler(),
                    InvocationPolicy.NestedOnly
                )
            );
            Add(
                new HandlerRegistration<SavingThrowOp, CheckOutcome>(
                    new SavingThrowHandler(),
                    InvocationPolicy.NestedOnly
                )
            );
            Add(
                new HandlerRegistration<CollectSkillCheckModifiersOp, ModifierCollection>(
                    new CollectSkillCheckModifiersHandler(selectors),
                    InvocationPolicy.NestedOnly
                )
            );
            Add(
                new HandlerRegistration<CollectSavingThrowModifiersOp, ModifierCollection>(
                    new CollectSavingThrowModifiersHandler(selectors),
                    InvocationPolicy.NestedOnly
                )
            );
            Add(
                new HandlerRegistration<CollectAttackModifiersOp, ModifierCollection>(
                    new CollectAttackModifiersHandler(selectors),
                    InvocationPolicy.NestedOnly
                )
            );
            return this;
        }

        /// <summary>
        /// Registers the engine-owned nested prompt resolver for one concrete choice data type.
        /// </summary>
        /// <typeparam name="TChoice">The immutable choice type handled by the adapter.</typeparam>
        /// <param name="adapter">
        /// The player, AI, replay, or scripted adapter that resolves this choice type.
        /// </param>
        /// <returns>This builder so configuration can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="adapter"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// A resolver for <see cref="PromptChoiceOp{TChoice}"/> is already registered.
        /// </exception>
        /// <remarks>
        /// Prompt operations are nested-only and bypass active rule middleware. The adapter receives
        /// immutable request data and a read-only snapshot, so presentation or AI work cannot mutate
        /// state or dispatch privileged operations while the root resolution is suspended.
        /// </remarks>
        public RuleDispatcherBuilder UsePromptAdapter<TChoice>(IPromptAdapter<TChoice> adapter)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            Add(new PromptRegistration<TChoice>(adapter));
            return this;
        }

        /// <summary>
        /// Selects the immutable rule registry used for binding-controlled middleware and Fact listeners.
        /// </summary>
        /// <param name="registry">The static registry to validate and attach to the dispatcher.</param>
        /// <returns>This builder so configuration can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// A different registry instance was already configured for this builder.
        /// </exception>
        /// <remarks>
        /// The registry stores static definitions only. Which registrations participate is decided
        /// from <see cref="RulesSnapshot.RuleBindings"/> each time rules work is resolved.
        /// </remarks>
        public RuleDispatcherBuilder UseRuleRegistry(RuleRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (isRuleRegistryConfigured && !ReferenceEquals(ruleRegistry, registry))
            {
                throw new InvalidOperationException(
                    "A dispatcher builder cannot replace its configured rule registry."
                );
            }

            ruleRegistry = registry;
            isRuleRegistryConfigured = true;
            return this;
        }

        /// <summary>
        /// Builds a dispatcher from a snapshot of the current registrations.
        /// </summary>
        /// <returns>A dispatcher that owns its registration map, trace, and diagnostics.</returns>
        public RuleDispatcher Build()
        {
            Dictionary<Type, IRegistration> completedRegistrations = new Dictionary<
                Type,
                IRegistration
            >(registrations);
            bool hasActionHandler = completedRegistrations.Values.Any(registration =>
                typeof(IActionOpMetadata).IsAssignableFrom(registration.OpType)
            );
            if (
                (hasActionHandler || actionValidators.Count > 0)
                && !actionRuntimeConfiguration.IsConfigured
            )
            {
                throw new InvalidOperationException(
                    "Action handlers and validators require UseActionLifecycle before Build."
                );
            }
            foreach (
                IRegistration actionRegistration in completedRegistrations.Values.Where(
                    registration => typeof(IActionOpMetadata).IsAssignableFrom(registration.OpType)
                )
            )
            {
                if (actionRegistration.IsReducer)
                {
                    throw new InvalidOperationException(
                        $"Action operation {actionRegistration.OpType.Name} must use a feature handler, not a reducer."
                    );
                }
            }

            if (actionRuntimeConfiguration.IsConfigured)
            {
                foreach (
                    KeyValuePair<Type, List<IActionValidatorRegistration>> pair in actionValidators
                )
                {
                    if (
                        !completedRegistrations.TryGetValue(
                            pair.Key,
                            out IRegistration registration
                        )
                    )
                    {
                        throw new InvalidOperationException(
                            $"Action validators for {pair.Key.Name} require a registered handler."
                        );
                    }
                    if (registration.IsReducer)
                    {
                        throw new InvalidOperationException(
                            $"Action operation {pair.Key.Name} must use a feature handler, not a reducer."
                        );
                    }
                }

                AddLifecycleRegistration(
                    completedRegistrations,
                    new HandlerRegistration<ActionBegunOp, ActionStartOutcome>(
                        new ActionBegunHandler(),
                        InvocationPolicy.NestedOnly
                    )
                );
                AddLifecycleRegistration(
                    completedRegistrations,
                    new ReducerRegistration<CommitActionCostsOp, ActionCostsOutcome>(
                        new CommitActionCostsReducer(),
                        RuleSource.FromSlug("action-lifecycle"),
                        ResolverMiddlewarePolicy.Disabled
                    )
                );
            }

            ActionRuntime actionRuntime = actionRuntimeConfiguration.CreateRuntime(
                actionValidators
            );
            ruleRegistry.ValidateResolvers(completedRegistrations);
            return new RuleDispatcher(
                store,
                ids,
                rollService,
                completedRegistrations,
                ruleRegistry,
                actionRuntime
            );
        }

        private static void AddLifecycleRegistration(
            IDictionary<Type, IRegistration> completedRegistrations,
            IRegistration registration
        )
        {
            if (completedRegistrations.ContainsKey(registration.OpType))
            {
                throw new InvalidOperationException(
                    $"{registration.OpType.Name} is reserved for the engine-owned action lifecycle."
                );
            }
            completedRegistrations.Add(registration.OpType, registration);
        }

        private void Add(IRegistration registration)
        {
            if (
                registration.OpType.IsGenericType
                && registration.OpType.GetGenericTypeDefinition() == typeof(PromptChoiceOp<>)
                && !(registration is IPromptRegistration)
            )
            {
                throw new InvalidOperationException(
                    $"{registration.OpType.Name} is reserved for UsePromptAdapter."
                );
            }
            if (registrations.ContainsKey(registration.OpType))
                throw new InvalidOperationException(
                    $"A resolver is already registered for {registration.OpType.Name}."
                );
            registrations.Add(registration.OpType, registration);
        }
    }
}
