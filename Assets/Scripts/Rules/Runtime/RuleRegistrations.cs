using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Describes one registered middleware extension without storing active-instance state.
    /// </summary>
    public abstract class MiddlewareRegistration
    {
        internal MiddlewareRegistration(
            Type operationType,
            Type resultType,
            RuleLifecyclePhase phase,
            long registrationOrder
        )
        {
            OperationType = operationType ?? throw new ArgumentNullException(nameof(operationType));
            ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
            Phase = phase;
            RegistrationOrder = registrationOrder;
        }

        /// <summary>
        /// Gets the concrete operation type wrapped by this registration.
        /// </summary>
        public Type OperationType { get; }

        /// <summary>
        /// Gets the successful result type required from the operation's resolver.
        /// </summary>
        public Type ResultType { get; }

        /// <summary>
        /// Gets the semantic lifecycle stage used for deterministic ordering.
        /// </summary>
        public RuleLifecyclePhase Phase { get; }

        internal long RegistrationOrder { get; }

        internal abstract ValueTask<object> Invoke(
            ActiveRuleBinding binding,
            IFrameInvocation invocation,
            RuleDispatcher dispatcher,
            Func<ValueTask<object>> next
        );
    }

    /// <summary>
    /// Describes one typed post-commit listener extension without storing active-instance state.
    /// </summary>
    public abstract class FactListenerRegistration
    {
        internal FactListenerRegistration(
            Type factType,
            RuleLifecyclePhase phase,
            bool isBatch,
            long registrationOrder
        )
        {
            FactType = factType ?? throw new ArgumentNullException(nameof(factType));
            Phase = phase;
            IsBatch = isBatch;
            RegistrationOrder = registrationOrder;
        }

        /// <summary>
        /// Gets the committed Fact type selected by this registration.
        /// </summary>
        public Type FactType { get; }

        /// <summary>
        /// Gets the semantic lifecycle stage used for deterministic ordering.
        /// </summary>
        public RuleLifecyclePhase Phase { get; }

        /// <summary>
        /// Gets whether matching Facts are delivered together once per committed root.
        /// </summary>
        public bool IsBatch { get; }

        internal long RegistrationOrder { get; }
        internal abstract bool Matches(RuleFact fact);
        internal abstract ValueTask Invoke(
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            FactContext context
        );
    }
}
