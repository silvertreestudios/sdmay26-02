using System;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class ActiveEffectReduction
    {
        /// <summary>
        /// Validates definition availability and the initial optimistic-concurrency version shared
        /// by every active-effect creation path.
        /// </summary>
        internal static bool TryValidateCreationState(
            RuleRegistry registry,
            ActiveEffectInstance effect,
            out string rejection
        )
        {
            if (!registry.TryGetDefinition(effect.DefinitionId, out _))
            {
                rejection = $"Rule definition {effect.DefinitionId.Value} is unknown.";
                return false;
            }
            if (effect.EffectStateVersion != EffectStateVersion.Initial)
            {
                rejection = "A new active effect must use the initial state version.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        /// <summary>
        /// Validates the enabled one-to-one binding supplied by every active-effect creation path.
        /// </summary>
        internal static bool TryValidateCreationBinding(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            out string rejection
        )
        {
            if (
                !binding.EffectId.HasValue
                || binding.EffectId.Value != effect.Id
                || binding.DefinitionId != effect.DefinitionId
                || binding.Source != effect.Source
            )
            {
                rejection =
                    $"Active binding {binding.Id.Value} does not match effect {effect.Id.Value}.";
                return false;
            }
            if (!binding.IsEnabled)
            {
                rejection = "A new active effect requires an enabled binding.";
                return false;
            }

            rejection = string.Empty;
            return true;
        }

        public static bool TryGetCurrent(
            RulesStateDraft state,
            ActiveEffectId effectId,
            EffectStateVersion expectedVersion,
            out ActiveEffectInstance effect,
            out string rejection
        )
        {
            if (!state.ActiveEffects.TryGet(effectId, out effect))
            {
                rejection = $"Active effect {effectId.Value} is unknown.";
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

        public static bool TryRemove(
            RulesStateDraft state,
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion expectedVersion,
            ActiveEffectRemovalReason reason,
            FactSink facts,
            out string rejection
        )
        {
            if (
                !TryGetCurrent(
                    state,
                    effectId,
                    expectedVersion,
                    out ActiveEffectInstance effect,
                    out rejection
                )
                || !TryGetAssociatedBinding(
                    state,
                    effect,
                    bindingId,
                    out ActiveRuleBinding binding,
                    out rejection
                )
            )
                return false;

            state.ActiveEffects.Remove(effect.Id);
            state.RuleBindings.Remove(binding.Id);
            state.Frequencies.Remove(binding.Id);
            state.ActiveEffectTimings.Remove(effect.Id);
            facts.Stage(new ActiveEffectRemovedFact(effect, binding, reason));
            return true;
        }

        public static bool TryGetAssociatedBinding(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            BindingId bindingId,
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
            if (
                !ActiveEffectReduction.TryValidateCreationState(
                    registry,
                    effect,
                    out string rejection
                )
                || !ActiveEffectReduction.TryValidateCreationBinding(effect, binding, out rejection)
            )
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(rejection);

            if (effect.Duration.Kind != EffectDurationKind.Indefinite)
            {
                EncounterState encounter = state
                    .Encounters.Select(pair => pair.Value)
                    .FirstOrDefault(value =>
                        value.Phase == EncounterPhase.Initialized
                        || value.Phase == EncounterPhase.Active
                    );
                // Exploration-owned effects retain their existing host-managed lifetime until an
                // encounter roster exists. Once it does, every finite effect is scheduled before
                // any initiative boundary can occur and its source must belong to that roster.
                if (encounter != null)
                {
                    if (!encounter.Roster.Any(entry => entry.Creature == effect.SourceCreature))
                        return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                            "The effect source is not in the encounter roster."
                        );
                    state.ActiveEffectTimings.Set(
                        effect.Id,
                        ActiveEffectTimingState.ForEncounter(effect, binding, encounter)
                    );
                }
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
                    nextVersion
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
                !ActiveEffectReduction.TryRemove(
                    state,
                    context.Op.EffectId,
                    context.Op.BindingId,
                    context.Op.ExpectedVersion,
                    context.Op.Reason,
                    facts,
                    out string rejection
                )
            )
                return ReductionResult<ActiveEffectRemovalOutcome>.Reject(rejection);
            return ReductionResult<ActiveEffectRemovalOutcome>.Accept(
                new ActiveEffectRemovalOutcome(context.Op.EffectId, context.Op.BindingId)
            );
        }
    }
}
