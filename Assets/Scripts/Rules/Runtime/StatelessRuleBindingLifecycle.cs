using System;

namespace Game.Rules.Runtime
{
    /// <summary>Result of creating a stateless active binding.</summary>
    public readonly struct StatelessRuleBindingCreatedOutcome
    {
        public StatelessRuleBindingCreatedOutcome(BindingId binding) => Binding = binding;

        public BindingId Binding { get; }
    }

    /// <summary>Result of changing stateless binding participation.</summary>
    public readonly struct StatelessRuleBindingEnabledOutcome
    {
        public StatelessRuleBindingEnabledOutcome(BindingId binding, bool isEnabled)
        {
            Binding = binding;
            IsEnabled = isEnabled;
        }

        public BindingId Binding { get; }
        public bool IsEnabled { get; }
    }

    /// <summary>Result of removing a stateless active binding.</summary>
    public readonly struct StatelessRuleBindingRemovedOutcome
    {
        public StatelessRuleBindingRemovedOutcome(BindingId binding) => Binding = binding;

        public BindingId Binding { get; }
    }

    /// <summary>Requests creation of an enabled binding that has no active-effect owner.</summary>
    public sealed class CreateStatelessRuleBindingOp
        : IRuleOp<StatelessRuleBindingCreatedOutcome>,
            IRuleSourcedOp
    {
        public CreateStatelessRuleBindingOp(ActiveRuleBinding binding)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            Source = binding.Source;
        }

