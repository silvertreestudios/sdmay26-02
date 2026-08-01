using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class ConditionRuleDefinitions
    {
        internal static readonly RuleDefinitionId OffGuard = Id("off-guard");
        internal static readonly RuleDefinitionId Deafened = Id("deafened");
        internal static readonly RuleDefinitionId Fatigued = Id("fatigued");
        internal static readonly RuleDefinitionId Encumbered = Id("encumbered");
        internal static readonly RuleDefinitionId Slowed = Id("slowed");
        internal static readonly RuleDefinitionId Stunned = Id("stunned");
        internal static readonly RuleDefinitionId Quickened = Id("quickened");

        internal static RuleRegistryBuilder DefineAll(RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            builder.Define(OffGuard);
            builder.Define(Deafened);
            builder.Define(Fatigued);
            builder.Define(Encumbered);
            builder.Define(Slowed);
            builder.Define(Stunned);
            builder.Define(Quickened);
            return builder;
        }

        internal static bool Accepts(RuleDefinitionId definitionId, IEffectState state)
        {
            if (state == null)
                return false;

            Type stateType = state.GetType();
            if (IsMarker(definitionId))
                return stateType == typeof(ConditionMarkerState);
            if (definitionId == Slowed)
                return stateType == typeof(SlowedConditionState);
            if (definitionId == Stunned)
                return state is StunnedConditionState;
            if (definitionId == Quickened)
                return stateType == typeof(QuickenedConditionState);
            return false;
        }

        internal static bool IsMarker(RuleDefinitionId definitionId) =>
            definitionId == OffGuard
            || definitionId == Deafened
            || definitionId == Fatigued
            || definitionId == Encumbered;

        private static RuleDefinitionId Id(string slug) =>
            new RuleDefinitionId($"condition-{slug}");
    }

    internal static class ConditionInputNormalizer
    {
        internal static bool TryNormalize(string input, out RuleDefinitionId definitionId)
        {
            string slug = Pf2eSlug.FromName(input);
            // Flat-Footed is accepted only as external input. Authoritative state always uses the
            // remastered Off-Guard definition, so no legacy definition or runtime branch exists.
            if (slug == "flat-footed")
                slug = "off-guard";

            if (TryFromCanonicalSlug(slug, out definitionId))
                return true;

            definitionId = default;
            return false;
        }

        private static bool TryFromCanonicalSlug(string slug, out RuleDefinitionId definitionId)
        {
            switch (slug)
            {
                case "off-guard":
                    definitionId = ConditionRuleDefinitions.OffGuard;
                    return true;
                case "deafened":
                    definitionId = ConditionRuleDefinitions.Deafened;
                    return true;
                case "fatigued":
                    definitionId = ConditionRuleDefinitions.Fatigued;
                    return true;
                case "encumbered":
                    definitionId = ConditionRuleDefinitions.Encumbered;
                    return true;
                case "slowed":
                    definitionId = ConditionRuleDefinitions.Slowed;
                    return true;
                case "stunned":
                    definitionId = ConditionRuleDefinitions.Stunned;
                    return true;
                case "quickened":
                    definitionId = ConditionRuleDefinitions.Quickened;
                    return true;
                default:
                    definitionId = default;
                    return false;
            }
        }
    }

    internal sealed class ConditionMarkerState : IEffectState
    {
        internal static ConditionMarkerState Instance { get; } = new ConditionMarkerState();

        private ConditionMarkerState() { }
    }

    internal sealed class SlowedConditionState : IEffectState, IEquatable<SlowedConditionState>
    {
        internal SlowedConditionState(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "A Slowed value must be positive."
                );
            Value = value;
        }

        internal int Value { get; }

        public bool Equals(SlowedConditionState other) => other != null && Value == other.Value;

        public override bool Equals(object obj) =>
            obj is SlowedConditionState other && Equals(other);

        public override int GetHashCode() => Value;
    }

    internal abstract class StunnedConditionState : IEffectState
    {
        private protected StunnedConditionState() { }
    }

    internal sealed class ValuedStunnedConditionState
        : StunnedConditionState,
            IEquatable<ValuedStunnedConditionState>
    {
        internal ValuedStunnedConditionState(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "A valued Stunned condition must be positive."
                );
            Value = value;
        }

        internal int Value { get; }

        public bool Equals(ValuedStunnedConditionState other) =>
            other != null && Value == other.Value;

        public override bool Equals(object obj) =>
            obj is ValuedStunnedConditionState other && Equals(other);

        public override int GetHashCode() => Value;
    }

    internal sealed class DurationOnlyStunnedConditionState : StunnedConditionState
    {
        internal static DurationOnlyStunnedConditionState Instance { get; } =
            new DurationOnlyStunnedConditionState();

        private DurationOnlyStunnedConditionState() { }
    }

    internal sealed class QuickenedConditionState
        : IEffectState,
            IEquatable<QuickenedConditionState>
    {
        private readonly IReadOnlyList<ActionDefinitionId> allowedActions;
        private readonly HashSet<ActionDefinitionId> allowedActionLookup;

        internal QuickenedConditionState(IEnumerable<ActionDefinitionId> allowedActions)
        {
            if (allowedActions == null)
                throw new ArgumentNullException(nameof(allowedActions));

            ActionDefinitionId[] copied = allowedActions
                .Distinct()
                .OrderBy(action => action.Value, StringComparer.Ordinal)
                .ToArray();
            if (copied.Length == 0)
                throw new ArgumentException(
                    "Quickened must allow at least one action definition.",
                    nameof(allowedActions)
                );
            if (copied.Any(action => action.IsEmpty))
                throw new ArgumentException(
                    "Quickened cannot allow an empty action definition.",
                    nameof(allowedActions)
                );

            this.allowedActions = new ReadOnlyCollection<ActionDefinitionId>(copied);
            allowedActionLookup = new HashSet<ActionDefinitionId>(copied);
        }

        internal IReadOnlyList<ActionDefinitionId> AllowedActions => allowedActions;

        internal bool Allows(ActionDefinitionId action) => allowedActionLookup.Contains(action);

        public bool Equals(QuickenedConditionState other) =>
            other != null && allowedActions.SequenceEqual(other.allowedActions);

        public override bool Equals(object obj) =>
            obj is QuickenedConditionState other && Equals(other);

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (ActionDefinitionId action in allowedActions)
                hash = HashCode.Combine(hash, action);
            return hash;
        }
    }
}
