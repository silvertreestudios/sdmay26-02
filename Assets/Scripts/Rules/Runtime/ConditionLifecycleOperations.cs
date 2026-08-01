using System;

namespace Game.Rules.Runtime
{
    internal sealed class CreateConditionOp : IRuleOp<ConditionCreationOutcome>, IRuleSourcedOp
    {
        internal CreateConditionOp(ActiveEffectInstance effect, ActiveRuleBinding binding)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        internal ActiveEffectInstance Effect { get; }
        internal ActiveRuleBinding Binding { get; }
        public RuleSource Source => Effect.Source;
    }

    internal sealed class UpdateConditionStateOp
        : IRuleOp<ConditionStateUpdateOutcome>,
            IRuleSourcedOp
    {
        internal UpdateConditionStateOp(
            ActiveEffectId effectId,
            EffectStateVersion expectedVersion,
            IEffectState state,
            RuleSource source
        )
        {
            EffectId = ActiveEffectOperationValidation.RequireEffect(effectId);
            ExpectedVersion = expectedVersion;
            State = state ?? throw new ArgumentNullException(nameof(state));
            Source = ActiveEffectOperationValidation.RequireSource(source);
        }

        internal ActiveEffectId EffectId { get; }
        internal EffectStateVersion ExpectedVersion { get; }
        internal IEffectState State { get; }
        public RuleSource Source { get; }

        internal static UpdateConditionStateOp Create<TState>(
            ActiveEffectId effectId,
            EffectStateVersion expectedVersion,
            TState state,
            RuleSource source
        )
            where TState : IEffectState =>
            new UpdateConditionStateOp(effectId, expectedVersion, state, source);
    }

    internal sealed class ExpireConditionOp : IRuleOp<ConditionExpirationOutcome>, IRuleSourcedOp
    {
        internal ExpireConditionOp(
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion expectedVersion,
            RuleSource source
        )
        {
            EffectId = ActiveEffectOperationValidation.RequireEffect(effectId);
            BindingId = ActiveEffectOperationValidation.RequireBinding(bindingId);
            ExpectedVersion = expectedVersion;
            Source = ActiveEffectOperationValidation.RequireSource(source);
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal EffectStateVersion ExpectedVersion { get; }
        public RuleSource Source { get; }
    }

    internal sealed class RemoveConditionOp : IRuleOp<ConditionRemovalOutcome>, IRuleSourcedOp
    {
        internal RemoveConditionOp(
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion expectedVersion,
            RuleSource source
        )
        {
            EffectId = ActiveEffectOperationValidation.RequireEffect(effectId);
            BindingId = ActiveEffectOperationValidation.RequireBinding(bindingId);
            ExpectedVersion = expectedVersion;
            Source = ActiveEffectOperationValidation.RequireSource(source);
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal EffectStateVersion ExpectedVersion { get; }
        public RuleSource Source { get; }
    }

    internal readonly struct ConditionCreationOutcome
    {
        internal ConditionCreationOutcome(
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion version
        )
        {
            EffectId = effectId;
            BindingId = bindingId;
            Version = version;
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal EffectStateVersion Version { get; }
    }

    internal readonly struct ConditionStateUpdateOutcome
    {
        internal ConditionStateUpdateOutcome(
            ActiveEffectId effectId,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion
        )
        {
            EffectId = effectId;
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
        }

        internal ActiveEffectId EffectId { get; }
        internal EffectStateVersion PreviousVersion { get; }
        internal EffectStateVersion CurrentVersion { get; }
    }

    internal readonly struct ConditionExpirationOutcome
    {
        internal ConditionExpirationOutcome(ActiveEffectId effectId, EffectStateVersion version)
        {
            EffectId = effectId;
            Version = version;
        }

        internal ActiveEffectId EffectId { get; }
        internal EffectStateVersion Version { get; }
    }

    internal readonly struct ConditionRemovalOutcome
    {
        internal ConditionRemovalOutcome(ActiveEffectId effectId, BindingId bindingId)
        {
            EffectId = effectId;
            BindingId = bindingId;
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
    }
}
