using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Collects the static extensions contributed by one feat, condition, effect, item, or rule module.
    /// </summary>
    /// <remarks>
    /// Definitions are immutable after construction and contain no per-binding mutable state.
    /// Runtime participation is controlled exclusively by matching <see cref="ActiveRuleBinding"/>
    /// values selected from an operation frame's start <see cref="RulesSnapshot"/>. Middleware and
    /// Fact delivery recheck the live snapshot before invoking a selected binding.
    /// </remarks>
    public sealed class RuleDefinition
    {
        internal RuleDefinition(
            RuleDefinitionId id,
            Type effectStateType,
            IReadOnlyList<MiddlewareRegistration> middleware,
            IReadOnlyList<FactListenerRegistration> factListeners
        )
        {
            Id = id;
            EffectStateType = effectStateType;
            Middleware = middleware;
            FactListeners = factListeners;
        }

        /// <summary>
        /// Gets the stable ID referenced by active bindings.
        /// </summary>
        public RuleDefinitionId Id { get; }

        /// <summary>
        /// Gets the exact immutable state type accepted by active instances of this definition.
        /// </summary>
        /// <remarks>
        /// A <see langword="null"/> value means this definition contributes only stateless bindings
        /// and cannot back an <see cref="ActiveEffectInstance"/>. This is a genuine domain absence:
        /// feats and other permanent rules may participate without instance state.
        /// </remarks>
        public Type EffectStateType { get; }

        /// <summary>
        /// Gets whether the definition can back a stateful active-effect instance.
        /// </summary>
        public bool SupportsActiveEffects => EffectStateType != null;

        /// <summary>
        /// Determines whether a state value has the exact type declared by this definition.
        /// </summary>
        /// <param name="state">The required immutable effect-state value.</param>
        /// <returns><see langword="true"/> only for an exact declared-type match.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
        public bool AcceptsEffectState(IEffectState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            return EffectStateType == state.GetType();
        }

        /// <summary>
        /// Gets the definition's immutable middleware registrations.
        /// </summary>
        public IReadOnlyList<MiddlewareRegistration> Middleware { get; }

        /// <summary>
        /// Gets the definition's immutable committed-Fact listener registrations.
        /// </summary>
        public IReadOnlyList<FactListenerRegistration> FactListeners { get; }
    }

    /// <summary>
    /// Builds the static extensions for one <see cref="RuleDefinition"/>.
    /// </summary>
    public sealed class RuleDefinitionBuilder
    {
        private readonly List<MiddlewareRegistration> middleware =
            new List<MiddlewareRegistration>();
        private readonly List<FactListenerRegistration> factListeners =
            new List<FactListenerRegistration>();
        private Type effectStateType;
        private long registrationOrder;

        internal RuleDefinitionBuilder(RuleDefinitionId id)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(id));
            Id = id;
        }

        /// <summary>
        /// Gets the stable ID assigned to the definition under construction.
        /// </summary>
        public RuleDefinitionId Id { get; }

        /// <summary>
        /// Declares the exact immutable state type accepted by active instances of this definition.
        /// </summary>
        /// <typeparam name="TState">The definition-owned immutable state value type.</typeparam>
        /// <returns>This definition builder so registrations can be chained.</returns>
        /// <exception cref="InvalidOperationException">A state type was already declared.</exception>
        public RuleDefinitionBuilder EffectState<TState>()
            where TState : IEffectState
        {
            if (effectStateType != null)
            {
                throw new InvalidOperationException(
                    $"Definition {Id.Value} already declares effect state {effectStateType.Name}."
                );
            }

            effectStateType = typeof(TState);
            return this;
        }

        /// <summary>
        /// Adds one typed middleware extension to this definition.
        /// </summary>
        /// <typeparam name="TOp">The concrete operation type wrapped by the middleware.</typeparam>
        /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
        /// <param name="phase">The semantic stage used to order active middleware.</param>
        /// <param name="value">The stateless middleware implementation.</param>
        /// <returns>This definition builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// This definition already registers the operation in the same lifecycle phase.
        /// </exception>
        public RuleDefinitionBuilder Middleware<TOp, TResult>(
            RuleLifecyclePhase phase,
            IOpMiddleware<TOp, TResult> value
        )
            where TOp : IRuleOp<TResult>
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            RequirePhase(phase);
            if (middleware.Any(item => item.OperationType == typeof(TOp) && item.Phase == phase))
            {
                throw new InvalidOperationException(
                    $"Definition {Id.Value} already registers {typeof(TOp).Name} middleware in {phase}."
                );
            }

            middleware.Add(
                new TypedMiddlewareRegistration<TOp, TResult>(phase, registrationOrder++, value)
            );
            return this;
        }

        /// <summary>
        /// Adds a listener that receives matching committed Facts one at a time.
        /// </summary>
        /// <typeparam name="TFact">The committed Fact type to observe.</typeparam>
        /// <param name="phase">The semantic stage used to order active listeners.</param>
        /// <param name="value">The stateless listener implementation.</param>
        /// <returns>This definition builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// This definition already registers the same single-Fact listener type and phase.
        /// </exception>
        public RuleDefinitionBuilder FactListener<TFact>(
            RuleLifecyclePhase phase,
            IRuleFactListener<TFact> value
        )
            where TFact : RuleFact
        {
            AddFactListener(
                typeof(TFact),
                phase,
                false,
                value,
                order => new TypedFactListenerRegistration<TFact>(phase, order, value)
            );
            return this;
        }

        /// <summary>
        /// Adds a listener that receives all matching Facts together once per committed root.
        /// </summary>
        /// <typeparam name="TFact">The committed Fact type to group and observe.</typeparam>
        /// <param name="phase">The semantic stage used to order active listeners.</param>
        /// <param name="value">The stateless batch-listener implementation.</param>
        /// <returns>This definition builder so registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// This definition already registers the same batch-listener type and phase.
        /// </exception>
        public RuleDefinitionBuilder FactBatchListener<TFact>(
            RuleLifecyclePhase phase,
            IRuleFactBatchListener<TFact> value
        )
            where TFact : RuleFact
        {
            AddFactListener(
                typeof(TFact),
                phase,
                true,
                value,
                order => new TypedFactBatchListenerRegistration<TFact>(phase, order, value)
            );
            return this;
        }

        internal RuleDefinition Build()
        {
            MiddlewareRegistration[] middlewareCopy = middleware.ToArray();
            FactListenerRegistration[] listenerCopy = factListeners.ToArray();
            return new RuleDefinition(
                Id,
                effectStateType,
                Array.AsReadOnly(middlewareCopy),
                Array.AsReadOnly(listenerCopy)
            );
        }

        private void AddFactListener(
            Type factType,
            RuleLifecyclePhase phase,
            bool isBatch,
            object value,
            Func<long, FactListenerRegistration> create
        )
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            RequirePhase(phase);
            if (
                factListeners.Any(item =>
                    item.FactType == factType && item.Phase == phase && item.IsBatch == isBatch
                )
            )
            {
                throw new InvalidOperationException(
                    $"Definition {Id.Value} already registers this {factType.Name} listener in {phase}."
                );
            }
            factListeners.Add(create(registrationOrder++));
        }

        private static void RequirePhase(RuleLifecyclePhase phase)
        {
            if (!Enum.IsDefined(typeof(RuleLifecyclePhase), phase))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phase),
                    phase,
                    "Rule extensions must use a defined semantic lifecycle phase."
                );
            }
        }
    }
}
