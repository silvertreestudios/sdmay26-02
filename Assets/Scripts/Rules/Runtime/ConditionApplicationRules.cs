using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Pairs one immutable condition effect with its exact active binding.</summary>
    public sealed class ConditionRegistration
    {
        /// <summary>Creates one immutable registration pair.</summary>
        public ConditionRegistration(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            ActiveEffectTimingState timing = null
        )
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            Timing = timing;
            if (!ConditionRuleDefinitions.Accepts(effect.DefinitionId, effect.State))
                throw new ArgumentException(
                    "The effect is not a canonical condition.",
                    nameof(effect)
                );
            if (
                !binding.EffectId.HasValue
                || binding.EffectId.Value != effect.Id
                || binding.DefinitionId != effect.DefinitionId
                || binding.Source != effect.Source
            )
                throw new ArgumentException(
                    "The binding does not match the condition effect.",
                    nameof(binding)
                );
            if (
                timing != null
                && (
                    timing.Effect != effect.Id
                    || timing.Binding != binding.Id
                    || timing.SourceCreature != effect.SourceCreature
                    || timing.CreationOrder != binding.CreationOrder
                )
            )
                throw new ArgumentException(
                    "The timing does not match the condition registration.",
                    nameof(timing)
                );
        }

        /// <summary>Gets the active-effect half of the registration.</summary>
        public ActiveEffectInstance Effect { get; }

        /// <summary>Gets the exact active binding.</summary>
        public ActiveRuleBinding Binding { get; }

        /// <summary>Gets the exact encounter timing, when the active finite effect is scheduled.</summary>
        public ActiveEffectTimingState Timing { get; }
    }

    /// <summary>Requests creation of one canonical sourced condition.</summary>
    public sealed class ApplyConditionOp : IRuleOp<ConditionCreationOutcome>, IRuleSourcedOp
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

    /// <summary>Idempotently adopts prepared condition registration pairs into one store.</summary>
    public sealed class AdoptConditionRegistrationsOp
        : IRuleOp<ConditionAdoptionOutcome>,
            IRuleSourcedOp
    {
        private readonly IReadOnlyList<ConditionRegistration> registrations;

        /// <summary>Creates one adoption request for prepared persistence or enrollment state.</summary>
        public AdoptConditionRegistrationsOp(IEnumerable<ConditionRegistration> registrations)
        {
            if (registrations == null)
                throw new ArgumentNullException(nameof(registrations));
            ConditionRegistration[] copied = registrations.ToArray();
            if (copied.Any(registration => registration == null))
                throw new ArgumentException(
                    "Registrations cannot contain null.",
                    nameof(registrations)
                );
            this.registrations = Array.AsReadOnly(copied);
        }

        /// <summary>Gets prepared registrations in deterministic adoption order.</summary>
        public IReadOnlyList<ConditionRegistration> Registrations => registrations;

        /// <inheritdoc/>
        public RuleSource Source => RuleSource.FromSlug("condition-enrollment");
    }

    /// <summary>Reports how many prepared registrations were newly committed.</summary>
    public readonly struct ConditionAdoptionOutcome
    {
        internal ConditionAdoptionOutcome(int created) => Created = created;

        /// <summary>Gets the number of newly created registrations.</summary>
        public int Created { get; }
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

    internal sealed class ApplyConditionHandler
        : IOpHandler<ApplyConditionOp, ConditionCreationOutcome>
    {
        public async ValueTask<ConditionCreationOutcome> Handle(
            OpFrame<ApplyConditionOp> frame,
            OpHandlerContext context
        )
        {
            string suffix = frame.Id.Value.ToString();
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId($"condition-effect-{suffix}"),
                frame.Op.DefinitionId,
                frame.Op.SourceCreature,
                frame.Op.Source,
                frame.Op.Duration,
                frame.Op.State
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId($"condition-binding-{suffix}"),
                effect.DefinitionId,
                frame.Op.Target,
                effect.Id,
                effect.Source,
                frame.Id.Value
            );
            OpResult<ConditionCreationOutcome> created = await context.Dispatch(
                new CreateConditionOp(effect, binding)
            );
            return created switch
            {
                ResolvedOpResult<ConditionCreationOutcome> resolved => resolved.Value,
                InvalidOpResult<ConditionCreationOutcome> invalid =>
                    throw new InvalidOperationException(invalid.Reason),
                _ => throw new InvalidOperationException("Condition creation did not resolve."),
            };
        }
    }

    internal sealed class AdoptConditionRegistrationsReducer
        : IOpReducer<AdoptConditionRegistrationsOp, ConditionAdoptionOutcome>
    {
        private readonly RuleRegistry registry;

        internal AdoptConditionRegistrationsReducer(RuleRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public ReductionResult<ConditionAdoptionOutcome> Reduce(
            ReductionContext<AdoptConditionRegistrationsOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            List<ConditionRegistration> pending = new List<ConditionRegistration>();
            HashSet<ActiveEffectId> effectIds = new HashSet<ActiveEffectId>();
            HashSet<BindingId> bindingIds = new HashSet<BindingId>();
            foreach (ConditionRegistration registration in context.Op.Registrations)
            {
                if (
                    !effectIds.Add(registration.Effect.Id)
                    || !bindingIds.Add(registration.Binding.Id)
                )
                    return ReductionResult<ConditionAdoptionOutcome>.Reject(
                        "A condition adoption batch contains duplicate stable identities."
                    );
                if (!TryValidate(registration, state, out string rejection))
                    return ReductionResult<ConditionAdoptionOutcome>.Reject(rejection);

                bool hasEffect = state.ActiveEffects.TryGet(
                    registration.Effect.Id,
                    out ActiveEffectInstance effect
                );
                bool hasBinding = state.RuleBindings.TryGet(
                    registration.Binding.Id,
                    out ActiveRuleBinding binding
                );
                if (hasEffect || hasBinding)
                {
                    bool hasTiming = state.ActiveEffectTimings.TryGet(
                        registration.Effect.Id,
                        out ActiveEffectTimingState timing
                    );
                    ActiveEffectTimingState expectedTiming = ResolveTiming(registration, state);
                    if (
                        hasEffect
                        && hasBinding
                        && effect.Equals(registration.Effect)
                        && binding.Equals(registration.Binding)
                        && hasTiming == (expectedTiming != null)
                        && (!hasTiming || timing.Equals(expectedTiming))
                    )
                        continue;
                    return ReductionResult<ConditionAdoptionOutcome>.Reject(
                        "A condition registration ID is already used by different state."
                    );
                }
                pending.Add(registration);
            }

            foreach (ConditionRegistration registration in pending)
            {
                ActiveEffectTimingState timing = ResolveTiming(registration, state);
                ActiveEffectReduction.CommitCreation(
                    state,
                    registration.Effect,
                    registration.Binding,
                    timing,
                    facts
                );
                facts.Stage(new ConditionCreatedFact(registration.Effect, registration.Binding));
            }
            return ReductionResult<ConditionAdoptionOutcome>.Accept(
                new ConditionAdoptionOutcome(pending.Count)
            );
        }

        private bool TryValidate(
            ConditionRegistration registration,
            RulesStateDraft state,
            out string rejection
        )
        {
            ActiveEffectInstance effect = registration.Effect;
            ActiveRuleBinding binding = registration.Binding;
            if (
                !ActiveEffectReduction.TryValidateRegistration(
                    registry,
                    effect,
                    binding,
                    out rejection
                )
            )
                return false;
            if ((effect.Status == ActiveEffectStatus.Active) != binding.IsEnabled)
            {
                rejection = "A restored condition's lifecycle status and binding state disagree.";
                return false;
            }
            if (
                registration.Timing != null
                && (
                    effect.Status != ActiveEffectStatus.Active
                    || effect.Duration.Kind == EffectDurationKind.Indefinite
                )
            )
            {
                rejection = "Only an active finite condition can retain encounter timing.";
                return false;
            }
            EncounterState encounter = ActiveEncounter(state);
            if (registration.Timing != null)
            {
                if (encounter != null && registration.Timing.Encounter != encounter.Id)
                {
                    rejection = "Restored condition timing belongs to a different encounter.";
                    return false;
                }
                if (
                    registration.Timing.ExpiresWithEncounter
                    != (effect.Duration.Kind == EffectDurationKind.Encounter)
                )
                {
                    rejection = "Restored condition timing disagrees with its duration kind.";
                    return false;
                }
            }
            if (
                encounter != null
                && effect.Status == ActiveEffectStatus.Active
                && effect.Duration.Kind != EffectDurationKind.Indefinite
                && !encounter.Roster.Any(entry => entry.Creature == effect.SourceCreature)
            )
            {
                rejection = "The condition source is not in the active encounter roster.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        private static ActiveEffectTimingState ResolveTiming(
            ConditionRegistration registration,
            RulesStateDraft state
        )
        {
            if (registration.Timing != null)
                return registration.Timing;
            ActiveEffectInstance effect = registration.Effect;
            EncounterState encounter = ActiveEncounter(state);
            return
                effect.Status == ActiveEffectStatus.Active
                && effect.Duration.Kind != EffectDurationKind.Indefinite
                && encounter != null
                ? ActiveEffectTimingState.ForEncounter(effect, registration.Binding, encounter)
                : null;
        }

        private static EncounterState ActiveEncounter(RulesStateDraft state) =>
            state
                .Encounters.Select(pair => pair.Value)
                .FirstOrDefault(value => value.Phase == EncounterPhase.Active);
    }

    internal sealed class CleanupConditionsFromSourceReducer
        : IOpReducer<CleanupConditionsFromSourceOp, ConditionCleanupOutcome>
    {
        private readonly ExpireConditionReducer expire = new ExpireConditionReducer();
        private readonly RemoveConditionReducer remove = new RemoveConditionReducer();

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
                    ReductionResult<ConditionExpirationOutcome> result = expire.Reduce(
                        ConditionReduction.Translate(
                            context,
                            new ExpireConditionOp(
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
                    ReductionResult<ConditionRemovalOutcome> result = remove.Reduce(
                        ConditionReduction.Translate(
                            context,
                            new RemoveConditionOp(
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
