using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Identifies whether a legal condition application changed authoritative state.</summary>
    public enum ConditionApplicationStatus
    {
        /// <summary>The condition effect and binding were created.</summary>
        Applied,

        /// <summary>A game-rule prevention such as immunity accepted the request without mutation.</summary>
        Blocked,
    }

    /// <summary>Reports either one created condition or an expected game-rule prevention.</summary>
    public sealed class ConditionApplicationOutcome
    {
        private readonly ActiveEffectCreationOutcome creation;

        private ConditionApplicationOutcome(
            ConditionApplicationStatus status,
            ActiveEffectCreationOutcome creation,
            string blockedReason
        )
        {
            Status = status;
            this.creation = creation;
            BlockedReason = blockedReason ?? string.Empty;
        }

        /// <summary>Gets whether the request created state or was legally blocked.</summary>
        public ConditionApplicationStatus Status { get; }

        /// <summary>Gets the explicit game-rule reason when <see cref="Status"/> is blocked.</summary>
        public string BlockedReason { get; }

        /// <summary>Gets the created effect ID.</summary>
        /// <exception cref="InvalidOperationException">The application was blocked.</exception>
        public ActiveEffectId EffectId => RequireCreation().EffectId;

        /// <summary>Gets the created binding ID.</summary>
        /// <exception cref="InvalidOperationException">The application was blocked.</exception>
        public BindingId BindingId => RequireCreation().BindingId;

        /// <summary>Gets the created effect's initial version.</summary>
        /// <exception cref="InvalidOperationException">The application was blocked.</exception>
        public EffectStateVersion Version => RequireCreation().Version;

        internal static ConditionApplicationOutcome Applied(ActiveEffectCreationOutcome creation) =>
            new ConditionApplicationOutcome(
                ConditionApplicationStatus.Applied,
                creation,
                string.Empty
            );

        internal static ConditionApplicationOutcome Blocked(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "A blocked application requires a reason.",
                    nameof(reason)
                );
            return new ConditionApplicationOutcome(
                ConditionApplicationStatus.Blocked,
                default,
                reason.Trim()
            );
        }

        private ActiveEffectCreationOutcome RequireCreation()
        {
            if (Status != ConditionApplicationStatus.Applied)
                throw new InvalidOperationException(
                    "A blocked condition application has no created identity."
                );
            return creation;
        }
    }

    /// <summary>Requests application of one canonical sourced condition.</summary>
    public sealed class ApplyConditionOp : IRuleOp<ConditionApplicationOutcome>, IRuleSourcedOp
    {
        /// <summary>Creates a condition request from an external canonical or legacy alias name.</summary>
        public ApplyConditionOp(
            string condition,
            CreatureId target,
            CreatureId sourceCreature,
            RuleSource source,
            EffectDuration duration,
            IEffectState state
        )
        {
            if (!ConditionInputNormalizer.TryNormalize(condition, out RuleDefinitionId definition))
                throw new ArgumentException(
                    $"Unsupported condition '{condition}'.",
                    nameof(condition)
                );
            if (target.IsEmpty)
                throw new ArgumentException("A condition target is required.", nameof(target));
            if (sourceCreature.IsEmpty)
                throw new ArgumentException(
                    "A source creature is required.",
                    nameof(sourceCreature)
                );
            if (source.IsEmpty)
                throw new ArgumentException(
                    "A stable condition source is required.",
                    nameof(source)
                );
            DefinitionId = definition;
            Target = target;
            SourceCreature = sourceCreature;
            Source = source;
            Duration = duration;
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (!ConditionRuleDefinitions.Accepts(DefinitionId, State))
                throw new ArgumentException(
                    "The state does not match the canonical condition definition.",
                    nameof(state)
                );
        }

        /// <summary>Gets the normalized canonical definition.</summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>Gets the affected creature.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the creature that caused the condition.</summary>
        public CreatureId SourceCreature { get; }

        /// <inheritdoc/>
        public RuleSource Source { get; }

        /// <summary>Gets the condition duration.</summary>
        public EffectDuration Duration { get; }

        /// <summary>Gets the typed condition state.</summary>
        public IEffectState State { get; }
    }

    /// <summary>Selects whether source-wide cleanup expires or removes matching conditions.</summary>
    public enum ConditionCleanupKind
    {
        /// <summary>Expire active matches and retain tombstones.</summary>
        Expire,

        /// <summary>Remove active or expired matches and their exact bindings.</summary>
        Remove,
    }

    /// <summary>Requests deterministic cleanup of matching conditions from one stable source.</summary>
    public sealed class CleanupConditionsFromSourceOp
        : IRuleOp<ConditionCleanupOutcome>,
            IRuleSourcedOp
    {
        /// <summary>Creates one source-wide cleanup request.</summary>
        public CleanupConditionsFromSourceOp(
            RuleSource source,
            ConditionCleanupKind kind,
            CreatureId? target = null,
            RuleDefinitionId? definitionId = null
        )
        {
            if (target.HasValue && target.Value.IsEmpty)
                throw new ArgumentException(
                    "A condition target filter cannot be empty.",
                    nameof(target)
                );
            if (definitionId.HasValue && definitionId.Value.IsEmpty)
                throw new ArgumentException(
                    "A condition definition filter cannot be empty.",
                    nameof(definitionId)
                );
            if (source.IsEmpty)
                throw new ArgumentException("A stable source is required.", nameof(source));
            if (!Enum.IsDefined(typeof(ConditionCleanupKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Target = target;
            DefinitionId = definitionId;
            Source = source;
            Kind = kind;
        }

        /// <summary>Creates a source cleanup request with both typed filters.</summary>
        public CleanupConditionsFromSourceOp(
            CreatureId target,
            RuleDefinitionId definitionId,
            RuleSource source,
            ConditionCleanupKind kind
        )
            : this(source, kind, target, definitionId) { }

        /// <summary>Gets the affected creature.</summary>
        public CreatureId? Target { get; }

        /// <summary>Gets the canonical condition definition to match.</summary>
        public RuleDefinitionId? DefinitionId { get; }

        /// <inheritdoc/>
        public RuleSource Source { get; }

        /// <summary>Gets the cleanup behavior.</summary>
        public ConditionCleanupKind Kind { get; }
    }

    /// <summary>Reports exact condition identities affected by source-wide cleanup.</summary>
    public sealed class ConditionCleanupOutcome
    {
        internal ConditionCleanupOutcome(IEnumerable<ActiveEffectId> affected) =>
            Affected = Array.AsReadOnly(affected.ToArray());

        /// <summary>Gets affected effect IDs in stable binding/effect order.</summary>
        public IReadOnlyList<ActiveEffectId> Affected { get; }
    }

    internal sealed class ApplyConditionReducer
        : IOpReducer<ApplyConditionOp, ConditionApplicationOutcome>
    {
        private readonly RuleRegistry registry;

        internal ApplyConditionReducer(RuleRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public ReductionResult<ConditionApplicationOutcome> Reduce(
            ReductionContext<ApplyConditionOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !ConditionApplicationReduction.TryApply(
                    registry,
                    state,
                    facts,
                    context.SourceOpId,
                    context.Op,
                    out ConditionApplicationOutcome outcome,
                    out string rejection
                )
            )
                return ReductionResult<ConditionApplicationOutcome>.Reject(rejection);
            return ReductionResult<ConditionApplicationOutcome>.Accept(outcome);
        }
    }

    internal static class ConditionApplicationReduction
    {
        internal static bool TryApply(
            RuleRegistry registry,
            RulesStateDraft state,
            FactSink facts,
            OpId operationId,
            ApplyConditionOp operation,
            out ConditionApplicationOutcome outcome,
            out string rejection
        )
        {
            if (!state.Creatures.Contains(operation.SourceCreature))
            {
                outcome = null;
                rejection = "A freshly applied condition requires a registered source creature.";
                return false;
            }
            if (!state.Creatures.TryGet(operation.Target, out CreatureState target))
            {
                outcome = null;
                rejection = "An active-effect binding owner is not a registered creature.";
                return false;
            }
            ConditionIdentityAllocation identity = ConditionIdentityAllocator.Allocate(
                operationId,
                target.IdentityNamespace,
                state
            );
            ActiveEffectInstance effect = new ActiveEffectInstance(
                identity.EffectId,
                operation.DefinitionId,
                operation.SourceCreature,
                operation.Source,
                operation.Duration,
                operation.State
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                identity.BindingId,
                effect.DefinitionId,
                operation.Target,
                effect.Id,
                effect.Source,
                identity.CreationOrder
            );
            if (
                !ActiveEffectCreationReduction.TryCreate(
                    registry,
                    state,
                    facts,
                    effect,
                    binding,
                    out ActiveEffectCreationOutcome created,
                    out ActiveEffectCreationFailure failure,
                    out rejection
                )
            )
            {
                if (failure == ActiveEffectCreationFailure.ConditionImmune)
                {
                    outcome = ConditionApplicationOutcome.Blocked(rejection);
                    rejection = string.Empty;
                    return true;
                }
                outcome = null;
                return false;
            }
            outcome = ConditionApplicationOutcome.Applied(created);
            rejection = string.Empty;
            return true;
        }
    }

    internal readonly struct ConditionIdentityAllocation
    {
        internal ConditionIdentityAllocation(
            ActiveEffectId effectId,
            BindingId bindingId,
            long creationOrder
        )
        {
            EffectId = effectId;
            BindingId = bindingId;
            CreationOrder = creationOrder;
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal long CreationOrder { get; }
    }

    internal static class ConditionIdentityAllocator
    {
        internal static ConditionIdentityAllocation Allocate(
            OpId frameId,
            GeneratedIdentityNamespace targetNamespace,
            RulesStateDraft state
        )
        {
            if (frameId.IsEmpty)
                throw new ArgumentException("A condition frame ID is required.", nameof(frameId));
            if (targetNamespace.IsEmpty)
                throw new ArgumentException(
                    "A condition target identity namespace is required.",
                    nameof(targetNamespace)
                );
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            long maximumCreationOrder = 0;
            foreach (KeyValuePair<BindingId, ActiveRuleBinding> pair in state.RuleBindings)
                maximumCreationOrder = Math.Max(maximumCreationOrder, pair.Value.CreationOrder);
            foreach (
                KeyValuePair<
                    ActiveEffectId,
                    ActiveEffectTimingState
                > pair in state.ActiveEffectTimings
            )
                maximumCreationOrder = Math.Max(maximumCreationOrder, pair.Value.CreationOrder);
            if (maximumCreationOrder == long.MaxValue)
                throw new InvalidOperationException(
                    "The condition identity sequence is exhausted."
                );

            long candidate = Math.Max(frameId.Value, maximumCreationOrder + 1);
            while (true)
            {
                ActiveEffectId effectId = new ActiveEffectId(
                    $"condition-effect-{targetNamespace.Value}-{candidate}"
                );
                BindingId bindingId = new BindingId(
                    $"condition-binding-{targetNamespace.Value}-{candidate}"
                );
                if (
                    !state.ActiveEffects.Contains(effectId)
                    && !state.RuleBindings.Contains(bindingId)
                )
                    return new ConditionIdentityAllocation(effectId, bindingId, candidate);
                if (candidate == long.MaxValue)
                    throw new InvalidOperationException(
                        "The condition identity sequence is exhausted."
                    );
                candidate++;
            }
        }
    }

    internal sealed class CleanupConditionsFromSourceReducer
        : IOpReducer<CleanupConditionsFromSourceOp, ConditionCleanupOutcome>
    {
        private readonly ExpireActiveEffectReducer expire = new ExpireActiveEffectReducer();
        private readonly RemoveActiveEffectReducer remove = new RemoveActiveEffectReducer();

        public ReductionResult<ConditionCleanupOutcome> Reduce(
            ReductionContext<CleanupConditionsFromSourceOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            ActiveRuleBinding[] candidates = state
                .RuleBindings.Select(pair => pair.Value)
                .Where(binding =>
                    binding.Source == context.Op.Source
                    && ConditionRuleDefinitions.IsConditionDefinition(binding.DefinitionId)
                    && (!context.Op.Target.HasValue || binding.Owner == context.Op.Target.Value)
                    && (
                        !context.Op.DefinitionId.HasValue
                        || binding.DefinitionId == context.Op.DefinitionId.Value
                    )
                )
                .OrderBy(binding => binding.CreationOrder)
                .ThenBy(binding => binding.Id.Value, StringComparer.Ordinal)
                .ThenBy(binding => binding.EffectId?.Value, StringComparer.Ordinal)
                .ToArray();
            List<ConditionSelection<IEffectState>> matches =
                new List<ConditionSelection<IEffectState>>();
            foreach (ActiveRuleBinding binding in candidates)
            {
                if (
                    !binding.EffectId.HasValue
                    || !state.ActiveEffects.TryGet(
                        binding.EffectId.Value,
                        out ActiveEffectInstance effect
                    )
                    || effect.DefinitionId != binding.DefinitionId
                    || effect.Source != binding.Source
                    || !ConditionRuleDefinitions.Accepts(effect.DefinitionId, effect.State)
                )
                    return ReductionResult<ConditionCleanupOutcome>.Reject(
                        $"Source cleanup found invalid binding {binding.Id.Value}."
                    );
                if ((effect.Status == ActiveEffectStatus.Active) != binding.IsEnabled)
                    return ReductionResult<ConditionCleanupOutcome>.Reject(
                        $"Source cleanup found conflicting lifecycle state for {effect.Id.Value}."
                    );
                if (
                    context.Op.Kind == ConditionCleanupKind.Expire
                    && effect.Status == ActiveEffectStatus.Expired
                )
                    continue;
                matches.Add(new ConditionSelection<IEffectState>(effect, binding, effect.State));
            }

            List<ActiveEffectId> affected = new List<ActiveEffectId>();
            foreach (ConditionSelection<IEffectState> match in matches)
            {
                if (context.Op.Kind == ConditionCleanupKind.Expire)
                {
                    ReductionResult<ActiveEffectExpirationOutcome> result = expire.Reduce(
                        ConditionReduction.Translate(
                            context,
                            new ExpireActiveEffectOp(
                                match.EffectId,
                                match.BindingId,
                                match.Version,
                                context.Op.Source
                            )
                        ),
                        state,
                        facts
                    );
                    if (result.IsRejected)
                        return ReductionResult<ConditionCleanupOutcome>.Reject(
                            result.RejectionReason
                        );
                }
                else
                {
                    ReductionResult<ActiveEffectRemovalOutcome> result = remove.Reduce(
                        ConditionReduction.Translate(
                            context,
                            new RemoveActiveEffectOp(
                                match.EffectId,
                                match.BindingId,
                                match.Version,
                                context.Op.Source
                            )
                        ),
                        state,
                        facts
                    );
                    if (result.IsRejected)
                        return ReductionResult<ConditionCleanupOutcome>.Reject(
                            result.RejectionReason
                        );
                }
                affected.Add(match.EffectId);
            }
            return ReductionResult<ConditionCleanupOutcome>.Accept(
                new ConditionCleanupOutcome(affected)
            );
        }
    }
}
