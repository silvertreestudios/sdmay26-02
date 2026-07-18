using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

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
        private RuleRegistry ruleRegistry = RuleRegistry.Empty;
        private ActionRuntimeConfiguration actionRuntimeConfiguration =
            ActionRuntimeConfiguration.Unconfigured;

        /// <summary>
        /// Initializes a dispatcher builder with its rules store and operation ID source.
        /// </summary>
        /// <param name="store">The store used for snapshots and reducer commits.</param>
        /// <param name="ids">
        /// The identifier provider, or <see langword="null"/> to use a new
        /// <see cref="SequentialOpIdProvider"/>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
        public RuleDispatcherBuilder(IRulesStore store, IOpIdProvider ids = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ids = ids ?? new SequentialOpIdProvider();
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
        /// <exception cref="InvalidOperationException"><typeparamref name="TOp"/> already has a resolver.</exception>
        public RuleDispatcherBuilder RegisterHandler<TOp, TResult>(
            IOpHandler<TOp, TResult> handler,
            InvocationPolicy policy = InvocationPolicy.ExternalAllowed)
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
        /// <exception cref="InvalidOperationException"><typeparamref name="TOp"/> already has a resolver.</exception>
        public RuleDispatcherBuilder RegisterReducer<TOp, TResult>(
            IOpReducer<TOp, TResult> reducer,
            RuleSource source)
            where TOp : IRuleOp<TResult>
        {
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));
            if (source.IsEmpty)
                throw new ArgumentException("A reducer registration requires a rule source.", nameof(source));
            Add(new ReducerRegistration<TOp, TResult>(reducer, source));
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
            IActionProfileResolver profileResolver)
        {
            if (actionRuntimeConfiguration.IsConfigured)
                throw new InvalidOperationException("Action lifecycle services are already configured.");
            actionRuntimeConfiguration = ActionRuntimeConfiguration.Configure(
                catalog,
                profileResolver);
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
        public RuleDispatcherBuilder RegisterActionValidator<TOp>(
            IActionValidator<TOp> validator)
            where TOp : IRuleOp
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));
            if (!typeof(IActionOpMetadata).IsAssignableFrom(typeof(TOp)))
            {
                throw new InvalidOperationException(
                    $"{typeof(TOp).Name} is not an ActionOp and cannot use action validators.");
            }

            if (!actionValidators.TryGetValue(
                typeof(TOp),
                out List<IActionValidatorRegistration> registrationsForType))
            {
                registrationsForType = new List<IActionValidatorRegistration>();
                actionValidators.Add(typeof(TOp), registrationsForType);
            }
            registrationsForType.Add(new ActionValidatorRegistration<TOp>(validator));
            return this;
        }

        /// <summary>
        /// Selects the immutable rule registry used for binding-controlled middleware and Fact listeners.
        /// </summary>
        /// <param name="registry">The static registry to validate and attach to the dispatcher.</param>
        /// <returns>This builder so configuration can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// The registry stores static definitions only. Which registrations participate is decided
        /// from <see cref="RulesSnapshot.RuleBindings"/> each time rules work is resolved.
        /// </remarks>
        public RuleDispatcherBuilder UseRuleRegistry(RuleRegistry registry)
        {
            ruleRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            return this;
        }

        /// <summary>
        /// Builds a dispatcher from a snapshot of the current registrations.
        /// </summary>
        /// <returns>A dispatcher that owns its registration map, trace, and diagnostics.</returns>
        public RuleDispatcher Build()
        {
            Dictionary<Type, IRegistration> completedRegistrations =
                new Dictionary<Type, IRegistration>(registrations);
            bool hasActionHandler = completedRegistrations.Values.Any(registration =>
                typeof(IActionOpMetadata).IsAssignableFrom(registration.OpType));
            if ((hasActionHandler || actionValidators.Count > 0) &&
                !actionRuntimeConfiguration.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Action handlers and validators require UseActionLifecycle before Build.");
            }
            foreach (IRegistration actionRegistration in completedRegistrations.Values.Where(
                registration => typeof(IActionOpMetadata).IsAssignableFrom(registration.OpType)))
            {
                if (actionRegistration.IsReducer)
                {
                    throw new InvalidOperationException(
                        $"Action operation {actionRegistration.OpType.Name} must use a feature handler, not a reducer.");
                }
            }

            if (actionRuntimeConfiguration.IsConfigured)
            {
                foreach (KeyValuePair<Type, List<IActionValidatorRegistration>> pair in actionValidators)
                {
                    if (!completedRegistrations.TryGetValue(pair.Key, out IRegistration registration))
                    {
                        throw new InvalidOperationException(
                            $"Action validators for {pair.Key.Name} require a registered handler.");
                    }
                    if (registration.IsReducer)
                    {
                        throw new InvalidOperationException(
                            $"Action operation {pair.Key.Name} must use a feature handler, not a reducer.");
                    }
                }

                AddLifecycleRegistration(
                    completedRegistrations,
                    new HandlerRegistration<ActionBegunOp, ActionStartOutcome>(
                        new ActionBegunHandler(),
                        InvocationPolicy.NestedOnly));
                AddLifecycleRegistration(
                    completedRegistrations,
                    new ReducerRegistration<CommitActionCostsOp, ActionCostsOutcome>(
                        new CommitActionCostsReducer(),
                        RuleSource.FromSlug("action-lifecycle"),
                        ResolverMiddlewarePolicy.Disabled));
            }

            ActionRuntime actionRuntime = actionRuntimeConfiguration.CreateRuntime(
                actionValidators);
            ruleRegistry.ValidateResolvers(completedRegistrations);
            return new RuleDispatcher(
                store,
                ids,
                completedRegistrations,
                ruleRegistry,
                actionRuntime);
        }

        private static void AddLifecycleRegistration(
            IDictionary<Type, IRegistration> completedRegistrations,
            IRegistration registration)
        {
            if (completedRegistrations.ContainsKey(registration.OpType))
            {
                throw new InvalidOperationException(
                    $"{registration.OpType.Name} is reserved for the engine-owned action lifecycle.");
            }
            completedRegistrations.Add(registration.OpType, registration);
        }

        private void Add(IRegistration registration)
        {
            if (registrations.ContainsKey(registration.OpType))
                throw new InvalidOperationException(
                    $"A resolver is already registered for {registration.OpType.Name}.");
            registrations.Add(registration.OpType, registration);
        }
    }
}
