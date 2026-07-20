using System;

namespace Game.Rules.Runtime
{
    internal static class ActiveEffectReduction
    {
        public static bool TryGetCurrent(
            RulesStateDraft state,
            ActiveEffectId effectId,
            EffectStateVersion expectedVersion,
            bool requireActive,
            out ActiveEffectInstance effect,
            out string rejection
        )
        {
            if (!state.ActiveEffects.TryGet(effectId, out effect))
            {
                rejection = $"Active effect {effectId.Value} is unknown.";
                return false;
            }
            if (requireActive && effect.Status == ActiveEffectStatus.Expired)
            {
                rejection = $"Active effect {effectId.Value} has expired.";
                return false;
            }
            if (effect.EffectStateVersion != expectedVersion)
            {
                rejection =
                    $"Active effect {effectId.Value} expected version {expectedVersion.Value}, "
                    + $"but current version is {effect.EffectStateVersion.Value}.";
                return false;
            }

            rejection = string.Empty;
            return true;
        }

        public static bool TryGetAssociatedBinding(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            BindingId bindingId,
            bool requireEnabled,
            out ActiveRuleBinding binding,
            out string rejection
        )
        {
            if (!state.RuleBindings.TryGet(bindingId, out binding))
            {
                rejection = $"Active binding {bindingId.Value} is unknown.";
                return false;
            }
            if (
                !binding.EffectId.HasValue
                || binding.EffectId.Value != effect.Id
                || binding.DefinitionId != effect.DefinitionId
                || binding.Source != effect.Source
            )
            {
                rejection =
                    $"Active binding {bindingId.Value} is not associated with effect {effect.Id.Value}.";
                return false;
            }
            if (requireEnabled && !binding.IsEnabled)
            {
                rejection = $"Active binding {bindingId.Value} is already disabled.";
                return false;
            }

            rejection = string.Empty;
            return true;
        }
    }

    internal sealed class CreateActiveEffectReducer
        : IOpReducer<CreateActiveEffectOp, ActiveEffectCreationOutcome>
    {
        private readonly RuleRegistry registry;

        public CreateActiveEffectReducer(RuleRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public ReductionResult<ActiveEffectCreationOutcome> Reduce(
            ReductionContext<CreateActiveEffectOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            ActiveEffectInstance effect = context.Op.Effect;
            ActiveRuleBinding binding = context.Op.Binding;
            if (state.ActiveEffects.Contains(effect.Id))
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    $"Active effect {effect.Id.Value} already exists."
                );
            }
            if (state.RuleBindings.Contains(binding.Id))
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    $"Active binding {binding.Id.Value} already exists."
                );
            }
            if (!registry.TryGetDefinition(effect.DefinitionId, out _))
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    $"Rule definition {effect.DefinitionId.Value} is unknown."
                );
            }
            if (effect.EffectStateVersion != EffectStateVersion.Initial)
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    "A new active effect must use the initial state version."
                );
            }
            if (effect.Status != ActiveEffectStatus.Active)
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    "A new active effect must begin active."
                );
            }
            if (
                !binding.EffectId.HasValue
                || binding.EffectId.Value != effect.Id
                || binding.DefinitionId != effect.DefinitionId
                || binding.Source != effect.Source
            )
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    $"Active binding {binding.Id.Value} does not match effect {effect.Id.Value}."
                );
            }
            if (!binding.IsEnabled)
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    "A new active effect requires an enabled binding."
                );
            }

            state.ActiveEffects.Set(effect.Id, effect);
            state.RuleBindings.Set(binding.Id, binding);
            facts.Stage(new ActiveEffectCreatedFact(effect, binding.Id));
            return ReductionResult<ActiveEffectCreationOutcome>.Accept(
                new ActiveEffectCreationOutcome(effect.Id, binding.Id, effect.EffectStateVersion)
            );
        }
    }

    internal sealed class UpdateActiveEffectStateReducer
        : IOpReducer<UpdateActiveEffectStateOp, ActiveEffectStateUpdateOutcome>
    {
        public ReductionResult<ActiveEffectStateUpdateOutcome> Reduce(
            ReductionContext<UpdateActiveEffectStateOp> context,
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
            )
            {
                return ReductionResult<ActiveEffectStateUpdateOutcome>.Reject(rejection);
            }
            Type currentStateType = effect.State.GetType();
            if (currentStateType != context.Op.State.GetType())
            {
                return ReductionResult<ActiveEffectStateUpdateOutcome>.Reject(
                    $"Active effect {effect.Id.Value} requires {currentStateType.Name}, "
                        + $"not {context.Op.State.GetType().Name}."
                );
            }

            EffectStateVersion nextVersion = effect.EffectStateVersion.Next();
            state.ActiveEffects.Set(effect.Id, effect.WithState(context.Op.State, nextVersion));
            facts.Stage(
                new ActiveEffectStateUpdatedFact(
                    effect.Id,
                    effect.DefinitionId,
                    effect.EffectStateVersion,
                    nextVersion,
                    context.Op.State.GetType()
                )
            );
            return ReductionResult<ActiveEffectStateUpdateOutcome>.Accept(
                new ActiveEffectStateUpdateOutcome(
                    effect.Id,
                    effect.EffectStateVersion,
                    nextVersion
                )
            );
        }
    }

    internal sealed class ExpireActiveEffectReducer
        : IOpReducer<ExpireActiveEffectOp, ActiveEffectExpirationOutcome>
    {
        public ReductionResult<ActiveEffectExpirationOutcome> Reduce(
            ReductionContext<ExpireActiveEffectOp> context,
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
                || !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    context.Op.BindingId,
                    true,
                    out ActiveRuleBinding binding,
                    out rejection
                )
            )
            {
                return ReductionResult<ActiveEffectExpirationOutcome>.Reject(rejection);
            }

            EffectStateVersion nextVersion = effect.EffectStateVersion.Next();
            state.ActiveEffects.Set(
                effect.Id,
                effect.WithStatus(ActiveEffectStatus.Expired, nextVersion)
            );
            state.RuleBindings.Set(binding.Id, binding.WithEnabled(false));
            facts.Stage(
                new ActiveEffectExpiredFact(
                    effect.Id,
                    effect.DefinitionId,
                    binding.Id,
                    effect.EffectStateVersion,
                    nextVersion
                )
            );
            return ReductionResult<ActiveEffectExpirationOutcome>.Accept(
                new ActiveEffectExpirationOutcome(effect.Id, nextVersion)
            );
        }
    }

    internal sealed class RemoveActiveEffectReducer
        : IOpReducer<RemoveActiveEffectOp, ActiveEffectRemovalOutcome>
    {
        public ReductionResult<ActiveEffectRemovalOutcome> Reduce(
            ReductionContext<RemoveActiveEffectOp> context,
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
                || !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    context.Op.BindingId,
                    false,
                    out ActiveRuleBinding binding,
                    out rejection
                )
            )
            {
                return ReductionResult<ActiveEffectRemovalOutcome>.Reject(rejection);
            }

            state.ActiveEffects.Remove(effect.Id);
            state.RuleBindings.Remove(binding.Id);
            state.Frequencies.Remove(binding.Id);
            facts.Stage(
                new ActiveEffectRemovedFact(
                    effect.Id,
                    effect.DefinitionId,
                    binding.Id,
                    effect.EffectStateVersion,
                    effect.Status
                )
            );
            return ReductionResult<ActiveEffectRemovalOutcome>.Accept(
                new ActiveEffectRemovalOutcome(effect.Id, binding.Id)
            );
        }
    }
}
