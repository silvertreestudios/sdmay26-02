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

    /// <summary>Requests an optimistic typed state update for one exact condition.</summary>
    public sealed class UpdateConditionStateOp
        : IRuleOp<ConditionStateUpdateOutcome>,
            IRuleSourcedOp
    {
        /// <summary>Creates one exact condition-state update.</summary>
        public UpdateConditionStateOp(
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

        /// <summary>Gets the effect to update.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the expected optimistic version.</summary>
        public EffectStateVersion ExpectedVersion { get; }

        /// <summary>Gets the immutable replacement state.</summary>
        public IEffectState State { get; }
        public RuleSource Source { get; }

        /// <summary>Creates an update while preserving the typed state at the call site.</summary>
        public static UpdateConditionStateOp Create<TState>(
            ActiveEffectId effectId,
            EffectStateVersion expectedVersion,
            TState state,
            RuleSource source
        )
            where TState : IEffectState =>
            new UpdateConditionStateOp(effectId, expectedVersion, state, source);
    }

    /// <summary>Requests expiration of one exact condition effect and binding.</summary>
    public sealed class ExpireConditionOp : IRuleOp<ConditionExpirationOutcome>, IRuleSourcedOp
    {
        /// <summary>Creates one exact optimistic expiration.</summary>
        public ExpireConditionOp(
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

        /// <summary>Gets the effect identity.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the exact binding identity.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the expected optimistic version.</summary>
        public EffectStateVersion ExpectedVersion { get; }
        public RuleSource Source { get; }
    }

    /// <summary>Requests removal of one exact condition effect and binding.</summary>
    public sealed class RemoveConditionOp : IRuleOp<ConditionRemovalOutcome>, IRuleSourcedOp
    {
        /// <summary>Creates one exact optimistic removal.</summary>
        public RemoveConditionOp(
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

        /// <summary>Gets the effect identity.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the exact binding identity.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the expected optimistic version.</summary>
        public EffectStateVersion ExpectedVersion { get; }
        public RuleSource Source { get; }
    }

    /// <summary>Identifies a newly created condition registration.</summary>
    public readonly struct ConditionCreationOutcome
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

        /// <summary>Gets the created effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the created binding ID.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the initial state version.</summary>
        public EffectStateVersion Version { get; }
    }

    /// <summary>Reports the version transition of a condition update.</summary>
    public readonly struct ConditionStateUpdateOutcome
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

        /// <summary>Gets the updated effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the prior version.</summary>
        public EffectStateVersion PreviousVersion { get; }

        /// <summary>Gets the committed version.</summary>
        public EffectStateVersion CurrentVersion { get; }
    }

    /// <summary>Reports an expired condition and its tombstone version.</summary>
    public readonly struct ConditionExpirationOutcome
    {
        internal ConditionExpirationOutcome(ActiveEffectId effectId, EffectStateVersion version)
        {
            EffectId = effectId;
            Version = version;
        }

        /// <summary>Gets the expired effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the tombstone version.</summary>
        public EffectStateVersion Version { get; }
    }

    /// <summary>Reports the exact condition registration removed from the snapshot.</summary>
    public readonly struct ConditionRemovalOutcome
    {
        internal ConditionRemovalOutcome(ActiveEffectId effectId, BindingId bindingId)
        {
            EffectId = effectId;
            BindingId = bindingId;
        }

        /// <summary>Gets the removed effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the removed binding ID.</summary>
        public BindingId BindingId { get; }
    }
}
