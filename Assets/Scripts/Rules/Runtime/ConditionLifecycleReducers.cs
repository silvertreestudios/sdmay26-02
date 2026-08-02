using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class ConditionReduction
    {
        internal static bool IsCondition(ActiveEffectInstance effect) =>
            effect != null && ConditionRuleDefinitions.Accepts(effect.DefinitionId, effect.State);

        internal static ReductionContext<TOp> Translate<TConditionOp, TOp>(
            ReductionContext<TConditionOp> context,
            TOp op
        )
        {
            return new ReductionContext<TOp>(
                op,
                context.SourceOpId,
                context.RootOpId,
                context.Source
            );
        }

        internal static string InvalidContract(ActiveEffectInstance effect) =>
            InvalidContract(effect.DefinitionId, effect.State);

        internal static string InvalidContract(RuleDefinitionId definitionId, IEffectState state) =>
            $"Rule definition {definitionId.Value} does not accept condition state "
            + $"{state.GetType().Name}.";

        internal static bool TryGetBinding(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            out ActiveRuleBinding binding,
            out string rejection
        )
        {
            ActiveRuleBinding[] matches = state
                .RuleBindings.Select(pair => pair.Value)
                .Where(candidate =>
                    candidate.EffectId.HasValue && candidate.EffectId.Value == effect.Id
                )
                .ToArray();
            if (matches.Length != 1)
            {
                binding = null;
                rejection = $"Condition effect {effect.Id.Value} requires exactly one binding.";
                return false;
            }
            binding = matches[0];
            return ActiveEffectReduction.TryGetAssociatedBinding(
                state,
                effect,
                binding.Id,
                true,
                out binding,
                out rejection
            );
        }
    }

    /// <summary>Enforces compiled condition immunity wherever active condition authority enters.</summary>
    internal static class ConditionImmunityValidation
    {
        internal static bool TryValidateActive(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            out string rejection
        )
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (
                effect.Status != ActiveEffectStatus.Active
                || !ConditionReduction.IsCondition(effect)
            )
            {
                rejection = string.Empty;
                return true;
            }
            if (!state.Creatures.Contains(binding.Owner))
            {
                rejection = "The condition owner is not a registered creature.";
                return false;
            }
            if (!state.PreparedInputs.TryGet(binding.Owner, out PreparedCreatureInputs prepared))
                throw new InvalidOperationException(
                    $"Registered condition owner {binding.Owner.Value} has no authoritative prepared inputs."
                );
            return TryValidateActive(effect, prepared, out rejection);
        }

        internal static void ValidateStateInvariant(
            IReadOnlyDictionary<CreatureId, CreatureState> creatures,
            IReadOnlyDictionary<CreatureId, PreparedCreatureInputs> preparedInputs,
            IReadOnlyDictionary<ActiveEffectId, ActiveEffectInstance> activeEffects,
            IReadOnlyDictionary<BindingId, ActiveRuleBinding> ruleBindings
        )
        {
            foreach (ActiveRuleBinding binding in ruleBindings.Values)
            {
                if (
                    !binding.IsEnabled
                    || !binding.EffectId.HasValue
                    || !activeEffects.TryGetValue(
                        binding.EffectId.Value,
                        out ActiveEffectInstance effect
                    )
                    || effect.Status != ActiveEffectStatus.Active
                    || !ActiveEffectRegistration.BindingMatchesEffect(effect, binding)
                    || !ConditionReduction.IsCondition(effect)
                )
                    continue;
                if (!creatures.ContainsKey(binding.Owner))
                    throw new InvalidOperationException(
                        $"Active condition owner {binding.Owner.Value} is not a registered creature."
                    );
                if (!preparedInputs.TryGetValue(binding.Owner, out PreparedCreatureInputs prepared))
                    throw new InvalidOperationException(
                        $"Registered condition owner {binding.Owner.Value} has no authoritative prepared inputs."
                    );
                if (!TryValidateActive(effect, prepared, out string rejection))
                    throw new InvalidOperationException(rejection);
            }
        }

        private static bool TryValidateActive(
            ActiveEffectInstance effect,
            PreparedCreatureInputs prepared,
            out string rejection
        )
        {
            if (
                ConditionRuleDefinitions.TryGetCanonicalSlug(
                    effect.DefinitionId,
                    out string conditionSlug
                )
                && prepared.Immunities.Any(immunity =>
                    immunity.Kind == PreparedImmunityKind.Condition
                    && ConditionInputNormalizer.TryNormalize(
                        immunity.Type,
                        out RuleDefinitionId immunityDefinition
                    )
                    && immunityDefinition == effect.DefinitionId
                )
            )
            {
                rejection = $"The condition owner is immune to {conditionSlug}.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }
    }

    internal sealed class CreateConditionReducer
        : IOpReducer<CreateConditionOp, ConditionCreationOutcome>
    {
        private readonly CreateActiveEffectReducer activeEffectReducer;

        internal CreateConditionReducer(RuleRegistry registry) =>
            activeEffectReducer = new CreateActiveEffectReducer(
                registry ?? throw new ArgumentNullException(nameof(registry))
            );

        public ReductionResult<ConditionCreationOutcome> Reduce(
            ReductionContext<CreateConditionOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            ActiveEffectInstance effect = context.Op.Effect;
            if (!ConditionReduction.IsCondition(effect))
                return ReductionResult<ConditionCreationOutcome>.Reject(
                    ConditionReduction.InvalidContract(effect)
                );
            if (!state.Creatures.Contains(effect.SourceCreature))
                return ReductionResult<ConditionCreationOutcome>.Reject(
                    "The condition source is not a registered creature."
                );

            CreateActiveEffectOp translated = new CreateActiveEffectOp(effect, context.Op.Binding);
            ReductionResult<ActiveEffectCreationOutcome> result = activeEffectReducer.Reduce(
                ConditionReduction.Translate(context, translated),
                state,
                facts
            );
            if (result.IsRejected)
                return ReductionResult<ConditionCreationOutcome>.Reject(result.RejectionReason);

            facts.Stage(new ConditionCreatedFact(effect, context.Op.Binding));
            return ReductionResult<ConditionCreationOutcome>.Accept(
                new ConditionCreationOutcome(
                    result.Value.EffectId,
                    result.Value.BindingId,
                    result.Value.Version
                )
            );
        }
    }

    internal sealed class UpdateConditionStateReducer
        : IOpReducer<UpdateConditionStateOp, ConditionStateUpdateOutcome>
    {
        private readonly UpdateActiveEffectStateReducer activeEffectReducer =
            new UpdateActiveEffectStateReducer();

        public ReductionResult<ConditionStateUpdateOutcome> Reduce(
            ReductionContext<UpdateConditionStateOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!state.ActiveEffects.TryGet(context.Op.EffectId, out ActiveEffectInstance effect))
                return ReductionResult<ConditionStateUpdateOutcome>.Reject(
                    $"Active effect {context.Op.EffectId.Value} is unknown."
                );
            if (
                !ConditionReduction.IsCondition(effect)
                || !ConditionRuleDefinitions.Accepts(effect.DefinitionId, context.Op.State)
                || context.Op.Source != effect.Source
            )
            {
                return ReductionResult<ConditionStateUpdateOutcome>.Reject(
                    ConditionReduction.InvalidContract(effect.DefinitionId, context.Op.State)
                );
            }

            if (
                !ConditionReduction.TryGetBinding(
                    state,
                    effect,
                    out ActiveRuleBinding binding,
                    out string rejection
                )
            )
                return ReductionResult<ConditionStateUpdateOutcome>.Reject(rejection);

            return Delegate(context, state, facts, effect, binding);
        }

        private ReductionResult<ConditionStateUpdateOutcome> Delegate(
            ReductionContext<UpdateConditionStateOp> context,
            RulesStateDraft state,
            FactSink facts,
            ActiveEffectInstance previous,
            ActiveRuleBinding binding
        )
        {
            UpdateActiveEffectStateOp translated = UpdateActiveEffectStateOp.Create(
                context.Op.EffectId,
                context.Op.ExpectedVersion,
                context.Op.State,
                context.Op.Source
            );
            ReductionResult<ActiveEffectStateUpdateOutcome> result = activeEffectReducer.Reduce(
                ConditionReduction.Translate(context, translated),
                state,
                facts
            );
            if (result.IsRejected)
                return ReductionResult<ConditionStateUpdateOutcome>.Reject(result.RejectionReason);

            if (
                !state.ActiveEffects.TryGet(result.Value.EffectId, out ActiveEffectInstance updated)
            )
                throw new InvalidOperationException(
                    "An accepted condition update lost its effect."
                );
            facts.Stage(
                new ConditionStateUpdatedFact(
                    updated,
                    binding,
                    result.Value.PreviousVersion,
                    result.Value.CurrentVersion,
                    previous.State,
                    updated.State
                )
            );
            return ReductionResult<ConditionStateUpdateOutcome>.Accept(
                new ConditionStateUpdateOutcome(
                    result.Value.EffectId,
                    result.Value.PreviousVersion,
                    result.Value.CurrentVersion
                )
            );
        }
    }

    internal sealed class ExpireConditionReducer
        : IOpReducer<ExpireConditionOp, ConditionExpirationOutcome>
    {
        private readonly ExpireActiveEffectReducer activeEffectReducer =
            new ExpireActiveEffectReducer();

        public ReductionResult<ConditionExpirationOutcome> Reduce(
            ReductionContext<ExpireConditionOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !ActiveEffectReduction.TryGetCurrent(
                    state,
                    context.Op.EffectId,
                    context.Op.ExpectedVersion,
                    true,
                    out ActiveEffectInstance effect,
                    out string rejection
                )
                || !ConditionReduction.IsCondition(effect)
                || context.Op.Source != effect.Source
            )
                return ReductionResult<ConditionExpirationOutcome>.Reject(
                    rejection.Length > 0
                        ? rejection
                        : "The requested source does not own this canonical condition."
                );
            if (
                !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    context.Op.BindingId,
                    true,
                    out ActiveRuleBinding binding,
                    out rejection
                )
            )
                return ReductionResult<ConditionExpirationOutcome>.Reject(rejection);

            ExpireActiveEffectOp translated = new ExpireActiveEffectOp(
                context.Op.EffectId,
                context.Op.BindingId,
                context.Op.ExpectedVersion,
                context.Op.Source
            );
            ReductionResult<ActiveEffectExpirationOutcome> result = activeEffectReducer.Reduce(
                ConditionReduction.Translate(context, translated),
                state,
                facts
            );
            if (result.IsRejected)
                return ReductionResult<ConditionExpirationOutcome>.Reject(result.RejectionReason);

            facts.Stage(
                new ConditionExpiredFact(
                    effect,
                    binding,
                    context.Op.ExpectedVersion,
                    result.Value.Version
                )
            );
            return ReductionResult<ConditionExpirationOutcome>.Accept(
                new ConditionExpirationOutcome(result.Value.EffectId, result.Value.Version)
            );
        }
    }

    internal sealed class RemoveConditionReducer
        : IOpReducer<RemoveConditionOp, ConditionRemovalOutcome>
    {
        private readonly RemoveActiveEffectReducer activeEffectReducer =
            new RemoveActiveEffectReducer();

        public ReductionResult<ConditionRemovalOutcome> Reduce(
            ReductionContext<RemoveConditionOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !ActiveEffectReduction.TryGetCurrent(
                    state,
                    context.Op.EffectId,
                    context.Op.ExpectedVersion,
                    false,
                    out ActiveEffectInstance effect,
                    out string rejection
                )
                || !ConditionReduction.IsCondition(effect)
                || context.Op.Source != effect.Source
            )
                return ReductionResult<ConditionRemovalOutcome>.Reject(
                    rejection.Length > 0
                        ? rejection
                        : "The requested source does not own this canonical condition."
                );
            if (
                !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    context.Op.BindingId,
                    false,
                    out ActiveRuleBinding binding,
                    out rejection
                )
            )
                return ReductionResult<ConditionRemovalOutcome>.Reject(rejection);

            RemoveActiveEffectOp translated = new RemoveActiveEffectOp(
                context.Op.EffectId,
                context.Op.BindingId,
                context.Op.ExpectedVersion,
                context.Op.Source
            );
            ReductionResult<ActiveEffectRemovalOutcome> result = activeEffectReducer.Reduce(
                ConditionReduction.Translate(context, translated),
                state,
                facts
            );
            if (result.IsRejected)
                return ReductionResult<ConditionRemovalOutcome>.Reject(result.RejectionReason);

            facts.Stage(new ConditionRemovedFact(effect, binding));
            return ReductionResult<ConditionRemovalOutcome>.Accept(
                new ConditionRemovalOutcome(result.Value.EffectId, result.Value.BindingId)
            );
        }
    }
}
