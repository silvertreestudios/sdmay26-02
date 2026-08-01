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
        public ConditionRegistration(ActiveEffectInstance effect, ActiveRuleBinding binding)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
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
        }

        /// <summary>Gets the active-effect half of the registration.</summary>
        public ActiveEffectInstance Effect { get; }

        /// <summary>Gets the exact active binding.</summary>
        public ActiveRuleBinding Binding { get; }
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
            CreatureId target,
            RuleDefinitionId definitionId,
            RuleSource source,
            ConditionCleanupKind kind
        )
        {
            if (target.IsEmpty)
                throw new ArgumentException("A condition target is required.", nameof(target));
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A condition definition is required.",
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

        /// <summary>Gets the affected creature.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the canonical condition definition to match.</summary>
        public RuleDefinitionId DefinitionId { get; }

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

    internal sealed class AdoptConditionRegistrationsHandler
        : IOpHandler<AdoptConditionRegistrationsOp, ConditionAdoptionOutcome>
    {
        public async ValueTask<ConditionAdoptionOutcome> Handle(
            OpFrame<AdoptConditionRegistrationsOp> frame,
            OpHandlerContext context
        )
        {
            int created = 0;
            foreach (ConditionRegistration registration in frame.Op.Registrations)
            {
                bool hasEffect = context.Snapshot.ActiveEffects.TryGet(
                    registration.Effect.Id,
                    out ActiveEffectInstance effect
                );
                bool hasBinding = context.Snapshot.RuleBindings.TryGet(
                    registration.Binding.Id,
                    out ActiveRuleBinding binding
                );
                if (hasEffect || hasBinding)
                {
                    if (
                        hasEffect
                        && hasBinding
                        && effect.Equals(registration.Effect)
                        && binding.Equals(registration.Binding)
                    )
                        continue;
                    throw new InvalidOperationException(
                        "A condition registration ID is already used by different state."
                    );
                }

                OpResult<ConditionCreationOutcome> result = await context.Dispatch(
                    new CreateConditionOp(registration.Effect, registration.Binding)
                );
                if (result is not ResolvedOpResult<ConditionCreationOutcome>)
                    throw new InvalidOperationException("Prepared condition adoption failed.");
                created++;
            }
            return new ConditionAdoptionOutcome(created);
        }
    }

    internal sealed class CleanupConditionsFromSourceHandler
        : IOpHandler<CleanupConditionsFromSourceOp, ConditionCleanupOutcome>
    {
        public async ValueTask<ConditionCleanupOutcome> Handle(
            OpFrame<CleanupConditionsFromSourceOp> frame,
            OpHandlerContext context
        )
        {
            ConditionSelection<IEffectState>[] matches = context
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .Where(binding =>
                    binding.Owner == frame.Op.Target
                    && binding.DefinitionId == frame.Op.DefinitionId
                    && binding.Source == frame.Op.Source
                    && binding.EffectId.HasValue
                )
                .Select(binding =>
                {
                    if (
                        !context.Snapshot.ActiveEffects.TryGet(
                            binding.EffectId.Value,
                            out ActiveEffectInstance effect
                        )
                        || effect.DefinitionId != binding.DefinitionId
                        || effect.Source != binding.Source
                        || !ConditionRuleDefinitions.Accepts(effect.DefinitionId, effect.State)
                        || (
                            frame.Op.Kind == ConditionCleanupKind.Expire
                            && (effect.Status != ActiveEffectStatus.Active || !binding.IsEnabled)
                        )
                    )
                        return null;
                    return new ConditionSelection<IEffectState>(effect, binding, effect.State);
                })
                .Where(selection => selection != null)
                .OrderBy(selection => selection.Binding.CreationOrder)
                .ThenBy(selection => selection.BindingId.Value, StringComparer.Ordinal)
                .ThenBy(selection => selection.EffectId.Value, StringComparer.Ordinal)
                .ToArray();
            List<ActiveEffectId> affected = new List<ActiveEffectId>();
            foreach (ConditionSelection<IEffectState> match in matches)
            {
                if (frame.Op.Kind == ConditionCleanupKind.Expire)
                {
                    OpResult<ConditionExpirationOutcome> expired = await context.Dispatch(
                        new ExpireConditionOp(
                            match.EffectId,
                            match.BindingId,
                            match.Version,
                            frame.Op.Source
                        )
                    );
                    if (expired is not ResolvedOpResult<ConditionExpirationOutcome>)
                        throw new InvalidOperationException(
                            "Condition expiration failed during source cleanup."
                        );
                }
                else
                {
                    OpResult<ConditionRemovalOutcome> removed = await context.Dispatch(
                        new RemoveConditionOp(
                            match.EffectId,
                            match.BindingId,
                            match.Version,
                            frame.Op.Source
                        )
                    );
                    if (removed is not ResolvedOpResult<ConditionRemovalOutcome>)
                        throw new InvalidOperationException(
                            "Condition removal failed during source cleanup."
                        );
                }
                affected.Add(match.EffectId);
            }
            return new ConditionCleanupOutcome(affected);
        }
    }
}
