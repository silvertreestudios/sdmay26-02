using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal sealed class ConditionSelection<TState>
        where TState : IEffectState
    {
        internal ConditionSelection(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            TState state
        )
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        internal ActiveEffectInstance Effect { get; }
        internal ActiveRuleBinding Binding { get; }
        internal ActiveEffectId EffectId => Effect.Id;
        internal BindingId BindingId => Binding.Id;
        internal CreatureId Owner => Binding.Owner;
        internal RuleSource Source => Effect.Source;
        internal EffectStateVersion Version => Effect.EffectStateVersion;
        internal TState State { get; }
    }

    internal static class ConditionSelectors
    {
        internal static bool TryGetMarker(
            RulesSnapshot snapshot,
            CreatureId owner,
            RuleDefinitionId definitionId,
            out ConditionSelection<ConditionMarkerState> selection
        )
        {
            if (!ConditionRuleDefinitions.IsMarker(definitionId))
                throw new ArgumentException(
                    "A marker condition definition is required.",
                    nameof(definitionId)
                );
            return TrySelect(snapshot, owner, definitionId, _ => 0, out selection);
        }

        internal static bool TryGetSlowed(
            RulesSnapshot snapshot,
            CreatureId owner,
            out ConditionSelection<SlowedConditionState> selection
        ) =>
            TrySelect(
                snapshot,
                owner,
                ConditionRuleDefinitions.Slowed,
                state => state.Value,
                out selection
            );

        internal static bool TryGetStunned(
            RulesSnapshot snapshot,
            CreatureId owner,
            out ConditionSelection<StunnedConditionState> selection
        ) =>
            TrySelect(
                snapshot,
                owner,
                ConditionRuleDefinitions.Stunned,
                state => state is ValuedStunnedConditionState valued ? valued.Value : 0,
                out selection
            );

        internal static bool TryGetQuickened(
            RulesSnapshot snapshot,
            CreatureId owner,
            out ConditionSelection<QuickenedConditionState> selection
        ) => TrySelect(snapshot, owner, ConditionRuleDefinitions.Quickened, _ => 0, out selection);

        private static bool TrySelect<TState>(
            RulesSnapshot snapshot,
            CreatureId owner,
            RuleDefinitionId definitionId,
            Func<TState, int> value,
            out ConditionSelection<TState> selection
        )
            where TState : IEffectState
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (owner.IsEmpty)
                throw new ArgumentException("A condition owner is required.", nameof(owner));

            // Binding.Owner is the affected creature. Effect.SourceCreature is provenance and can
            // legitimately be a different creature, so it must never drive condition selection.
            IEnumerable<ConditionSelection<TState>> candidates = snapshot
                .RuleBindings.Select(pair => pair.Value)
                .Where(binding =>
                    binding.Owner == owner
                    && binding.IsEnabled
                    && binding.DefinitionId == definitionId
                    && binding.EffectId.HasValue
                )
                .Select(binding => CreateCandidate<TState>(snapshot, definitionId, binding))
                .Where(candidate => candidate != null);

            selection = candidates
                .OrderByDescending(candidate => value(candidate.State))
                .ThenBy(candidate => candidate.Binding.CreationOrder)
                .ThenBy(candidate => candidate.BindingId.Value, StringComparer.Ordinal)
                .FirstOrDefault();
            return selection != null;
        }

        private static ConditionSelection<TState> CreateCandidate<TState>(
            RulesSnapshot snapshot,
            RuleDefinitionId definitionId,
            ActiveRuleBinding binding
        )
            where TState : IEffectState
        {
            if (
                !snapshot.ActiveEffects.TryGet(
                    binding.EffectId.Value,
                    out ActiveEffectInstance effect
                )
                || effect.Status != ActiveEffectStatus.Active
                || effect.DefinitionId != definitionId
                || effect.DefinitionId != binding.DefinitionId
                || effect.Source != binding.Source
                || !(effect.State is TState state)
                || !ConditionRuleDefinitions.Accepts(effect.DefinitionId, effect.State)
            )
            {
                return null;
            }

            return new ConditionSelection<TState>(effect, binding, state);
        }
    }
}
