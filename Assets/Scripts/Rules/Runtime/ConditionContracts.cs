using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Declares the canonical condition definitions supported by the rules store.</summary>
    public static class ConditionRuleDefinitions
    {
        /// <summary>The canonical remastered Off-Guard definition.</summary>
        public static readonly RuleDefinitionId OffGuard = Id("off-guard");

        /// <summary>The Deafened marker definition.</summary>
        public static readonly RuleDefinitionId Deafened = Id("deafened");

        /// <summary>The Fatigued marker definition.</summary>
        public static readonly RuleDefinitionId Fatigued = Id("fatigued");

        /// <summary>The Encumbered marker definition.</summary>
        public static readonly RuleDefinitionId Encumbered = Id("encumbered");

        /// <summary>The valued Slowed definition.</summary>
        public static readonly RuleDefinitionId Slowed = Id("slowed");

        /// <summary>The valued-or-duration-only Stunned definition.</summary>
        public static readonly RuleDefinitionId Stunned = Id("stunned");

        /// <summary>The Quickened definition with an immutable action allowance.</summary>
        public static readonly RuleDefinitionId Quickened = Id("quickened");

        /// <summary>Defines every canonical condition and its definition-local middleware.</summary>
        public static RuleRegistryBuilder DefineAll(RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            builder
                .Define(OffGuard)
                .Middleware<CollectDefenseModifiersOp, ModifierCollection>(
                    RuleLifecyclePhase.Transformation,
                    new OffGuardDefenseMiddleware()
                );
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

    /// <summary>Normalizes external condition names to canonical runtime definitions.</summary>
    public static class ConditionInputNormalizer
    {
        /// <summary>Attempts to normalize a user, JSON, or persistence condition name.</summary>
        public static bool TryNormalize(string input, out RuleDefinitionId definitionId)
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

    /// <summary>Represents presence-only condition state without an invented numeric value.</summary>
    public sealed class ConditionMarkerState : IEffectState
    {
        /// <summary>Gets the immutable marker singleton.</summary>
        public static ConditionMarkerState Instance { get; } = new ConditionMarkerState();

        private ConditionMarkerState() { }
    }

    /// <summary>Stores the positive value of one Slowed source.</summary>
    public sealed class SlowedConditionState : IEffectState, IEquatable<SlowedConditionState>
    {
        /// <summary>Creates a positive Slowed value.</summary>
        public SlowedConditionState(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "A Slowed value must be positive."
                );
            Value = value;
        }

        /// <summary>Gets the source's Slowed value.</summary>
        public int Value { get; }

        public bool Equals(SlowedConditionState other) => other != null && Value == other.Value;

        public override bool Equals(object obj) =>
            obj is SlowedConditionState other && Equals(other);

        public override int GetHashCode() => Value;
    }

    /// <summary>Base type for the mutually exclusive valued and duration-only Stunned forms.</summary>
    public abstract class StunnedConditionState : IEffectState
    {
        private protected StunnedConditionState() { }
    }

    /// <summary>Stores the positive value of one valued Stunned source.</summary>
    public sealed class ValuedStunnedConditionState
        : StunnedConditionState,
            IEquatable<ValuedStunnedConditionState>
    {
        /// <summary>Creates a positive valued Stunned state.</summary>
        public ValuedStunnedConditionState(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "A valued Stunned condition must be positive."
                );
            Value = value;
        }

        /// <summary>Gets the source's Stunned value.</summary>
        public int Value { get; }

        public bool Equals(ValuedStunnedConditionState other) =>
            other != null && Value == other.Value;

        public override bool Equals(object obj) =>
            obj is ValuedStunnedConditionState other && Equals(other);

        public override int GetHashCode() => Value;
    }

    /// <summary>Represents duration-only Stunned, which dominates valued sources.</summary>
    public sealed class DurationOnlyStunnedConditionState : StunnedConditionState
    {
        /// <summary>Gets the immutable duration-only singleton.</summary>
        public static DurationOnlyStunnedConditionState Instance { get; } =
            new DurationOnlyStunnedConditionState();

        private DurationOnlyStunnedConditionState() { }
    }

    /// <summary>Stores an unrestricted or immutable restricted Quickened action allowance.</summary>
    public sealed class QuickenedConditionState : IEffectState, IEquatable<QuickenedConditionState>
    {
        private readonly IReadOnlyList<ActionDefinitionId> allowedActions;
        private readonly HashSet<ActionDefinitionId> allowedActionLookup;

        /// <summary>Creates a restricted Quickened source.</summary>
        public QuickenedConditionState(IEnumerable<ActionDefinitionId> allowedActions)
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
            IsRestricted = true;
        }

        private QuickenedConditionState()
        {
            allowedActions = Array.AsReadOnly(Array.Empty<ActionDefinitionId>());
            allowedActionLookup = new HashSet<ActionDefinitionId>();
            IsRestricted = false;
        }

        /// <summary>Gets the unrestricted Quickened state.</summary>
        public static QuickenedConditionState Unrestricted { get; } = new QuickenedConditionState();

        /// <summary>Gets whether this source restricts its additional action.</summary>
        public bool IsRestricted { get; }

        /// <summary>Gets the canonical allowed definitions, empty only when unrestricted.</summary>
        public IReadOnlyList<ActionDefinitionId> AllowedActions => allowedActions;

        /// <summary>Tests whether the source permits the supplied action.</summary>
        public bool Allows(ActionDefinitionId action) =>
            !IsRestricted || allowedActionLookup.Contains(action);

        public bool Equals(QuickenedConditionState other) =>
            other != null
            && IsRestricted == other.IsRestricted
            && allowedActions.SequenceEqual(other.allowedActions);

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
