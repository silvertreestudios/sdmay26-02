using System;

namespace Game.Rules.Runtime
{
    /// <summary>Base payload for one committed active-effect lifecycle transition.</summary>
    public abstract class ActiveEffectFact : RuleFact
    {
        /// <summary>Gets the affected effect.</summary>
        public abstract ActiveEffectId EffectId { get; }

        /// <summary>Gets the static definition backing the effect.</summary>
        public abstract RuleDefinitionId DefinitionId { get; }
    }

    /// <summary>Records atomic creation of an effect and activation of its rule binding.</summary>
    public sealed class ActiveEffectCreatedFact : ActiveEffectFact
    {
        private readonly ActiveEffectId effectId;
        private readonly RuleDefinitionId definitionId;

        /// <inheritdoc/>
        public override ActiveEffectId EffectId => effectId;

        /// <inheritdoc/>
        public override RuleDefinitionId DefinitionId => definitionId;

        /// <summary>Gets the binding activated in the same transaction.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the initial effect-state version.</summary>
        public EffectStateVersion Version { get; }

        /// <summary>Gets the effect's declared duration metadata.</summary>
        public EffectDuration Duration { get; }

        /// <summary>Initializes one committed effect-creation record.</summary>
        public ActiveEffectCreatedFact(ActiveEffectInstance effect, BindingId bindingId)
        {
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));
            effectId = effect.Id;
            definitionId = effect.DefinitionId;
            BindingId = ActiveEffectOperationValidation.RequireBinding(bindingId);
            Version = effect.EffectStateVersion;
            Duration = effect.Duration;
        }
    }

    /// <summary>Records an optimistic replacement of an active effect's typed state.</summary>
    public sealed class ActiveEffectStateUpdatedFact : ActiveEffectFact
    {
        /// <inheritdoc/>
        public override ActiveEffectId EffectId { get; }

        /// <inheritdoc/>
        public override RuleDefinitionId DefinitionId { get; }

        /// <summary>Gets the version replaced by the update.</summary>
        public EffectStateVersion PreviousVersion { get; }

        /// <summary>Gets the newly committed version.</summary>
        public EffectStateVersion CurrentVersion { get; }

        /// <summary>Initializes one committed typed-state update record.</summary>
        public ActiveEffectStateUpdatedFact(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            EffectStateVersion previousVersion,
            EffectStateVersion currentVersion
        )
        {
            EffectId = ActiveEffectOperationValidation.RequireEffect(effectId);
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A rule definition ID is required.",
                    nameof(definitionId)
                );
            DefinitionId = definitionId;
            PreviousVersion = previousVersion;
            CurrentVersion = currentVersion;
        }
    }

    /// <summary>Identifies why an active effect left authoritative rules state.</summary>
    public enum ActiveEffectRemovalReason
    {
        /// <summary>The effect's declared duration or encounter-owned lifetime ended.</summary>
        Expired,

        /// <summary>The owning feature explicitly ended the effect early.</summary>
        Ended,
    }

    /// <summary>Records atomic removal of an effect instance and its rule binding.</summary>
    public sealed class ActiveEffectRemovedFact : ActiveEffectFact
    {
        /// <inheritdoc/>
        public override ActiveEffectId EffectId => Effect.Id;

        /// <inheritdoc/>
        public override RuleDefinitionId DefinitionId => Effect.DefinitionId;

        /// <summary>Gets the immutable effect removed from authoritative state.</summary>
        public ActiveEffectInstance Effect { get; }

        /// <summary>Gets the immutable binding removed in the same transaction.</summary>
        public ActiveRuleBinding Binding { get; }

        /// <summary>Gets why rules code removed the effect.</summary>
        public ActiveEffectRemovalReason Reason { get; }

        /// <summary>Gets the removed binding's identity.</summary>
        public BindingId BindingId => Binding.Id;

        /// <summary>Gets the final effect-state version that was removed.</summary>
        public EffectStateVersion RemovedVersion => Effect.EffectStateVersion;

        /// <summary>Initializes one committed effect-removal record.</summary>
        /// <param name="effect">The immutable effect removed from authoritative state.</param>
        /// <param name="binding">The immutable associated binding removed with the effect.</param>
        /// <param name="reason">Why rules code removed the effect.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="effect"/> or <paramref name="binding"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="binding"/> is not associated with <paramref name="effect"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="reason"/> is not a defined removal reason.
        /// </exception>
        public ActiveEffectRemovedFact(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            ActiveEffectRemovalReason reason
        )
        {
            if (!Enum.IsDefined(typeof(ActiveEffectRemovalReason), reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (
                !Binding.EffectId.HasValue
                || Binding.EffectId.Value != Effect.Id
                || Binding.DefinitionId != Effect.DefinitionId
                || Binding.Source != Effect.Source
            )
                throw new ArgumentException(
                    "The removed binding must be associated with the removed effect.",
                    nameof(binding)
                );
            Reason = reason;
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
