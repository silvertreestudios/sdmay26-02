using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Pairs one active condition effect with its exact binding and typed state.</summary>
    public sealed class ConditionSelection<TState>
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

        /// <summary>Gets the authoritative active-effect instance.</summary>
        public ActiveEffectInstance Effect { get; }

        /// <summary>Gets the exact active binding.</summary>
        public ActiveRuleBinding Binding { get; }

        /// <summary>Gets the effect identity.</summary>
        public ActiveEffectId EffectId => Effect.Id;

        /// <summary>Gets the binding identity.</summary>
        public BindingId BindingId => Binding.Id;

        /// <summary>Gets the affected creature.</summary>
        public CreatureId Owner => Binding.Owner;

        /// <summary>Gets the stable source.</summary>
        public RuleSource Source => Effect.Source;

        /// <summary>Gets the optimistic state version.</summary>
        public EffectStateVersion Version => Effect.EffectStateVersion;

        /// <summary>Gets the typed condition state.</summary>
        public TState State { get; }
    }

    /// <summary>Provides pure derived reads over active-effect-owned conditions.</summary>
    public static class ConditionSelectors
    {
        /// <summary>Tests whether a marker condition is active for one creature.</summary>
        public static bool HasMarker(
            RulesSnapshot snapshot,
            CreatureId owner,
            RuleDefinitionId definitionId
        ) => TryGetMarker(snapshot, owner, definitionId, out _);

        /// <summary>Finds the first stable active marker source.</summary>
        public static bool TryGetMarker(
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

        /// <summary>Selects the highest active Slowed value without summing sources.</summary>
        public static bool TryGetSlowed(
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

        /// <summary>Selects duration-only Stunned first, otherwise the highest valued source.</summary>
        public static bool TryGetStunned(
            RulesSnapshot snapshot,
            CreatureId owner,
            out ConditionSelection<StunnedConditionState> selection
        ) =>
            TrySelect(
                snapshot,
                owner,
                ConditionRuleDefinitions.Stunned,
                state =>
                    state is DurationOnlyStunnedConditionState
                        ? int.MaxValue
                        : ((ValuedStunnedConditionState)state).Value,
                out selection
            );

        /// <summary>Unions active Quickened allowances, with unrestricted sources dominating.</summary>
        /// <param name="snapshot">The authoritative snapshot to inspect.</param>
        /// <param name="owner">The creature whose active Quickened sources are selected.</param>
        /// <returns>
        /// <see cref="ActionAllowance.None"/> when no source is active; otherwise the canonical
        /// union of every active source.
        /// </returns>
        public static ActionAllowance GetQuickenedAllowance(
            RulesSnapshot snapshot,
            CreatureId owner
        )
        {
            IReadOnlyList<ConditionSelection<QuickenedConditionState>> sources =
                SelectAll<QuickenedConditionState>(
                    snapshot,
                    owner,
                    ConditionRuleDefinitions.Quickened
                );
            return sources.Aggregate(
                ActionAllowance.None,
                (allowance, source) => allowance.Union(source.State.Allowance)
            );
        }

        /// <summary>Returns every valid active source in stable binding/effect order.</summary>
        public static IReadOnlyList<ConditionSelection<IEffectState>> GetActiveInstances(
            RulesSnapshot snapshot,
            CreatureId owner,
            RuleDefinitionId definitionId
        ) => SelectAll<IEffectState>(snapshot, owner, definitionId);

        /// <summary>Gets canonical active condition slugs in stable ordinal order.</summary>
        public static IReadOnlyList<string> GetActiveSlugs(RulesSnapshot snapshot, CreatureId owner)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (owner.IsEmpty)
                throw new ArgumentException("A condition owner is required.", nameof(owner));
            return snapshot
                .RuleBindings.Select(pair => pair.Value)
                .Where(binding =>
                    binding.Owner == owner && binding.IsEnabled && binding.EffectId.HasValue
                )
                .Select(binding =>
                    CreateCandidate<IEffectState>(snapshot, binding.DefinitionId, binding)
                )
                .Where(candidate => candidate != null)
                .Select(candidate => candidate.Effect.DefinitionId)
                .Select(definition =>
                    ConditionRuleDefinitions.TryGetCanonicalSlug(definition, out string slug)
                        ? slug
                        : string.Empty
                )
                .Where(slug => !string.IsNullOrEmpty(slug))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(slug => slug, StringComparer.Ordinal)
                .ToArray();
        }

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
            IReadOnlyList<ConditionSelection<TState>> candidates = SelectAll<TState>(
                snapshot,
                owner,
                definitionId
            );

            selection = candidates
                .OrderByDescending(candidate => value(candidate.State))
                .ThenBy(candidate => candidate.Binding.CreationOrder)
                .ThenBy(candidate => candidate.BindingId.Value, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.EffectId.Value, StringComparer.Ordinal)
                .FirstOrDefault();
            return selection != null;
        }

        private static IReadOnlyList<ConditionSelection<TState>> SelectAll<TState>(
            RulesSnapshot snapshot,
            CreatureId owner,
            RuleDefinitionId definitionId
        )
            where TState : IEffectState
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (owner.IsEmpty)
                throw new ArgumentException("A condition owner is required.", nameof(owner));

            return snapshot
                .RuleBindings.Select(pair => pair.Value)
                .Where(binding =>
                    binding.Owner == owner
                    && binding.IsEnabled
                    && binding.DefinitionId == definitionId
                    && binding.EffectId.HasValue
                )
                .Select(binding => CreateCandidate<TState>(snapshot, definitionId, binding))
                .Where(candidate => candidate != null)
                .OrderBy(candidate => candidate.Binding.CreationOrder)
                .ThenBy(candidate => candidate.BindingId.Value, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.EffectId.Value, StringComparer.Ordinal)
                .ToArray();
        }

        private static ConditionSelection<TState> CreateCandidate<TState>(
            RulesSnapshot snapshot,
            RuleDefinitionId definitionId,
            ActiveRuleBinding binding
        )
            where TState : IEffectState
        {
            if (
                !ActiveEffectAssociation.TryGetActive(
                    snapshot,
                    binding,
                    out ActiveEffectInstance effect
                )
                || effect.DefinitionId != definitionId
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
