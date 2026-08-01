using System;

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
            {
                return Delegate(context, state, facts);
            }
            if (
                !ConditionReduction.IsCondition(effect)
                || !ConditionRuleDefinitions.Accepts(effect.DefinitionId, context.Op.State)
            )
            {
                return ReductionResult<ConditionStateUpdateOutcome>.Reject(
                    ConditionReduction.InvalidContract(effect.DefinitionId, context.Op.State)
                );
            }

            return Delegate(context, state, facts);
        }

        private ReductionResult<ConditionStateUpdateOutcome> Delegate(
            ReductionContext<UpdateConditionStateOp> context,
            RulesStateDraft state,
            FactSink facts
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
                    updated.Id,
                    updated.DefinitionId,
                    result.Value.PreviousVersion,
                    result.Value.CurrentVersion,
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
                state.ActiveEffects.TryGet(context.Op.EffectId, out ActiveEffectInstance effect)
                && !ConditionReduction.IsCondition(effect)
            )
            {
                return ReductionResult<ConditionExpirationOutcome>.Reject(
                    ConditionReduction.InvalidContract(effect)
                );
            }

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

            if (
                !state.ActiveEffects.TryGet(result.Value.EffectId, out ActiveEffectInstance expired)
            )
                throw new InvalidOperationException(
                    "An accepted condition expiration lost its effect."
                );
            facts.Stage(
                new ConditionExpiredFact(
                    expired.Id,
                    expired.DefinitionId,
                    context.Op.BindingId,
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
            ActiveEffectInstance effect = null;
            if (
                state.ActiveEffects.TryGet(context.Op.EffectId, out effect)
                && !ConditionReduction.IsCondition(effect)
            )
            {
                return ReductionResult<ConditionRemovalOutcome>.Reject(
                    ConditionReduction.InvalidContract(effect)
                );
            }

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

            facts.Stage(
                new ConditionRemovedFact(
                    effect.Id,
                    effect.DefinitionId,
                    result.Value.BindingId,
                    effect.EffectStateVersion,
                    effect.Status
                )
            );
            return ReductionResult<ConditionRemovalOutcome>.Accept(
                new ConditionRemovalOutcome(result.Value.EffectId, result.Value.BindingId)
            );
        }
    }
}
