using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Freezes one binding's listener eligibility at an operation frame's start boundary.
    /// </summary>
    /// <remarks>
    /// The binding and registration are immutable, so retaining this pair for committed Facts
    /// preserves the selection decision without retaining the frame's complete rules snapshot.
    /// </remarks>
    internal sealed class BoundFactListenerRegistration
    {
        public ActiveRuleBinding Binding { get; }
        public FactListenerRegistration Registration { get; }

        public BoundFactListenerRegistration(
            ActiveRuleBinding binding,
            FactListenerRegistration registration)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
        }

        public static int Compare(
            BoundFactListenerRegistration left,
            BoundFactListenerRegistration right) =>
            Compare(
                left.Binding,
                left.Registration,
                right.Binding,
                right.Registration);

        internal static int Compare(
            ActiveRuleBinding leftBinding,
            FactListenerRegistration leftRegistration,
            ActiveRuleBinding rightBinding,
            FactListenerRegistration rightRegistration)
        {
            int phase = leftRegistration.Phase.CompareTo(rightRegistration.Phase);
            if (phase != 0)
                return phase;
            int creation = leftBinding.CreationOrder.CompareTo(rightBinding.CreationOrder);
            if (creation != 0)
                return creation;
            int id = string.Compare(
                leftBinding.Id.Value,
                rightBinding.Id.Value,
                StringComparison.Ordinal);
            if (id != 0)
                return id;
            int registration = leftRegistration.RegistrationOrder.CompareTo(
                rightRegistration.RegistrationOrder);
            if (registration != 0)
                return registration;

            // A binding ID normally names one immutable value throughout a root. These final
            // comparisons keep delivery stable even if a root removes and recreates that ID with
            // different provenance before notification.
            int definition = string.Compare(
                leftBinding.DefinitionId.Value,
                rightBinding.DefinitionId.Value,
                StringComparison.Ordinal);
            if (definition != 0)
                return definition;
            int owner = string.Compare(
                leftBinding.Owner.Value,
                rightBinding.Owner.Value,
                StringComparison.Ordinal);
            if (owner != 0)
                return owner;
            string leftEffect = leftBinding.EffectId.HasValue
                ? leftBinding.EffectId.Value.Value
                : string.Empty;
            string rightEffect = rightBinding.EffectId.HasValue
                ? rightBinding.EffectId.Value.Value
                : string.Empty;
            int effect = string.Compare(leftEffect, rightEffect, StringComparison.Ordinal);
            if (effect != 0)
                return effect;
            int source = string.Compare(
                leftBinding.Source.Slug,
                rightBinding.Source.Slug,
                StringComparison.Ordinal);
            if (source != 0)
                return source;
            return leftBinding.IsEnabled.CompareTo(rightBinding.IsEnabled);
        }
    }

    /// <summary>
    /// Associates one Fact with the immutable listeners selected by its source frame.
    /// </summary>
    internal sealed class CommittedFactRecord
    {
        public RuleFact Fact { get; }
        public IReadOnlyList<BoundFactListenerRegistration> EligibleListeners { get; }

        public CommittedFactRecord(
            RuleFact fact,
            IReadOnlyList<BoundFactListenerRegistration> eligibleListeners)
        {
            Fact = fact ?? throw new ArgumentNullException(nameof(fact));
            EligibleListeners = eligibleListeners ??
                throw new ArgumentNullException(nameof(eligibleListeners));
        }
    }

    /// <summary>
    /// Groups eligible Facts only when both the immutable binding value and static registration
    /// match, so a recreated binding cannot inherit an earlier binding version's eligibility.
    /// </summary>
    internal sealed class FactListenerDeliveryKey : IEquatable<FactListenerDeliveryKey>
    {
        public ActiveRuleBinding Binding { get; }
        public FactListenerRegistration Registration { get; }

        public FactListenerDeliveryKey(
            ActiveRuleBinding binding,
            FactListenerRegistration registration)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
        }

        public bool Equals(FactListenerDeliveryKey other) =>
            other != null && Binding.Equals(other.Binding) &&
            ReferenceEquals(Registration, other.Registration);

        public override bool Equals(object obj) =>
            obj is FactListenerDeliveryKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Binding, Registration);
    }

    internal sealed class FactListenerDelivery
    {
        public ActiveRuleBinding Binding { get; }
        public FactListenerRegistration Registration { get; }
        public OpId RootId { get; }
        public IReadOnlyList<RuleFact> Facts { get; }

        public FactListenerDelivery(
            ActiveRuleBinding binding,
            FactListenerRegistration registration,
            OpId rootId,
            IReadOnlyList<RuleFact> facts)
        {
            Binding = binding;
            Registration = registration;
            RootId = rootId;
            Facts = facts;
        }

        public static int Compare(FactListenerDelivery left, FactListenerDelivery right)
            => BoundFactListenerRegistration.Compare(
                left.Binding,
                left.Registration,
                right.Binding,
                right.Registration);
    }

    internal sealed class TypedFactListenerRegistration<TFact> : FactListenerRegistration
        where TFact : RuleFact
    {
        private readonly IRuleFactListener<TFact> listener;

        public TypedFactListenerRegistration(
            RuleLifecyclePhase phase,
            long registrationOrder,
            IRuleFactListener<TFact> listener)
            : base(typeof(TFact), phase, false, registrationOrder) => this.listener = listener;

        internal override bool Matches(RuleFact fact) => fact is TFact;

        internal override ValueTask Invoke(
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            FactContext context)
        {
            if (facts.Count != 1 || !(facts[0] is TFact typed))
                throw new InvalidOperationException("A single-Fact listener received an impossible delivery.");
            return listener.OnFactCommitted(typed, context);
        }
    }

    internal sealed class TypedFactBatchListenerRegistration<TFact> : FactListenerRegistration
        where TFact : RuleFact
    {
        private readonly IRuleFactBatchListener<TFact> listener;

        public TypedFactBatchListenerRegistration(
            RuleLifecyclePhase phase,
            long registrationOrder,
            IRuleFactBatchListener<TFact> listener)
            : base(typeof(TFact), phase, true, registrationOrder) => this.listener = listener;

        internal override bool Matches(RuleFact fact) => fact is TFact;

        internal override ValueTask Invoke(
            OpId rootId,
            IReadOnlyList<RuleFact> facts,
            FactContext context)
        {
            TFact[] typed = facts.Cast<TFact>().ToArray();
            return listener.OnFactsCommitted(
                new CommittedFactBatch<TFact>(rootId, Array.AsReadOnly(typed)),
                context);
        }
    }
}