        public ActiveRuleBinding Binding { get; }
        public RuleSource Source { get; }
    }

    /// <summary>Requests enabling an exact stateless binding generation.</summary>
    public sealed class EnableStatelessRuleBindingOp
        : IRuleOp<StatelessRuleBindingEnabledOutcome>,
            IRuleSourcedOp
    {
        public EnableStatelessRuleBindingOp(
            BindingId binding,
            long expectedCreationOrder,
            RuleSource source
        )
        {
            Binding = Require(binding, nameof(binding));
            ExpectedCreationOrder = RequireOrder(
                expectedCreationOrder,
                nameof(expectedCreationOrder)
            );
            Source = Require(source, nameof(source));
        }

        public BindingId Binding { get; }
        public long ExpectedCreationOrder { get; }
        public RuleSource Source { get; }

        private static BindingId Require(BindingId value, string parameterName) =>
            value.IsEmpty
                ? throw new ArgumentException("A binding ID is required.", parameterName)
                : value;

        private static long RequireOrder(long value, string parameterName) =>
            value < 0 ? throw new ArgumentOutOfRangeException(parameterName) : value;

        private static RuleSource Require(RuleSource value, string parameterName) =>
            value.IsEmpty
                ? throw new ArgumentException("A rule source is required.", parameterName)
                : value;
    }

    /// <summary>Requests disabling an exact stateless binding generation.</summary>
    public sealed class DisableStatelessRuleBindingOp
        : IRuleOp<StatelessRuleBindingEnabledOutcome>,
            IRuleSourcedOp
    {
        public DisableStatelessRuleBindingOp(
            BindingId binding,
            long expectedCreationOrder,
            RuleSource source
        )
        {
            Binding = binding;
            ExpectedCreationOrder = expectedCreationOrder;
            Source = source;
            if (binding.IsEmpty || source.IsEmpty || expectedCreationOrder < 0)
                throw new ArgumentException(
                    "A binding, source, and non-negative creation order are required."
                );
        }

        public BindingId Binding { get; }
        public long ExpectedCreationOrder { get; }
        public RuleSource Source { get; }
    }

    /// <summary>Requests removal of an exact stateless binding generation.</summary>
    public sealed class RemoveStatelessRuleBindingOp
        : IRuleOp<StatelessRuleBindingRemovedOutcome>,
            IRuleSourcedOp
    {
        public RemoveStatelessRuleBindingOp(
            BindingId binding,
            long expectedCreationOrder,
            RuleSource source
        )
        {
            Binding = binding;
            ExpectedCreationOrder = expectedCreationOrder;
            Source = source;
            if (binding.IsEmpty || source.IsEmpty || expectedCreationOrder < 0)
                throw new ArgumentException(
                    "A binding, source, and non-negative creation order are required."
                );
        }

        public BindingId Binding { get; }
        public long ExpectedCreationOrder { get; }
        public RuleSource Source { get; }
    }

    /// <summary>Records committed creation of one stateless binding.</summary>
    public sealed class StatelessRuleBindingCreatedFact : RuleFact
    {
        internal StatelessRuleBindingCreatedFact(ActiveRuleBinding binding) => Binding = binding;

        public ActiveRuleBinding Binding { get; }
    }

    /// <summary>Records a committed stateless binding participation change.</summary>
    public sealed class StatelessRuleBindingEnabledChangedFact : RuleFact
    {
        internal StatelessRuleBindingEnabledChangedFact(ActiveRuleBinding binding) =>
            Binding = binding;

        /// <summary>Gets the complete committed binding generation after the change.</summary>
        public ActiveRuleBinding Binding { get; }

        /// <summary>Gets the committed participation state.</summary>
        public bool IsEnabled => Binding.IsEnabled;
    }

    /// <summary>Records committed removal of one stateless binding.</summary>
    public sealed class StatelessRuleBindingRemovedFact : RuleFact
    {
        internal StatelessRuleBindingRemovedFact(ActiveRuleBinding binding) => Binding = binding;

        /// <summary>Gets the complete removed binding generation and its provenance.</summary>
        public ActiveRuleBinding Binding { get; }
    }

    internal static class StatelessBindingReduction
    {
        internal static bool TryGet(
            RulesStateDraft state,
            BindingId id,
            long expectedOrder,
            RuleSource expectedSource,
            out ActiveRuleBinding binding,
            out string rejection
        )
        {
            if (!state.RuleBindings.TryGet(id, out binding))
            {
                rejection = $"Active binding {id.Value} is unknown.";
                return false;
            }
            if (binding.EffectId.HasValue)
            {
                rejection =
                    $"Active binding {id.Value} is effect-backed and must use active-effect lifecycle operations.";
                return false;
            }
            if (binding.CreationOrder != expectedOrder)
            {
                rejection =
                    $"Active binding {id.Value} expected creation order {expectedOrder}, but current order is {binding.CreationOrder}.";
                return false;
            }
            if (binding.Source != expectedSource)
            {
                rejection =
                    $"Active binding {id.Value} source {binding.Source.Slug} does not match {expectedSource.Slug}.";
                return false;
            }
            if (
                !state.StatelessRuleBindingGenerations.TryGet(id, out long latestGeneration)
                || latestGeneration != binding.CreationOrder
            )
            {
                rejection =
                    $"Active binding {id.Value} does not match its authoritative stateless generation history.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        internal static bool CanCreate(
            RulesStateDraft state,
            ActiveRuleBinding binding,
            out string rejection
        )
        {
            if (
                state.StatelessRuleBindingGenerations.TryGet(binding.Id, out long latestGeneration)
                && binding.CreationOrder <= latestGeneration
            )
            {
                rejection =
                    $"Stateless binding {binding.Id.Value} generation {binding.CreationOrder} must be newer than committed generation {latestGeneration}.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        internal static void Record(RulesStateDraft state, ActiveRuleBinding binding)
        {
            if (binding.EffectId.HasValue)
                throw new InvalidOperationException(
                    "Effect-backed bindings do not use stateless generation history."
                );
            state.StatelessRuleBindingGenerations.Set(binding.Id, binding.CreationOrder);
        }
    }

    internal sealed class CreateStatelessRuleBindingReducer
        : IOpReducer<CreateStatelessRuleBindingOp, StatelessRuleBindingCreatedOutcome>
    {
        private readonly RuleRegistry registry;

        internal CreateStatelessRuleBindingReducer(RuleRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public ReductionResult<StatelessRuleBindingCreatedOutcome> Reduce(
            ReductionContext<CreateStatelessRuleBindingOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            ActiveRuleBinding binding = context.Op.Binding;
            if (binding.EffectId.HasValue)
                return ReductionResult<StatelessRuleBindingCreatedOutcome>.Reject(
                    "Effect-backed bindings must use active-effect creation."
                );
            if (!binding.IsEnabled)
                return ReductionResult<StatelessRuleBindingCreatedOutcome>.Reject(
                    "A new stateless binding must begin enabled."
                );
            if (!registry.TryGetDefinition(binding.DefinitionId, out _))
                return ReductionResult<StatelessRuleBindingCreatedOutcome>.Reject(
                    $"Rule definition {binding.DefinitionId.Value} is unknown."
                );
            if (state.RuleBindings.Contains(binding.Id))
                return ReductionResult<StatelessRuleBindingCreatedOutcome>.Reject(
                    $"Active binding {binding.Id.Value} already exists."
                );
            if (!StatelessBindingReduction.CanCreate(state, binding, out string rejection))
                return ReductionResult<StatelessRuleBindingCreatedOutcome>.Reject(rejection);
            state.RuleBindings.Set(binding.Id, binding);
            StatelessBindingReduction.Record(state, binding);
            facts.Stage(new StatelessRuleBindingCreatedFact(binding));
            return ReductionResult<StatelessRuleBindingCreatedOutcome>.Accept(
                new StatelessRuleBindingCreatedOutcome(binding.Id)
            );
        }
    }

    internal abstract class SetStatelessRuleBindingEnabledReducer
    {
        protected static ReductionResult<StatelessRuleBindingEnabledOutcome> Reduce(
            BindingId id,
            long order,
            RuleSource source,
            bool enabled,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !StatelessBindingReduction.TryGet(
                    state,
                    id,
                    order,
                    source,
                    out ActiveRuleBinding binding,
                    out string rejection
                )
            )
                return ReductionResult<StatelessRuleBindingEnabledOutcome>.Reject(rejection);
            if (binding.IsEnabled == enabled)
                return ReductionResult<StatelessRuleBindingEnabledOutcome>.Reject(
                    $"Active binding {id.Value} is already {(enabled ? "enabled" : "disabled")}."
                );
            ActiveRuleBinding updated = binding.WithEnabled(enabled);
            state.RuleBindings.Set(id, updated);
            facts.Stage(new StatelessRuleBindingEnabledChangedFact(updated));
            return ReductionResult<StatelessRuleBindingEnabledOutcome>.Accept(
                new StatelessRuleBindingEnabledOutcome(id, enabled)
            );
        }
    }

    internal sealed class EnableStatelessRuleBindingReducer
        : SetStatelessRuleBindingEnabledReducer,
            IOpReducer<EnableStatelessRuleBindingOp, StatelessRuleBindingEnabledOutcome>
    {
        public ReductionResult<StatelessRuleBindingEnabledOutcome> Reduce(
            ReductionContext<EnableStatelessRuleBindingOp> context,
            RulesStateDraft state,
            FactSink facts
        ) =>
            Reduce(
                context.Op.Binding,
                context.Op.ExpectedCreationOrder,
                context.Op.Source,
                true,
                state,
                facts
            );
    }

    internal sealed class DisableStatelessRuleBindingReducer
        : SetStatelessRuleBindingEnabledReducer,
            IOpReducer<DisableStatelessRuleBindingOp, StatelessRuleBindingEnabledOutcome>
    {
        public ReductionResult<StatelessRuleBindingEnabledOutcome> Reduce(
            ReductionContext<DisableStatelessRuleBindingOp> context,
            RulesStateDraft state,
            FactSink facts
        ) =>
            Reduce(
                context.Op.Binding,
                context.Op.ExpectedCreationOrder,
                context.Op.Source,
                false,
                state,
                facts
            );
    }

    internal sealed class RemoveStatelessRuleBindingReducer
        : IOpReducer<RemoveStatelessRuleBindingOp, StatelessRuleBindingRemovedOutcome>
    {
        public ReductionResult<StatelessRuleBindingRemovedOutcome> Reduce(
            ReductionContext<RemoveStatelessRuleBindingOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !StatelessBindingReduction.TryGet(
                    state,
                    context.Op.Binding,
                    context.Op.ExpectedCreationOrder,
                    context.Op.Source,
                    out ActiveRuleBinding binding,
                    out string rejection
                )
            )
                return ReductionResult<StatelessRuleBindingRemovedOutcome>.Reject(rejection);
            state.RuleBindings.Remove(context.Op.Binding);
            state.Frequencies.Remove(context.Op.Binding);
            facts.Stage(new StatelessRuleBindingRemovedFact(binding));
            return ReductionResult<StatelessRuleBindingRemovedOutcome>.Accept(
                new StatelessRuleBindingRemovedOutcome(context.Op.Binding)
            );
        }
    }

    /// <summary>Registers generic stateless-binding lifecycle reducers.</summary>
    public static class StatelessRuleBindingDispatcherExtensions
    {
        private static readonly RuleSource Source = RuleSource.FromSlug(
            "stateless-binding-lifecycle"
        );

        public static RuleDispatcherBuilder UseStatelessRuleBindingRules(
            this RuleDispatcherBuilder builder,
            RuleRegistry registry
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            return builder
                .RegisterReducer<CreateStatelessRuleBindingOp, StatelessRuleBindingCreatedOutcome>(
                    new CreateStatelessRuleBindingReducer(registry),
                    Source
                )
                .RegisterReducer<EnableStatelessRuleBindingOp, StatelessRuleBindingEnabledOutcome>(
                    new EnableStatelessRuleBindingReducer(),
                    Source
                )
                .RegisterReducer<DisableStatelessRuleBindingOp, StatelessRuleBindingEnabledOutcome>(
                    new DisableStatelessRuleBindingReducer(),
                    Source
                )
                .RegisterReducer<RemoveStatelessRuleBindingOp, StatelessRuleBindingRemovedOutcome>(
                    new RemoveStatelessRuleBindingReducer(),
                    Source
                );
        }
    }
}
