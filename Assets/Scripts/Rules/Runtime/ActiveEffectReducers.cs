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
            if (!registry.ContainsDefinition(effect.DefinitionId))
            {
                rejection = $"Rule definition {effect.DefinitionId.Value} is unknown.";
                return false;
            }
            if (!ActiveEffectRegistration.BindingMatchesEffect(effect, binding))
            {
                rejection =
                    $"Active binding {binding.Id.Value} does not match effect {effect.Id.Value}.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        public static bool TryResolveCreationTiming(
            RuleRegistry registry,
            RulesStateDraft state,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            out ActiveEffectTimingState timing,
            out string rejection
        )
        {
            timing = null;
            if (state.ActiveEffects.Contains(effect.Id))
            {
                rejection = $"Active effect {effect.Id.Value} already exists.";
                return false;
            }
            if (state.RuleBindings.Contains(binding.Id))
            {
                rejection = $"Active binding {binding.Id.Value} already exists.";
                return false;
            }
            if (!TryValidateRegistration(registry, effect, binding, out rejection))
                return false;
            if (effect.EffectStateVersion != EffectStateVersion.Initial)
            {
                rejection = "A new active effect must use the initial state version.";
                return false;
            }
            if (effect.Status != ActiveEffectStatus.Active)
            {
                rejection = "A new active effect must begin active.";
                return false;
            }
            if (!binding.IsEnabled)
            {
                rejection = "A new active effect requires an enabled binding.";
                return false;
            }

            if (effect.Duration.Kind != EffectDurationKind.Indefinite)
            {
                // Exploration-owned finite effects retain their host-managed lifetime until an
                // encounter clock exists. Once one exists, scheduling requires that exact roster.
                EncounterState encounter = state
                    .Encounters.Select(pair => pair.Value)
                    .FirstOrDefault(value => value.Phase == EncounterPhase.Active);
                if (encounter != null)
                {
                    if (!encounter.Roster.Any(entry => entry.Creature == effect.SourceCreature))
                    {
                        rejection = "The effect source is not in the active encounter roster.";
                        return false;
                    }
                    timing = ActiveEffectTimingState.ForEncounter(effect, binding, encounter);
                }
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

        public static EffectStateVersion CommitStateUpdate(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            IEffectState nextState,
            FactSink facts
        )
        {
            EffectStateVersion nextVersion = effect.EffectStateVersion.Next();
            state.ActiveEffects.Set(effect.Id, effect.WithState(nextState, nextVersion));
            facts.Stage(
                new ActiveEffectStateUpdatedFact(
                    effect.Id,
                    effect.DefinitionId,
                    effect.EffectStateVersion,
                    nextVersion
                )
            );
            return nextVersion;
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

        public static ActiveEffectRemovalOutcome CommitRemoval(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            FactSink facts
        )
        {
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
            return new ActiveEffectRemovalOutcome(effect.Id, binding.Id);
        }
    }

    /// <summary>Validates and commits one generic active-effect adoption transaction.</summary>
    internal static class ActiveEffectAdoptionReduction
    {
        internal static bool TryAdopt(
            RuleRegistry registry,
            IReadOnlyList<ActiveEffectRegistration> registrations,
            RulesStateDraft state,
            FactSink facts,
            out int adopted,
            out string rejection
        )
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (registrations == null)
                throw new ArgumentNullException(nameof(registrations));
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (facts == null)
                throw new ArgumentNullException(nameof(facts));

            HashSet<ActiveEffectId> effectIds = new HashSet<ActiveEffectId>();
            HashSet<BindingId> bindingIds = new HashSet<BindingId>();
            List<(ActiveEffectRegistration Registration, ActiveEffectTimingState Timing)> pending =
                new List<(ActiveEffectRegistration Registration, ActiveEffectTimingState Timing)>();
            foreach (ActiveEffectRegistration registration in registrations)
            {
                if (
                    !effectIds.Add(registration.Effect.Id)
                    || !bindingIds.Add(registration.Binding.Id)
                )
                {
                    adopted = 0;
                    rejection =
                        "An active-effect adoption batch contains duplicate stable identities.";
                    return false;
                }
                if (!state.Creatures.Contains(registration.Binding.Owner))
                {
                    adopted = 0;
                    rejection = "An active-effect binding owner is not a registered creature.";
                    return false;
                }
                if (
                    !ActiveEffectReduction.TryResolveAdoptionTiming(
                        registry,
                        state,
                        registration.Effect,
                        registration.Binding,
                        registration.Timing,
                        out ActiveEffectTimingState expectedTiming,
                        out rejection
                    )
                )
                {
                    adopted = 0;
                    return false;
                }

                bool hasEffect = state.ActiveEffects.TryGet(
                    registration.Effect.Id,
                    out ActiveEffectInstance existingEffect
                );
                bool hasBinding = state.RuleBindings.TryGet(
                    registration.Binding.Id,
                    out ActiveRuleBinding existingBinding
                );
                bool hasTiming = state.ActiveEffectTimings.TryGet(
                    registration.Effect.Id,
                    out ActiveEffectTimingState existingTiming
                );
                if (hasEffect || hasBinding || hasTiming)
                {
                    if (
                        hasEffect
                        && hasBinding
                        && ActiveEffectInstanceExactEquality.Equals(
                            existingEffect,
                            registration.Effect
                        )
                        && existingBinding.Equals(registration.Binding)
                        && hasTiming == (expectedTiming != null)
                        && (!hasTiming || existingTiming.Equals(expectedTiming))
                    )
                        continue;
                    adopted = 0;
                    rejection =
                        "An adopted active-effect identity is already used by different or partial state.";
                    return false;
                }
                pending.Add((registration, expectedTiming));
            }

            foreach (var item in pending)
                ActiveEffectReduction.CommitAdoption(
                    state,
                    item.Registration.Effect,
                    item.Registration.Binding,
                    item.Timing,
                    facts
                );
            adopted = pending.Count;
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
            if (
                !ActiveEffectReduction.TryResolveCreationTiming(
                    registry,
                    state,
                    effect,
                    binding,
                    out ActiveEffectTimingState timing,
                    out string rejection
                )
            )
                return ReductionResult<ActiveEffectCreationOutcome>.Reject(rejection);

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

            EffectStateVersion nextVersion = ActiveEffectReduction.CommitStateUpdate(
                state,
                effect,
                context.Op.State,
                facts
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
            if (
                !ActiveEffectAdoptionReduction.TryAdopt(
                    registry,
                    context.Op.Registrations,
                    state,
                    facts,
                    out int adopted,
                    out string rejection
                )
            )
                return ReductionResult<ActiveEffectAdoptionOutcome>.Reject(rejection);
            return ReductionResult<ActiveEffectAdoptionOutcome>.Accept(
                new ActiveEffectAdoptionOutcome(adopted)
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

            return ReductionResult<ActiveEffectRemovalOutcome>.Accept(
                ActiveEffectReduction.CommitRemoval(state, effect, binding, facts)
            );
        }
    }
}
