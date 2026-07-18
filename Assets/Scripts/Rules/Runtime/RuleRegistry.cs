using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Builds an immutable registry of static rule definitions.
    /// </summary>
    public sealed class RuleRegistryBuilder
    {
        private readonly Dictionary<RuleDefinitionId, RuleDefinitionBuilder> definitions =
            new Dictionary<RuleDefinitionId, RuleDefinitionBuilder>();

        /// <summary>
        /// Starts one static rule definition.
        /// </summary>
        /// <param name="id">The stable definition ID stored by active bindings.</param>
        /// <returns>A builder used to register the definition's typed extensions.</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="id"/> was already defined.</exception>
        public RuleDefinitionBuilder Define(RuleDefinitionId id)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(id));
            if (definitions.ContainsKey(id))
                throw new InvalidOperationException($"Rule definition {id.Value} is already registered.");

            RuleDefinitionBuilder definition = new RuleDefinitionBuilder(id);
            definitions.Add(id, definition);
            return definition;
        }

        /// <summary>
        /// Builds an immutable registry snapshot from all definitions currently registered.
        /// </summary>
        /// <returns>A registry that is unaffected by later builder changes.</returns>
        public RuleRegistry Build() => new RuleRegistry(definitions.Values.Select(value => value.Build()));
    }

    /// <summary>
    /// Stores immutable rule definitions and selects their extensions from active snapshot bindings.
    /// </summary>
    public sealed class RuleRegistry
    {
        private readonly IReadOnlyDictionary<RuleDefinitionId, RuleDefinition> byId;

        internal static RuleRegistry Empty { get; } = new RuleRegistry(Array.Empty<RuleDefinition>());

        internal RuleRegistry(IEnumerable<RuleDefinition> definitions)
        {
            Dictionary<RuleDefinitionId, RuleDefinition> values = definitions.ToDictionary(
                definition => definition.Id,
                definition => definition);
            byId = new ReadOnlyDictionary<RuleDefinitionId, RuleDefinition>(values);
            Definitions = Array.AsReadOnly(values.Values
                .OrderBy(value => value.Id.Value, StringComparer.Ordinal)
                .ToArray());
        }

        /// <summary>
        /// Gets all static definitions ordered by stable definition ID using ordinal string comparison.
        /// </summary>
        public IReadOnlyList<RuleDefinition> Definitions { get; }

        internal void ValidateResolvers(IReadOnlyDictionary<Type, IRegistration> resolvers)
        {
            foreach (RuleDefinition definition in Definitions)
            {
                foreach (MiddlewareRegistration middlewareRegistration in definition.Middleware)
                {
                    if (!resolvers.TryGetValue(
                        middlewareRegistration.OperationType,
                        out IRegistration resolver))
                    {
                        throw new InvalidOperationException(
                            $"Middleware for {middlewareRegistration.OperationType.Name} has no registered resolver.");
                    }
                    if (resolver.ResultType != middlewareRegistration.ResultType)
                    {
                        throw new InvalidOperationException(
                            $"Middleware for {middlewareRegistration.OperationType.Name} expects " +
                            $"{middlewareRegistration.ResultType.Name}, but its resolver returns " +
                            $"{resolver.ResultType.Name}.");
                    }
                    if (resolver.MiddlewarePolicy == ResolverMiddlewarePolicy.Disabled)
                    {
                        throw new InvalidOperationException(
                            $"Middleware for {middlewareRegistration.OperationType.Name} is not " +
                            "allowed by its resolver registration.");
                    }
                }
            }
        }

        internal IReadOnlyList<BoundMiddlewareRegistration> SelectMiddleware(
            Type operationType,
            Type resultType,
            RulesSnapshot snapshot)
        {
            List<BoundMiddlewareRegistration> selected = new List<BoundMiddlewareRegistration>();
            foreach (KeyValuePair<BindingId, ActiveRuleBinding> pair in snapshot.RuleBindings)
            {
                ActiveRuleBinding binding = pair.Value;
                if (!binding.IsEnabled)
                    continue;
                RuleDefinition definition = RequireDefinition(binding.DefinitionId);
                foreach (MiddlewareRegistration registration in definition.Middleware)
                {
                    if (registration.OperationType == operationType && registration.ResultType == resultType)
                        selected.Add(new BoundMiddlewareRegistration(binding, registration));
                }
            }

            selected.Sort(BoundMiddlewareRegistration.Compare);
            return selected;
        }

        internal IReadOnlyList<BoundFactListenerRegistration> SelectFactListeners(
            RulesSnapshot snapshot)
        {
            List<BoundFactListenerRegistration> selected =
                new List<BoundFactListenerRegistration>();
            foreach (KeyValuePair<BindingId, ActiveRuleBinding> pair in snapshot.RuleBindings)
            {
                ActiveRuleBinding binding = pair.Value;
                if (!binding.IsEnabled)
                    continue;
                RuleDefinition definition = RequireDefinition(binding.DefinitionId);
                foreach (FactListenerRegistration registration in definition.FactListeners)
                    selected.Add(new BoundFactListenerRegistration(binding, registration));
            }

            selected.Sort(BoundFactListenerRegistration.Compare);
            return Array.AsReadOnly(selected.ToArray());
        }

        internal IReadOnlyList<FactListenerDelivery> BuildFactListenerDeliveries(
            OpId rootId,
            IReadOnlyList<CommittedFactRecord> committedFacts)
        {
            Dictionary<FactListenerDeliveryKey, List<RuleFact>> groupedFacts =
                new Dictionary<FactListenerDeliveryKey, List<RuleFact>>();
            foreach (CommittedFactRecord committed in committedFacts)
            {
                foreach (BoundFactListenerRegistration listener in committed.EligibleListeners)
                {
                    if (!listener.Registration.Matches(committed.Fact))
                        continue;

                    FactListenerDeliveryKey key = new FactListenerDeliveryKey(
                        listener.Binding,
                        listener.Registration);
                    if (!groupedFacts.TryGetValue(key, out List<RuleFact> matching))
                    {
                        matching = new List<RuleFact>();
                        groupedFacts.Add(key, matching);
                    }
                    matching.Add(committed.Fact);
                }
            }

            List<FactListenerDelivery> deliveries = new List<FactListenerDelivery>();
            foreach (KeyValuePair<FactListenerDeliveryKey, List<RuleFact>> pair in groupedFacts)
            {
                deliveries.Add(new FactListenerDelivery(
                    pair.Key.Binding,
                    pair.Key.Registration,
                    rootId,
                    Array.AsReadOnly(pair.Value.ToArray())));
            }
            deliveries.Sort(FactListenerDelivery.Compare);
            return Array.AsReadOnly(deliveries.ToArray());
        }

        internal bool IsActive(RulesSnapshot snapshot, ActiveRuleBinding binding) =>
            snapshot.RuleBindings.TryGet(binding.Id, out ActiveRuleBinding current) &&
            current.IsEnabled && current.Equals(binding);

        private RuleDefinition RequireDefinition(RuleDefinitionId id)
        {
            if (!byId.TryGetValue(id, out RuleDefinition definition))
                throw new InvalidOperationException($"Active binding references unknown rule definition {id.Value}.");
            return definition;
        }
    }
}
