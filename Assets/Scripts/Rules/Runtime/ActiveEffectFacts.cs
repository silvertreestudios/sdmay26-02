using System;

namespace Game.Rules.Runtime
{
    /// <summary>Base payload for one committed active-effect lifecycle transition.</summary>
    public abstract class ActiveEffectFact : RuleFact
    {
        /// <summary>Gets the affected effect.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the static definition backing the effect.</summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>Initializes lifecycle identity shared by active-effect Facts.</summary>
        /// <param name="effectId">The affected effect.</param>
        /// <param name="definitionId">The static definition backing the effect.</param>
        protected ActiveEffectFact(ActiveEffectId effectId, RuleDefinitionId definitionId)
        {
            EffectId = ActiveEffectOperationValidation.RequireEffect(effectId);
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A rule definition ID is required.",
                    nameof(definitionId)
                );
            DefinitionId = definitionId;
        }
    }

    /// <summary>Records atomic creation of an effect and activation of its rule binding.</summary>
    public sealed class ActiveEffectCreatedFact : ActiveEffectFact
    {
        /// <summary>Gets the binding activated in the same transaction.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the initial effect-state version.</summary>
        public EffectStateVersion Version { get; }

        /// <summary>Gets the exact definition-owned state type.</summary>
        public Type StateType { get; }

        /// <summary>Gets the effect's declared duration metadata.</summary>
        public EffectDuration Duration { get; }

        /// <summary>Initializes one committed effect-creation record.</summary>
        public ActiveEffectCreatedFact(ActiveEffectInstance effect, BindingId bindingId)
            : base(
                effect?.Id ?? throw new ArgumentNullException(nameof(effect)),
                effect.DefinitionId
            )
        {
            BindingId = ActiveEffectOperationValidation.RequireBinding(bindingId);
            Version = effect.EffectStateVersion;
            StateType = effect.State.GetType();
            Duration = effect.Duration;
        }
    }

    /// <summary>Records an optimistic replacement of an active effect's typed state.</summary>
    public sealed class ActiveEffectStateUpdatedFact : ActiveEffectFact
    {
        /// <summary>Gets the version replaced by the update.</summary>
        public EffectStateVersion PreviousVersion { get; }

        /// <summary>Gets the newly committed version.</summary>
        public EffectStateVersion CurrentVersion { get; }

        /// <summary>Gets the exact state type accepted by the definition.</summary>
        public Type StateType { get; }

        /// <summary>Initializes one committed typed-state update record.</summary>
        public ActiveEffectStateUpdatedFact(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion,
            Type stateType
        )
            : base(effectId, definitionId)
        {
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
            StateType = stateType ?? throw new ArgumentNullException(nameof(stateType));
        }
    }

    /// <summary>Records explicit expiration and atomic deactivation of an effect binding.</summary>
    public sealed class ActiveEffectExpiredFact : ActiveEffectFact
    {
        /// <summary>Gets the binding deactivated in the same transaction.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the version replaced by expiration.</summary>
        public EffectStateVersion PreviousVersion { get; }

        /// <summary>Gets the expired tombstone's version.</summary>
        public EffectStateVersion CurrentVersion { get; }

        /// <summary>Initializes one committed expiration record.</summary>
        public ActiveEffectExpiredFact(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            BindingId bindingId,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion
        )
            : base(effectId, definitionId)
        {
            BindingId = ActiveEffectOperationValidation.RequireBinding(bindingId);
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
        }
    }

    /// <summary>Records atomic removal of an effect instance and its rule binding.</summary>
    public sealed class ActiveEffectRemovedFact : ActiveEffectFact
    {
        /// <summary>Gets the binding removed in the same transaction.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the final version that was removed.</summary>
        public EffectStateVersion RemovedVersion { get; }

        /// <summary>Gets whether the removed instance was active or expired.</summary>
        public ActiveEffectStatus RemovedStatus { get; }

        /// <summary>Initializes one committed effect-removal record.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="removedStatus"/> is not a defined lifecycle status.
        /// </exception>
        public ActiveEffectRemovedFact(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            BindingId bindingId,
            EffectStateVersion removedVersion,
            ActiveEffectStatus removedStatus
        )
            : base(effectId, definitionId)
        {
            if (!Enum.IsDefined(typeof(ActiveEffectStatus), removedStatus))
                throw new ArgumentOutOfRangeException(nameof(removedStatus));
            BindingId = ActiveEffectOperationValidation.RequireBinding(bindingId);
            RemovedVersion = removedVersion;
            RemovedStatus = removedStatus;
        }
    }

    /// <summary>Describes an atomically created effect and active binding.</summary>
    public readonly struct ActiveEffectCreationOutcome
    {
        /// <summary>Gets the created effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the activated binding ID.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the initial effect-state version.</summary>
        public EffectStateVersion Version { get; }

        /// <summary>Initializes a successful effect-creation outcome.</summary>
        public ActiveEffectCreationOutcome(
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion version
        )
        {
            EffectId = effectId;
            BindingId = bindingId;
            Version = version;
        }
    }

    /// <summary>Describes a committed typed-state update.</summary>
    public readonly struct ActiveEffectStateUpdateOutcome
    {
        /// <summary>Gets the updated effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the replaced version.</summary>
        public EffectStateVersion PreviousVersion { get; }

        /// <summary>Gets the newly committed version.</summary>
        public EffectStateVersion CurrentVersion { get; }

        /// <summary>Initializes a successful typed-state update outcome.</summary>
        public ActiveEffectStateUpdateOutcome(
            ActiveEffectId effectId,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion
        )
        {
            EffectId = effectId;
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
        }
    }

    /// <summary>Describes a committed effect expiration.</summary>
    public readonly struct ActiveEffectExpirationOutcome
    {
        /// <summary>Gets the expired effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the expired tombstone's version.</summary>
        public EffectStateVersion Version { get; }

        /// <summary>Initializes a successful expiration outcome.</summary>
        public ActiveEffectExpirationOutcome(ActiveEffectId effectId, EffectStateVersion version)
        {
            EffectId = effectId;
            Version = version;
        }
    }

    /// <summary>Describes a committed effect and binding removal.</summary>
    public readonly struct ActiveEffectRemovalOutcome
    {
        /// <summary>Gets the removed effect ID.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the removed binding ID.</summary>
        public BindingId BindingId { get; }

        /// <summary>Initializes a successful removal outcome.</summary>
        public ActiveEffectRemovalOutcome(ActiveEffectId effectId, BindingId bindingId)
        {
            EffectId = effectId;
            BindingId = bindingId;
        }
    }
}
