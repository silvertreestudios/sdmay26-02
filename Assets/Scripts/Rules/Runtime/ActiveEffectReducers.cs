using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class ActiveEffectReduction
    {
        public static bool TryValidateRegistration(
            RuleRegistry registry,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            out string rejection
        )
        {
            if (!registry.TryGetDefinition(effect.DefinitionId, out _))
            {
                rejection = $"Rule definition {effect.DefinitionId.Value} is unknown.";
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
                    $"Active binding {binding.Id.Value} does not match effect {effect.Id.Value}.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        public static void CommitCreation(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            ActiveEffectTimingState timing,
            FactSink facts
        )
        {
            if (timing != null)
                state.ActiveEffectTimings.Set(effect.Id, timing);
            state.ActiveEffects.Set(effect.Id, effect);
            state.RuleBindings.Set(binding.Id, binding);
            facts.Stage(new ActiveEffectCreatedFact(effect, binding.Id));
        }

        public static void CommitAdoption(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            ActiveEffectTimingState timing,
            FactSink facts
        )
        {
            if (timing != null)
                state.ActiveEffectTimings.Set(effect.Id, timing);
            state.ActiveEffects.Set(effect.Id, effect);
            state.RuleBindings.Set(binding.Id, binding);
            facts.Stage(new ActiveEffectAdoptedFact(effect, binding));
        }

        public static bool TryResolveAdoptionTiming(
            RuleRegistry registry,
            RulesStateDraft state,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            ActiveEffectTimingState suppliedTiming,
            out ActiveEffectTimingState timing,
            out string rejection
        )
        {
            timing = null;
            if (!TryValidateRegistration(registry, effect, binding, out rejection))
                return false;
            if ((effect.Status == ActiveEffectStatus.Active) != binding.IsEnabled)
            {
                rejection = "An adopted effect's lifecycle status and binding state disagree.";
                return false;
            }
            if (
                suppliedTiming != null
                && (
                    effect.Status != ActiveEffectStatus.Active
                    || effect.Duration.Kind == EffectDurationKind.Indefinite
                    || suppliedTiming.Effect != effect.Id
                    || suppliedTiming.Binding != binding.Id
                    || suppliedTiming.SourceCreature != effect.SourceCreature
                    || suppliedTiming.CreationOrder != binding.CreationOrder
                    || suppliedTiming.ExpiresWithEncounter
                        != (effect.Duration.Kind == EffectDurationKind.Encounter)
                )
            )
            {
                rejection = "Adopted effect timing does not match its exact registration.";
                return false;
            }
            EncounterState encounter = state
                .Encounters.Select(pair => pair.Value)
                .FirstOrDefault(value => value.Phase == EncounterPhase.Active);
            if (
                suppliedTiming != null
                && encounter != null
                && suppliedTiming.Encounter != encounter.Id
            )
            {
                rejection = "Adopted effect timing belongs to a different encounter.";
                return false;
            }
            if (
                encounter != null
                && effect.Status == ActiveEffectStatus.Active
                && effect.Duration.Kind != EffectDurationKind.Indefinite
            )
            {
                if (!encounter.Roster.Any(entry => entry.Creature == effect.SourceCreature))
                {
                    rejection = "The adopted effect source is not in the active encounter roster.";
                    return false;
                }
                timing =
                    suppliedTiming
                    ?? ActiveEffectTimingState.ForEncounter(effect, binding, encounter);
            }
            else
                timing = suppliedTiming;
            rejection = string.Empty;
            return true;
        }

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
            if (
                !ActiveEffectReduction.TryValidateRegistration(
                    registry,
                    effect,
                    binding,
                    out string rejection
                )
            )
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(rejection);
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
            if (!binding.IsEnabled)
            {
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                    "A new active effect requires an enabled binding."
                );
            }

            ActiveEffectTimingState timing = null;
            if (effect.Duration.Kind != EffectDurationKind.Indefinite)
            {
                EncounterState encounter = state
                    .Encounters.Select(pair => pair.Value)
                    .FirstOrDefault(value => value.Phase == EncounterPhase.Active);
                // Exploration-owned effects retain their existing host-managed lifetime until an
                // encounter clock exists. Once a clock exists, every finite effect is scheduled
                // deterministically and its source must belong to that exact roster.
                if (encounter != null)
                {
                    if (!encounter.Roster.Any(entry => entry.Creature == effect.SourceCreature))
                        return ReductionResult<ActiveEffectCreationOutcome>.Reject(
                            "The effect source is not in the active encounter roster."
                        );
                    timing = ActiveEffectTimingState.ForEncounter(effect, binding, encounter);
                }
            }

            ActiveEffectReduction.CommitCreation(state, effect, binding, timing, facts);
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

    internal sealed class AdoptActiveEffectRegistrationsReducer
        : IOpReducer<AdoptActiveEffectRegistrationsOp, ActiveEffectAdoptionOutcome>
    {
        private readonly RuleRegistry registry;

        internal AdoptActiveEffectRegistrationsReducer(RuleRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public ReductionResult<ActiveEffectAdoptionOutcome> Reduce(
            ReductionContext<AdoptActiveEffectRegistrationsOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            HashSet<ActiveEffectId> effectIds = new HashSet<ActiveEffectId>();
            HashSet<BindingId> bindingIds = new HashSet<BindingId>();
            List<(ActiveEffectRegistration Registration, ActiveEffectTimingState Timing)> pending =
                new List<(ActiveEffectRegistration Registration, ActiveEffectTimingState Timing)>();
            foreach (ActiveEffectRegistration registration in context.Op.Registrations)
            {
                if (
                    !effectIds.Add(registration.Effect.Id)
                    || !bindingIds.Add(registration.Binding.Id)
                )
                    return ReductionResult<ActiveEffectAdoptionOutcome>.Reject(
                        "An active-effect adoption batch contains duplicate stable identities."
                    );
                if (
                    !ActiveEffectReduction.TryResolveAdoptionTiming(
                        registry,
                        state,
                        registration.Effect,
                        registration.Binding,
                        null,
                        out ActiveEffectTimingState timing,
                        out string rejection
                    )
                )
                    return ReductionResult<ActiveEffectAdoptionOutcome>.Reject(rejection);
                bool hasEffect = state.ActiveEffects.TryGet(
                    registration.Effect.Id,
                    out ActiveEffectInstance existingEffect
                );
                bool hasBinding = state.RuleBindings.TryGet(
                    registration.Binding.Id,
                    out ActiveRuleBinding existingBinding
                );
                if (hasEffect || hasBinding)
                {
                    bool hasTiming = state.ActiveEffectTimings.TryGet(
                        registration.Effect.Id,
                        out ActiveEffectTimingState existingTiming
                    );
                    if (
                        hasEffect
                        && hasBinding
                        && existingEffect.Equals(registration.Effect)
                        && existingBinding.Equals(registration.Binding)
                        && hasTiming == (timing != null)
                        && (!hasTiming || existingTiming.Equals(timing))
                    )
                        continue;
                    return ReductionResult<ActiveEffectAdoptionOutcome>.Reject(
                        "An adopted active-effect ID is already used by different state."
                    );
                }
                pending.Add((registration, timing));
            }
            foreach (var item in pending)
                ActiveEffectReduction.CommitAdoption(
                    state,
                    item.Registration.Effect,
                    item.Registration.Binding,
                    item.Timing,
                    facts
                );
            return ReductionResult<ActiveEffectAdoptionOutcome>.Accept(
                new ActiveEffectAdoptionOutcome(pending.Count)
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
            state.ActiveEffectTimings.Remove(effect.Id);
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
            state.ActiveEffectTimings.Remove(effect.Id);
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
