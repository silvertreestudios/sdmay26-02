using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Exposes the condition feature's explicit generic composition capabilities.</summary>
    public static class ConditionTurnResourceComposition
    {
        /// <summary>Creates the permission that blocks every action while Stunned remains.</summary>
        public static IActionPermission CreateActionPermission() => new StunnedActionPermission();

        /// <summary>Creates the provider for Quickened, Slowed, and elected Stunned state.</summary>
        public static ITurnResourceContributionProvider CreateProvider() =>
            new ConditionTurnResourceProvider();

        /// <summary>Creates the stable listener binding for one enrolled actor.</summary>
        public static ActiveRuleBinding CreateListenerBinding(CreatureId actor) =>
            ConditionTurnResourceRules.CreateListenerBinding(actor);
    }

    /// <summary>Owns condition-derived turn resources, Stunned permission, and mid-turn loss.</summary>
    internal static class ConditionTurnResourceRules
    {
        internal static readonly RuleDefinitionId ListenerDefinitionId = new(
            "condition-turn-resources"
        );
        internal static readonly RuleSource Source = RuleSource.FromSlug(
            "condition-turn-resources"
        );

        internal static RuleRegistryBuilder Define(RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            MidTurnStunnedListener listener = new();
            builder
                .Define(ListenerDefinitionId)
                .FactListener<ActiveEffectCreatedFact>(RuleLifecyclePhase.Reaction, listener)
                .FactListener<ActiveEffectStateUpdatedFact>(RuleLifecyclePhase.Reaction, listener);
            return builder;
        }

        internal static ActiveRuleBinding CreateListenerBinding(CreatureId actor) =>
            new(
                new BindingId($"condition-turn-resources-{actor.Value}"),
                ListenerDefinitionId,
                actor,
                default,
                Source,
                0
            );
    }

    internal sealed class ConditionTurnResourceProvider : ITurnResourceContributionProvider
    {
        public TurnResourceContributionBatch GetContributions(
            RulesSnapshot snapshot,
            CreatureId actor
        )
        {
            List<TurnResourceContribution> contributions = new();
            ActionAllowance quickened = ConditionSelectors.GetQuickenedAllowance(snapshot, actor);
            if (!quickened.IsNone)
                contributions.Add(TurnResourceContribution.OptionalAction(quickened));
            if (ConditionSelectors.TryGetSlowed(snapshot, actor, out var slowed))
                contributions.Add(TurnResourceContribution.StartTurnLoss(slowed.State.Value));

            ITurnResourceEffectAdjustment adjustment = NoTurnResourceEffectAdjustment.Instance;
            if (ConditionSelectors.TryGetStunned(snapshot, actor, out var stunned))
            {
                if (stunned.State is DurationOnlyStunnedConditionState)
                {
                    contributions.Add(
                        TurnResourceContribution.StunnedBy(StunnedResourcePolicy.DurationOnly)
                    );
                }
                else
                {
                    ValuedStunnedConditionState valued = (ValuedStunnedConditionState)stunned.State;
                    contributions.Add(
                        TurnResourceContribution.StunnedBy(
                            StunnedResourcePolicy.Valued(valued.Value)
                        )
                    );
                    adjustment = new ValuedStunnedTurnAdjustment(
                        ConditionSelectors.GetActiveInstances(
                            snapshot,
                            actor,
                            ConditionRuleDefinitions.Stunned
                        )
                    );
                }
            }
            return new TurnResourceContributionBatch(contributions, adjustment);
        }
    }

    internal sealed class ValuedStunnedTurnAdjustment : ITurnResourceEffectAdjustment
    {
        private readonly IReadOnlyList<ConditionSelection<StunnedConditionState>> selections;

        internal ValuedStunnedTurnAdjustment(
            IEnumerable<ConditionSelection<IEffectState>> activeStunned
        )
        {
            if (activeStunned == null)
                throw new ArgumentNullException(nameof(activeStunned));
            selections = activeStunned
                .Where(selection => selection.State is ValuedStunnedConditionState)
                .Select(selection => new ConditionSelection<StunnedConditionState>(
                    selection.Effect,
                    selection.Binding,
                    (StunnedConditionState)selection.State
                ))
                .ToArray();
            if (selections.Count == 0)
                throw new ArgumentException(
                    "At least one valued Stunned selection is required.",
                    nameof(activeStunned)
                );
            if (
                selections.Select(selection => selection.Owner).Distinct().Count() != 1
                || selections.Select(selection => selection.EffectId).Distinct().Count()
                    != selections.Count
                || selections.Select(selection => selection.BindingId).Distinct().Count()
                    != selections.Count
            )
                throw new ArgumentException(
                    "Valued Stunned adjustments require distinct effects for one owner.",
                    nameof(activeStunned)
                );
        }

        public bool IsPresent => true;

        public IReadOnlyList<TurnResourceEffectChange> CreateChanges(int resourcesConsumed)
        {
            if (resourcesConsumed <= 0)
                throw new ArgumentOutOfRangeException(nameof(resourcesConsumed));
            return selections
                .Select(selection =>
                {
                    int remaining =
                        ((ValuedStunnedConditionState)selection.State).Value - resourcesConsumed;
                    return new TurnResourceEffectChange(
                        selection.Owner,
                        selection.EffectId,
                        selection.BindingId,
                        selection.Version,
                        selection.Source,
                        remaining > 0 ? new ValuedStunnedConditionState(remaining) : null
                    );
                })
                .ToArray();
        }
    }

    internal sealed class StunnedActionPermission : IActionPermission
    {
        public ActionValidationResult Validate(
            ActionOpInfo action,
            ActionProfile profile,
            RulesSnapshot snapshot
        ) =>
            ConditionSelectors.TryGetStunned(snapshot, action.Actor, out _)
                ? ActionValidationResult.Invalid("A Stunned actor cannot take actions.")
                : ActionValidationResult.Valid;
    }

    internal sealed class CommitMidTurnStunnedLossOp : IRuleOp<MidTurnStunnedLossOutcome>
    {
        internal CommitMidTurnStunnedLossOp(
            TurnIdentity turn,
            ConditionSelection<StunnedConditionState> effective,
            ITurnResourceEffectAdjustment effectAdjustment
        )
        {
            Turn = turn;
            if (effective == null)
                throw new ArgumentNullException(nameof(effective));
            EffectId = effective.EffectId;
            BindingId = effective.BindingId;
            ExpectedVersion = effective.Version;
            Source = effective.Source;
            Stunned =
                effective.State is DurationOnlyStunnedConditionState
                    ? StunnedResourcePolicy.DurationOnly
                    : StunnedResourcePolicy.Valued(
                        ((ValuedStunnedConditionState)effective.State).Value
                    );
            EffectAdjustment =
                effectAdjustment ?? throw new ArgumentNullException(nameof(effectAdjustment));
            if (Stunned.Kind == StunnedResourcePolicyKind.Valued && !EffectAdjustment.IsPresent)
                throw new ArgumentException(
                    "Valued Stunned requires exact source adjustments.",
                    nameof(effectAdjustment)
                );
        }

        internal TurnIdentity Turn { get; }
        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal EffectStateVersion ExpectedVersion { get; }
        internal RuleSource Source { get; }
        internal StunnedResourcePolicy Stunned { get; }
        internal ITurnResourceEffectAdjustment EffectAdjustment { get; }
    }

    internal readonly struct MidTurnStunnedLossOutcome
    {
        internal MidTurnStunnedLossOutcome(int resourcesLost, bool stunnedRemains)
        {
            ResourcesLost = resourcesLost;
            StunnedRemains = stunnedRemains;
        }

        internal int ResourcesLost { get; }
        internal bool StunnedRemains { get; }
    }

    internal sealed class CommitMidTurnStunnedLossReducer
        : IOpReducer<CommitMidTurnStunnedLossOp, MidTurnStunnedLossOutcome>
    {
        public ReductionResult<MidTurnStunnedLossOutcome> Reduce(
            ReductionContext<CommitMidTurnStunnedLossOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            CommitMidTurnStunnedLossOp op = context.Op;
            if (
                !state.Encounters.TryGet(op.Turn.Encounter, out EncounterState encounter)
                || encounter.Phase != EncounterPhase.Active
                || !encounter.CurrentTurn.HasValue
                || encounter.CurrentTurn.Value != op.Turn
            )
                return ReductionResult<MidTurnStunnedLossOutcome>.Reject(
                    "The active turn identity is stale."
                );
            if (
                !ActiveEffectReduction.TryGetCurrent(
                    state,
                    op.EffectId,
                    op.ExpectedVersion,
                    true,
                    out ActiveEffectInstance effect,
                    out string rejection
                )
                || !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    op.BindingId,
                    true,
                    out ActiveRuleBinding binding,
                    out rejection
                )
                || !ActiveEffectReduction.TryValidateSourceOwner(effect, op.Source, out rejection)
            )
                return ReductionResult<MidTurnStunnedLossOutcome>.Reject(rejection);
            if (
                binding.Owner != op.Turn.Actor
                || effect.DefinitionId != ConditionRuleDefinitions.Stunned
                || op.Stunned.Kind == StunnedResourcePolicyKind.DurationOnly
                    && effect.State is not DurationOnlyStunnedConditionState
                || op.Stunned.Kind == StunnedResourcePolicyKind.Valued
                    && (
                        effect.State is not ValuedStunnedConditionState valued
                        || valued.Value != op.Stunned.Value
                    )
            )
                return ReductionResult<MidTurnStunnedLossOutcome>.Reject(
                    "The exact effective Stunned effect does not belong to the turn actor."
                );
            if (!state.ActionEconomy.TryGet(op.Turn.Actor, out ActionEconomyState economy))
                return ReductionResult<MidTurnStunnedLossOutcome>.Reject(
                    "The turn actor has no authoritative action economy."
                );

            int availableResources =
                economy.StandardActionsRemaining + (economy.OptionalAction.IsNone ? 0 : 1);
            int totalLost =
                op.Stunned.Kind == StunnedResourcePolicyKind.DurationOnly
                    ? availableResources
                    : Math.Min(availableResources, op.Stunned.Value);
            bool optionalLost = !economy.OptionalAction.IsNone && totalLost > 0;
            int standardLost = totalLost - (optionalLost ? 1 : 0);
            bool stunnedRemains =
                op.Stunned.Kind == StunnedResourcePolicyKind.DurationOnly
                || op.Stunned.Value > totalLost;
            if (
                totalLost > 0
                && op.EffectAdjustment.IsPresent
                && !TurnResourceEffectReduction.TryCommit(
                    state,
                    facts,
                    op.Turn.Actor,
                    op.EffectAdjustment,
                    totalLost,
                    out rejection
                )
            )
                return ReductionResult<MidTurnStunnedLossOutcome>.Reject(rejection);

            state.ActionEconomy.Set(
                op.Turn.Actor,
                new ActionEconomyState(
                    economy.StandardActionsRemaining - standardLost,
                    optionalLost ? ActionAllowance.None : economy.OptionalAction,
                    stunnedRemains ? false : economy.ReactionAvailable
                )
            );
            if (optionalLost)
                facts.Stage(
                    new ActionResourceLostFact(op.Turn.Actor, ActionResourceKind.Optional, 1)
                );
            if (standardLost > 0)
                facts.Stage(
                    new ActionResourceLostFact(
                        op.Turn.Actor,
                        ActionResourceKind.Standard,
                        standardLost
                    )
                );

            return ReductionResult<MidTurnStunnedLossOutcome>.Accept(
                new MidTurnStunnedLossOutcome(totalLost, stunnedRemains)
            );
        }
    }

    internal sealed class MidTurnStunnedListener
        : IRuleFactListener<ActiveEffectCreatedFact>,
            IRuleFactListener<ActiveEffectStateUpdatedFact>
    {
        public ValueTask OnFactCommitted(ActiveEffectCreatedFact fact, FactContext context) =>
            Drain(fact.EffectId, fact.DefinitionId, fact.SourceOpId, false, context);

        public ValueTask OnFactCommitted(ActiveEffectStateUpdatedFact fact, FactContext context) =>
            Drain(fact.EffectId, fact.DefinitionId, fact.SourceOpId, true, context);

        private static async ValueTask Drain(
            ActiveEffectId effectId,
            RuleDefinitionId definitionId,
            OpId sourceOpId,
            bool isStateUpdate,
            FactContext context
        )
        {
            if (definitionId != ConditionRuleDefinitions.Stunned)
                return;
            if (
                isStateUpdate
                && (
                    context.Trace.Require(sourceOpId).OpType == typeof(CommitTurnBeginOp)
                    || context.Trace.Require(sourceOpId).OpType
                        == typeof(CommitMidTurnStunnedLossOp)
                )
            )
                return;
            if (
                !context.Snapshot.ActiveEffects.TryGet(effectId, out ActiveEffectInstance effect)
                || effect.Status != ActiveEffectStatus.Active
                || effect.DefinitionId != ConditionRuleDefinitions.Stunned
            )
                return;
            ActiveRuleBinding binding = context
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .SingleOrDefault(candidate =>
                    candidate.IsEnabled
                    && candidate.EffectId.HasValue
                    && candidate.EffectId.Value == effect.Id
                    && candidate.DefinitionId == effect.DefinitionId
                    && candidate.Source == effect.Source
                );
            if (binding == null)
                return;
            if (context.Binding.Owner != binding.Owner)
                return;
            if (
                !ConditionSelectors.TryGetStunned(
                    context.Snapshot,
                    binding.Owner,
                    out ConditionSelection<StunnedConditionState> effective
                )
            )
                return;
            EncounterState[] encounters = context
                .Snapshot.Encounters.Select(pair => pair.Value)
                .Where(encounter =>
                    encounter.Phase == EncounterPhase.Active
                    && encounter.CurrentTurn.HasValue
                    && encounter.CurrentTurn.Value.Actor == binding.Owner
                )
                .ToArray();
            if (encounters.Length == 0)
                return;
            if (encounters.Length != 1)
                throw new InvalidOperationException(
                    "A creature cannot own more than one active encounter turn."
                );
            OpResult<MidTurnStunnedLossOutcome> result = await context.Dispatch(
                new CommitMidTurnStunnedLossOp(
                    encounters[0].CurrentTurn.Value,
                    effective,
                    effective.State is ValuedStunnedConditionState
                        ? new ValuedStunnedTurnAdjustment(
                            ConditionSelectors.GetActiveInstances(
                                context.Snapshot,
                                binding.Owner,
                                ConditionRuleDefinitions.Stunned
                            )
                        )
                        : NoTurnResourceEffectAdjustment.Instance
                )
            );
            if (result is InvalidOpResult<MidTurnStunnedLossOutcome> invalid)
                throw new InvalidOperationException(invalid.Reason);
            if (result is not ResolvedOpResult<MidTurnStunnedLossOutcome>)
                throw new InvalidOperationException(
                    "Mid-turn Stunned resource loss did not resolve."
                );
        }
    }
}
