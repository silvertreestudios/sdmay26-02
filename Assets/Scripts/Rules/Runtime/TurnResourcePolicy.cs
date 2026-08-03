using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Distinguishes the ordinary and optional action resources in a turn.</summary>
    public enum ActionResourceKind
    {
        /// <summary>An ordinary action from the creature's standard three-action pool.</summary>
        Standard,

        /// <summary>The single optional action allowance contributed by Quickened-like rules.</summary>
        Optional,
    }

    internal enum StunnedResourcePolicyKind
    {
        None,
        Valued,
        DurationOnly,
    }

    /// <summary>Keeps valued and duration-only Stunned planning inputs structurally distinct.</summary>
    internal readonly struct StunnedResourcePolicy : IEquatable<StunnedResourcePolicy>
    {
        private readonly int value;

        private StunnedResourcePolicy(StunnedResourcePolicyKind kind, int value)
        {
            Kind = kind;
            this.value = value;
        }

        internal static StunnedResourcePolicy None => default;
        internal static StunnedResourcePolicy DurationOnly =>
            new StunnedResourcePolicy(StunnedResourcePolicyKind.DurationOnly, 0);
        internal StunnedResourcePolicyKind Kind { get; }
        internal int Value =>
            Kind == StunnedResourcePolicyKind.Valued
                ? value
                : throw new InvalidOperationException("Only valued Stunned has a value.");

        internal static StunnedResourcePolicy Valued(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            return new StunnedResourcePolicy(StunnedResourcePolicyKind.Valued, value);
        }

        public bool Equals(StunnedResourcePolicy other) =>
            Kind == other.Kind && value == other.value;

        public override bool Equals(object obj) =>
            obj is StunnedResourcePolicy other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, value);
    }

    internal enum TurnResourceContributionKind
    {
        OptionalAction,
        StartTurnLoss,
        Stunned,
        SuppressReaction,
    }

    /// <summary>Carries one feature-owned input into generic deterministic turn planning.</summary>
    public sealed class TurnResourceContribution
    {
        private TurnResourceContribution(
            TurnResourceContributionKind kind,
            ActionAllowance allowance,
            int loss,
            StunnedResourcePolicy stunned
        )
        {
            Kind = kind;
            Allowance = allowance;
            Loss = loss;
            Stunned = stunned;
        }

        internal TurnResourceContributionKind Kind { get; }
        internal ActionAllowance Allowance { get; }
        internal int Loss { get; }
        internal StunnedResourcePolicy Stunned { get; }

        internal static TurnResourceContribution OptionalAction(ActionAllowance allowance)
        {
            if (allowance == null)
                throw new ArgumentNullException(nameof(allowance));
            if (allowance.IsNone)
                throw new ArgumentException(
                    "An optional action requires an allowance.",
                    nameof(allowance)
                );
            return new TurnResourceContribution(
                TurnResourceContributionKind.OptionalAction,
                allowance,
                0,
                StunnedResourcePolicy.None
            );
        }

        internal static TurnResourceContribution StartTurnLoss(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            return new TurnResourceContribution(
                TurnResourceContributionKind.StartTurnLoss,
                ActionAllowance.None,
                amount,
                StunnedResourcePolicy.None
            );
        }

        internal static TurnResourceContribution StunnedBy(StunnedResourcePolicy stunned)
        {
            if (stunned.Kind == StunnedResourcePolicyKind.None)
                throw new ArgumentException("A Stunned policy is required.", nameof(stunned));
            return new TurnResourceContribution(
                TurnResourceContributionKind.Stunned,
                ActionAllowance.None,
                0,
                stunned
            );
        }

        /// <summary>Suppresses the reaction resource without changing action resources.</summary>
        public static TurnResourceContribution SuppressReaction() =>
            new TurnResourceContribution(
                TurnResourceContributionKind.SuppressReaction,
                ActionAllowance.None,
                0,
                StunnedResourcePolicy.None
            );
    }

    /// <summary>Records exactly which granted action resources are removed at turn start.</summary>
    internal readonly struct TurnResourceLoss : IEquatable<TurnResourceLoss>
    {
        internal TurnResourceLoss(int standardActions, bool optionalAction)
        {
            if (standardActions < 0 || standardActions > TurnResourcePlanner.StandardActionCount)
                throw new ArgumentOutOfRangeException(nameof(standardActions));
            StandardActions = standardActions;
            OptionalAction = optionalAction;
        }

        internal int StandardActions { get; }
        internal bool OptionalAction { get; }
        internal int Total => StandardActions + (OptionalAction ? 1 : 0);

        public bool Equals(TurnResourceLoss other) =>
            StandardActions == other.StandardActions && OptionalAction == other.OptionalAction;

        public override bool Equals(object obj) => obj is TurnResourceLoss other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(StandardActions, OptionalAction);
    }

    /// <summary>Contains the pure result of one turn-resource refresh.</summary>
    internal sealed class TurnResourcePlan
    {
        internal TurnResourcePlan(
            ActionEconomyState economy,
            ActionAllowance grantedOptionalAction,
            TurnResourceLoss loss,
            int stunnedConsumed,
            int stunnedRemaining
        )
        {
            Economy = economy;
            GrantedOptionalAction =
                grantedOptionalAction
                ?? throw new ArgumentNullException(nameof(grantedOptionalAction));
            Loss = loss;
            StunnedConsumed = stunnedConsumed;
            StunnedRemaining = stunnedRemaining;
        }

        internal ActionEconomyState Economy { get; }
        internal ActionAllowance GrantedOptionalAction { get; }
        internal TurnResourceLoss Loss { get; }
        internal int StunnedConsumed { get; }
        internal int StunnedRemaining { get; }
    }

    /// <summary>Applies all start-turn grants and losses without reading or mutating rules state.</summary>
    internal static class TurnResourcePlanner
    {
        internal const int StandardActionCount = 3;

        internal static TurnResourcePlan Resolve(
            IEnumerable<TurnResourceContribution> contributions
        )
        {
            if (contributions == null)
                throw new ArgumentNullException(nameof(contributions));
            TurnResourceContribution[] copied = contributions.ToArray();
            if (copied.Any(contribution => contribution == null))
                throw new ArgumentException(
                    "Turn-resource contributions cannot contain null.",
                    nameof(contributions)
                );

            ActionAllowance optional = copied
                .Where(contribution =>
                    contribution.Kind == TurnResourceContributionKind.OptionalAction
                )
                .Select(contribution => contribution.Allowance)
                .Aggregate(ActionAllowance.None, (current, allowance) => current.Union(allowance));
            int slowedLoss = copied
                .Where(contribution =>
                    contribution.Kind == TurnResourceContributionKind.StartTurnLoss
                )
                .Select(contribution => contribution.Loss)
                .DefaultIfEmpty(0)
                .Max();
            StunnedResourcePolicy[] stunned = copied
                .Where(contribution => contribution.Kind == TurnResourceContributionKind.Stunned)
                .Select(contribution => contribution.Stunned)
                .ToArray();
            if (stunned.Length > 1)
                throw new ArgumentException(
                    "Turn planning requires one already-elected Stunned source.",
                    nameof(contributions)
                );

            StunnedResourcePolicy elected = stunned.SingleOrDefault();
            int totalResources = StandardActionCount + (optional.IsNone ? 0 : 1);
            int requestedLoss = elected.Kind switch
            {
                StunnedResourcePolicyKind.DurationOnly => totalResources,
                StunnedResourcePolicyKind.Valued => Math.Max(slowedLoss, elected.Value),
                _ => slowedLoss,
            };
            int actualLoss = Math.Min(totalResources, requestedLoss);
            bool optionalLost = !optional.IsNone && actualLoss > 0;
            int standardLost = actualLoss - (optionalLost ? 1 : 0);
            int stunnedConsumed =
                elected.Kind == StunnedResourcePolicyKind.Valued
                    ? Math.Min(elected.Value, actualLoss)
                    : 0;
            int stunnedRemaining =
                elected.Kind == StunnedResourcePolicyKind.Valued
                    ? elected.Value - stunnedConsumed
                    : 0;
            bool reactionAvailable =
                elected.Kind != StunnedResourcePolicyKind.DurationOnly
                && stunnedRemaining == 0
                && !copied.Any(contribution =>
                    contribution.Kind == TurnResourceContributionKind.SuppressReaction
                );
            ActionEconomyState economy = new(
                StandardActionCount - standardLost,
                optionalLost ? ActionAllowance.None : optional,
                reactionAvailable
            );
            return new TurnResourcePlan(
                economy,
                optional,
                new TurnResourceLoss(standardLost, optionalLost),
                stunnedConsumed,
                stunnedRemaining
            );
        }
    }

    /// <summary>Creates feature-owned turn-resource contributions from one authoritative snapshot.</summary>
    public interface ITurnResourceContributionProvider
    {
        /// <summary>Reads one actor's immutable turn-resource inputs.</summary>
        TurnResourceContributionBatch GetContributions(RulesSnapshot snapshot, CreatureId actor);
    }

    /// <summary>Describes one exact active-effect update or expiration.</summary>
    internal sealed class TurnResourceEffectChange
    {
        internal TurnResourceEffectChange(
            CreatureId owner,
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion expectedVersion,
            RuleSource source,
            IEffectState remainingState
        )
        {
            Owner = owner;
            EffectId = effectId;
            BindingId = bindingId;
            ExpectedVersion = expectedVersion;
            Source = source;
            RemainingState = remainingState;
        }

        internal CreatureId Owner { get; }
        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal EffectStateVersion ExpectedVersion { get; }
        internal RuleSource Source { get; }
        internal IEffectState RemainingState { get; }
    }

    /// <summary>Creates exact changes for resource-consuming active effects.</summary>
    internal interface ITurnResourceEffectAdjustment
    {
        bool IsPresent { get; }
        IReadOnlyList<TurnResourceEffectChange> CreateChanges(int resourcesConsumed);
    }

    internal sealed class NoTurnResourceEffectAdjustment : ITurnResourceEffectAdjustment
    {
        internal static NoTurnResourceEffectAdjustment Instance { get; } = new();

        private NoTurnResourceEffectAdjustment() { }

        public bool IsPresent => false;

        public IReadOnlyList<TurnResourceEffectChange> CreateChanges(int resourcesConsumed) =>
            throw new InvalidOperationException("No turn-resource effect is selected.");
    }

    /// <summary>Commits a feature-prepared set of exact active-effect changes atomically.</summary>
    internal static class TurnResourceEffectReduction
    {
        internal static bool TryCommit(
            RulesStateDraft state,
            FactSink facts,
            CreatureId actor,
            ITurnResourceEffectAdjustment adjustment,
            int resourcesConsumed,
            out string rejection
        )
        {
            rejection = string.Empty;
            if (resourcesConsumed <= 0)
            {
                rejection = "A selected turn-resource effect must consume at least one resource.";
                return false;
            }
            IReadOnlyList<TurnResourceEffectChange> changes = adjustment.CreateChanges(
                resourcesConsumed
            );
            if (
                changes == null
                || changes.Count == 0
                || changes.Any(change => change == null)
                || changes.Select(change => change.EffectId).Distinct().Count() != changes.Count
                || changes.Select(change => change.BindingId).Distinct().Count() != changes.Count
            )
            {
                rejection = "Turn-resource effect changes must be nonempty and exact.";
                return false;
            }

            foreach (TurnResourceEffectChange change in changes)
            {
                if (change.Owner != actor)
                {
                    rejection = "A turn-resource effect does not belong to the turn actor.";
                    return false;
                }
                if (
                    !ActiveEffectReduction.TryGetCurrent(
                        state,
                        change.EffectId,
                        change.ExpectedVersion,
                        true,
                        out ActiveEffectInstance effect,
                        out rejection
                    )
                    || !ActiveEffectReduction.TryGetAssociatedBinding(
                        state,
                        effect,
                        change.BindingId,
                        true,
                        out ActiveRuleBinding binding,
                        out rejection
                    )
                    || !ActiveEffectReduction.TryValidateSourceOwner(
                        effect,
                        change.Source,
                        out rejection
                    )
                )
                    return false;
                if (binding.Owner != actor)
                {
                    rejection = "A turn-resource effect does not belong to the turn actor.";
                    return false;
                }

                if (change.RemainingState != null)
                {
                    if (change.RemainingState.GetType() != effect.State.GetType())
                    {
                        rejection =
                            "The turn-resource effect replacement has the wrong state type.";
                        return false;
                    }
                    ActiveEffectReduction.CommitStateUpdate(
                        state,
                        effect,
                        change.RemainingState,
                        facts
                    );
                    continue;
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
            }
            return true;
        }
    }

    /// <summary>Groups one module's immutable contributions and optional exact effect adjustment.</summary>
    public sealed class TurnResourceContributionBatch
    {
        /// <summary>Creates contributions that do not adjust an active effect.</summary>
        public TurnResourceContributionBatch(IEnumerable<TurnResourceContribution> contributions)
            : this(contributions, NoTurnResourceEffectAdjustment.Instance) { }

        internal TurnResourceContributionBatch(
            IEnumerable<TurnResourceContribution> contributions,
            ITurnResourceEffectAdjustment effectAdjustment
        )
        {
            Contributions = Array.AsReadOnly(
                contributions?.ToArray() ?? throw new ArgumentNullException(nameof(contributions))
            );
            if (Contributions.Any(contribution => contribution == null))
                throw new ArgumentException(
                    "Turn-resource contributions cannot contain null.",
                    nameof(contributions)
                );
            EffectAdjustment =
                effectAdjustment ?? throw new ArgumentNullException(nameof(effectAdjustment));
        }

        internal IReadOnlyList<TurnResourceContribution> Contributions { get; }
        internal ITurnResourceEffectAdjustment EffectAdjustment { get; }
    }

    /// <summary>Freezes a complete plan plus the exact feature effect adjusted by its loss.</summary>
    internal sealed class TurnResourceCommitPlan
    {
        internal TurnResourceCommitPlan(
            TurnResourcePlan resources,
            ITurnResourceEffectAdjustment effectAdjustment
        )
        {
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            EffectAdjustment =
                effectAdjustment ?? throw new ArgumentNullException(nameof(effectAdjustment));
        }

        internal TurnResourcePlan Resources { get; }
        internal ITurnResourceEffectAdjustment EffectAdjustment { get; }
    }

    /// <summary>Builds one deterministic turn plan from explicitly composed feature providers.</summary>
    public sealed class TurnResourceStrategy
    {
        private readonly IReadOnlyList<ITurnResourceContributionProvider> providers;

        /// <summary>Creates a strategy from providers in deterministic composition order.</summary>
        public TurnResourceStrategy(IEnumerable<ITurnResourceContributionProvider> providers)
        {
            ITurnResourceContributionProvider[] copied =
                providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
            if (copied.Any(provider => provider == null))
                throw new ArgumentException(
                    "Turn-resource providers cannot contain null.",
                    nameof(providers)
                );
            this.providers = Array.AsReadOnly(copied);
        }

        internal TurnResourceCommitPlan CreatePlan(RulesSnapshot snapshot, CreatureId actor)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (actor.IsEmpty)
                throw new ArgumentException("A turn actor is required.", nameof(actor));
            TurnResourceContributionBatch[] batches = providers
                .Select(provider => provider.GetContributions(snapshot, actor))
                .ToArray();
            if (batches.Any(batch => batch == null))
                throw new InvalidOperationException("A turn-resource provider returned null.");
            ITurnResourceEffectAdjustment[] adjustments = batches
                .Select(batch => batch.EffectAdjustment)
                .Where(adjustment => adjustment.IsPresent)
                .ToArray();
            if (adjustments.Length > 1)
                throw new InvalidOperationException(
                    "Only one exact turn-resource effect may be adjusted per turn."
                );
            return new TurnResourceCommitPlan(
                TurnResourcePlanner.Resolve(batches.SelectMany(batch => batch.Contributions)),
                adjustments.SingleOrDefault() ?? NoTurnResourceEffectAdjustment.Instance
            );
        }
    }

    /// <summary>Plans one trusted action-economy cost for both preview and commit.</summary>
    public static class ActionResourcePayment
    {
        /// <summary>Checks whether the exact top-level action can pay its frozen cost.</summary>
        public static bool CanPay(
            ActionEconomyState economy,
            ActionDefinitionId definitionId,
            ActionProfile profile
        ) => TryPay(economy, definitionId, profile, out _, out _);

        internal static bool TryPay(
            ActionEconomyState economy,
            ActionDefinitionId definitionId,
            ActionProfile profile,
            out ActionEconomyState remaining,
            out ActionResourceKind? spentResource
        )
        {
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "An action definition is required.",
                    nameof(definitionId)
                );
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            remaining = economy;
            spentResource = null;
            ActionCost cost = profile.Cost;
            if (cost.Kind == ActionCostKind.None || cost.Kind == ActionCostKind.FreeAction)
                return true;
            if (cost.Kind == ActionCostKind.Reaction)
            {
                if (!economy.ReactionAvailable)
                    return false;
                remaining = new ActionEconomyState(
                    economy.StandardActionsRemaining,
                    economy.OptionalAction,
                    false
                );
                return true;
            }
            if (cost.Kind != ActionCostKind.Actions)
                throw new InvalidOperationException($"Unsupported action cost kind {cost.Kind}.");

            if (economy.OptionalAction.Allows(definitionId, profile))
            {
                remaining = new ActionEconomyState(
                    economy.StandardActionsRemaining,
                    ActionAllowance.None,
                    economy.ReactionAvailable
                );
                spentResource = ActionResourceKind.Optional;
                return true;
            }
            if (economy.StandardActionsRemaining < cost.Amount)
                return false;
            remaining = new ActionEconomyState(
                economy.StandardActionsRemaining - cost.Amount,
                economy.OptionalAction,
                economy.ReactionAvailable
            );
            spentResource = ActionResourceKind.Standard;
            return true;
        }
    }
}
