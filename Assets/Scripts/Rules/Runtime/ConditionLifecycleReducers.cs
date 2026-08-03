using System;
using System.Collections.Generic;
using System.Linq;

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
    }

    internal enum ConditionActiveValidationFailure
    {
        None,
        InvalidOwner,
        Immune,
    }

    /// <summary>Enforces compiled condition immunity wherever active condition authority enters.</summary>
    internal static class ConditionImmunityValidation
    {
        internal static bool TryValidateActive(
            RulesStateDraft state,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            out ConditionActiveValidationFailure failure,
            out string rejection
        )
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (
                effect.Status != ActiveEffectStatus.Active
                || !ConditionReduction.IsCondition(effect)
            )
            {
                failure = ConditionActiveValidationFailure.None;
                rejection = string.Empty;
                return true;
            }
            if (!state.Creatures.Contains(binding.Owner))
            {
                failure = ConditionActiveValidationFailure.InvalidOwner;
                rejection = "The condition owner is not a registered creature.";
                return false;
            }
            if (!state.PreparedInputs.TryGet(binding.Owner, out PreparedCreatureInputs prepared))
                throw new InvalidOperationException(
                    $"Registered condition owner {binding.Owner.Value} has no authoritative prepared inputs."
                );
            if (TryGetPreparedBlock(effect.DefinitionId, prepared, out rejection))
            {
                failure = ConditionActiveValidationFailure.Immune;
                return false;
            }
            failure = ConditionActiveValidationFailure.None;
            return true;
        }

        internal static void ValidateStateInvariant(
            IReadOnlyDictionary<CreatureId, CreatureState> creatures,
            IReadOnlyDictionary<CreatureId, PreparedCreatureInputs> preparedInputs,
            IReadOnlyDictionary<ActiveEffectId, ActiveEffectInstance> activeEffects,
            IReadOnlyDictionary<BindingId, ActiveRuleBinding> ruleBindings
        )
        {
            foreach (
                ActiveEffectInstance effect in activeEffects.Values.Where(value =>
                    value.Status == ActiveEffectStatus.Active
                    && ConditionReduction.IsCondition(value)
                )
            )
            {
                ActiveRuleBinding[] bindings = ruleBindings
                    .Values.Where(value =>
                        value.IsEnabled
                        && value.EffectId.HasValue
                        && value.EffectId.Value == effect.Id
                    )
                    .ToArray();
                if (
                    bindings.Length != 1
                    || !ActiveEffectRegistration.BindingMatchesEffect(effect, bindings[0])
                    || !ConditionRuleDefinitions.Accepts(effect.DefinitionId, effect.State)
                )
                    throw new InvalidOperationException(
                        $"Active condition {effect.Id.Value} has a malformed registration."
                    );
                ActiveRuleBinding binding = bindings[0];
                if (!creatures.ContainsKey(binding.Owner))
                    throw new InvalidOperationException(
                        $"Active condition owner {binding.Owner.Value} is not a registered creature."
                    );
                if (!preparedInputs.TryGetValue(binding.Owner, out PreparedCreatureInputs prepared))
                    throw new InvalidOperationException(
                        $"Registered condition owner {binding.Owner.Value} has no authoritative prepared inputs."
                    );
                if (TryGetPreparedBlock(effect.DefinitionId, prepared, out string rejection))
                    throw new InvalidOperationException(rejection);
            }
        }

        private static bool TryGetPreparedBlock(
            RuleDefinitionId definitionId,
            PreparedCreatureInputs prepared,
            out string reason
        )
        {
            if (
                ConditionRuleDefinitions.TryGetCanonicalSlug(definitionId, out string conditionSlug)
                && prepared.Immunities.Any(immunity =>
                    immunity.Kind == PreparedImmunityKind.Condition
                    && ConditionInputNormalizer.TryNormalize(
                        immunity.Type,
                        out RuleDefinitionId immunityDefinition
                    )
                    && immunityDefinition == definitionId
                )
            )
            {
                reason = $"The condition owner is immune to {conditionSlug}.";
                return true;
            }
            reason = string.Empty;
            return false;
        }
    }
}
