using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class ActiveEffectOperationValidation
    {
        public static ActiveEffectId RequireEffect(ActiveEffectId value)
        {
            if (value.IsEmpty)
                throw new ArgumentException("An active effect ID is required.", nameof(value));
            return value;
        }

        public static BindingId RequireBinding(BindingId value)
        {
            if (value.IsEmpty)
                throw new ArgumentException("An active binding ID is required.", nameof(value));
            return value;
        }

        public static RuleSource RequireSource(RuleSource value)
        {
            if (value.IsEmpty)
                throw new ArgumentException(
                    "An active-effect rule source is required.",
                    nameof(value)
                );
            return value;
        }
    }

    /// <summary>
    /// Requests atomic creation of one typed effect instance and its active rule binding.
    /// </summary>
    /// <remarks>
    /// This reducer operation is nested-only. A spell, condition, feat, or other feature handler
    /// dispatches it after its own rules workflow authorizes creation.
    /// </remarks>
    public sealed class CreateActiveEffectOp : IRuleOp<ActiveEffectCreationOutcome>, IRuleSourcedOp
    {
        /// <summary>Gets the complete initial effect value.</summary>
        public ActiveEffectInstance Effect { get; }

        /// <summary>Gets the binding activated in the same transaction.</summary>
        public ActiveRuleBinding Binding { get; }

        /// <inheritdoc/>
        public RuleSource Source => Effect.Source;

        /// <summary>Initializes one nested typed-effect creation request.</summary>
        /// <param name="effect">The complete effect at its initial active version.</param>
        /// <param name="binding">The enabled binding associated with the effect.</param>
        /// <exception cref="ArgumentNullException">Either value is <see langword="null"/>.</exception>
        public CreateActiveEffectOp(ActiveEffectInstance effect, ActiveRuleBinding binding)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }
    }

    /// <summary>Pairs one prepared initial effect with its exact active binding.</summary>
    public sealed class ActiveEffectRegistration
    {
        /// <summary>Creates one structurally matched registration pair.</summary>
        public ActiveEffectRegistration(ActiveEffectInstance effect, ActiveRuleBinding binding)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (
                !binding.EffectId.HasValue
                || binding.EffectId.Value != effect.Id
                || binding.DefinitionId != effect.DefinitionId
                || binding.Source != effect.Source
            )
                throw new ArgumentException(
                    "The binding does not match the active effect.",
                    nameof(binding)
                );
        }

        /// <summary>Gets the prepared effect.</summary>
        public ActiveEffectInstance Effect { get; }

        /// <summary>Gets the prepared binding.</summary>
        public ActiveRuleBinding Binding { get; }
    }

    /// <summary>Atomically adopts a deterministic batch of prepared active-effect pairs.</summary>
    public sealed class AdoptActiveEffectRegistrationsOp
        : IRuleOp<ActiveEffectAdoptionOutcome>,
            IRuleSourcedOp
    {
        private readonly IReadOnlyList<ActiveEffectRegistration> registrations;

        /// <summary>Creates one all-or-nothing adoption request.</summary>
        public AdoptActiveEffectRegistrationsOp(
            IEnumerable<ActiveEffectRegistration> registrations,
            RuleSource source
        )
        {
            if (registrations == null)
                throw new ArgumentNullException(nameof(registrations));
            ActiveEffectRegistration[] copied = registrations.ToArray();
            if (copied.Any(registration => registration == null))
                throw new ArgumentException(
                    "Active-effect registrations cannot contain null.",
                    nameof(registrations)
                );
            this.registrations = Array.AsReadOnly(copied);
            Source = ActiveEffectOperationValidation.RequireSource(source);
        }

        /// <summary>Gets prepared pairs in deterministic adoption order.</summary>
        public IReadOnlyList<ActiveEffectRegistration> Registrations => registrations;

        /// <inheritdoc/>
        public RuleSource Source { get; }
    }

    /// <summary>Reports the number of effect pairs committed by one adoption transaction.</summary>
    public readonly struct ActiveEffectAdoptionOutcome
    {
        internal ActiveEffectAdoptionOutcome(int created) => Created = created;

        /// <summary>Gets the number of committed pairs.</summary>
        public int Created { get; }
    }

    /// <summary>Requests an optimistic typed-state replacement for one active effect.</summary>
    public sealed class UpdateActiveEffectStateOp
        : IRuleOp<ActiveEffectStateUpdateOutcome>,
            IRuleSourcedOp
    {
        /// <summary>Gets the effect to update.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the version that must still be current.</summary>
        public EffectStateVersion ExpectedVersion { get; }

        /// <summary>Gets the new immutable state, which must preserve the instance's exact type.</summary>
        public IEffectState State { get; }

        /// <inheritdoc/>
        public RuleSource Source { get; }

        /// <summary>Initializes one nested optimistic state-update request.</summary>
        /// <param name="effectId">The effect to update.</param>
        /// <param name="expectedVersion">The version read by the requesting workflow.</param>
        /// <param name="state">The immutable replacement state.</param>
        /// <param name="source">The rule source stamped onto a committed update Fact.</param>
        /// <exception cref="ArgumentException">A required ID or source is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
        public UpdateActiveEffectStateOp(
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

        /// <summary>
        /// Creates an update while preserving the replacement state's concrete generic type at the call site.
        /// </summary>
        /// <typeparam name="TState">The immutable replacement state type.</typeparam>
        /// <param name="effectId">The effect to update.</param>
        /// <param name="expectedVersion">The version read by the requesting workflow.</param>
        /// <param name="state">The immutable replacement state.</param>
        /// <param name="source">The rule source stamped onto a committed update Fact.</param>
        /// <returns>A dispatchable update operation.</returns>
        public static UpdateActiveEffectStateOp Create<TState>(
            ActiveEffectId effectId,
            EffectStateVersion expectedVersion,
            TState state,
            RuleSource source
        )
            where TState : IEffectState =>
            new UpdateActiveEffectStateOp(effectId, expectedVersion, state, source);
    }

    /// <summary>
    /// Requests explicit effect expiration and binding deactivation at an expected version.
    /// </summary>
    public sealed class ExpireActiveEffectOp
        : IRuleOp<ActiveEffectExpirationOutcome>,
            IRuleSourcedOp
    {
        /// <summary>Gets the effect to expire.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the associated binding to deactivate atomically.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the version that must still be current.</summary>
        public EffectStateVersion ExpectedVersion { get; }

        /// <inheritdoc/>
        public RuleSource Source { get; }

        /// <summary>Initializes one nested optimistic expiration request.</summary>
        /// <param name="effectId">The effect to expire.</param>
        /// <param name="bindingId">The associated binding to deactivate.</param>
        /// <param name="expectedVersion">The version read by the requesting workflow.</param>
        /// <param name="source">The rule source stamped onto a committed expiration Fact.</param>
        public ExpireActiveEffectOp(
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
    }

    /// <summary>Requests atomic removal of one effect tombstone and its associated binding.</summary>
    public sealed class RemoveActiveEffectOp : IRuleOp<ActiveEffectRemovalOutcome>, IRuleSourcedOp
    {
        /// <summary>Gets the effect to remove.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets the associated binding to remove atomically.</summary>
        public BindingId BindingId { get; }

        /// <summary>Gets the version that must still be current.</summary>
        public EffectStateVersion ExpectedVersion { get; }

        /// <inheritdoc/>
        public RuleSource Source { get; }

        /// <summary>Initializes one nested optimistic removal request.</summary>
        /// <param name="effectId">The effect to remove.</param>
        /// <param name="bindingId">The associated binding to remove.</param>
        /// <param name="expectedVersion">The version read by the requesting workflow.</param>
        /// <param name="source">The rule source stamped onto a committed removal Fact.</param>
        public RemoveActiveEffectOp(
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
    }
}
