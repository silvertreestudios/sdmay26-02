using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Composes the authoritative encounter lifecycle into an existing dispatcher.</summary>
    public static class EncounterRuleRuntime
    {
        internal static readonly RuleDefinitionId OutcomeDefinitionId = new RuleDefinitionId(
            "encounter-outcome"
        );
        internal static readonly RuleSource Source = RuleSource.FromSlug("encounter-lifecycle");

        internal static BindingId OutcomeBindingId(EncounterId encounter) =>
            new BindingId($"encounter-outcome:{encounter.Value}");

        /// <summary>
        /// Adds encounter-owned outcome validation and observation to a shared registry builder.
        /// </summary>
        /// <param name="builder">The registry builder used by the shared dispatcher.</param>
        /// <returns>The same builder so registry composition can continue.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        public static RuleRegistryBuilder AddOutcomeRule(this RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder
                .Define(OutcomeDefinitionId)
                .Middleware<EndEncounterOp, EncounterEndOutcome>(
                    RuleLifecyclePhase.Prevention,
                    new EncounterEndValidationMiddleware()
                )
                .FactBatchListener(RuleLifecyclePhase.Observation, new EncounterOutcomeListener());
            return builder;
        }

        /// <summary>Registers encounter handlers and reducer-owned transitions on one dispatcher.</summary>
        /// <param name="builder">The shared dispatcher builder.</param>
        /// <returns>The same builder with encounter rules and no transitional start adapters.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        public static RuleDispatcherBuilder UseEncounterRules(this RuleDispatcherBuilder builder) =>
            UseEncounterRules(builder, Array.Empty<IEncounterTurnStartAdapter>());

        /// <summary>
        /// Registers encounter transitions plus ordered adapters for unmigrated turn-start behavior.
        /// </summary>
        /// <param name="builder">The shared dispatcher builder that owns all encounter rules.</param>
        /// <param name="turnStartAdapters">
        /// The spell, aura, and action-contribution adapters to await in exact registration order.
        /// </param>
        /// <returns>The same builder so composition can continue.</returns>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        public static RuleDispatcherBuilder UseEncounterRules(
            this RuleDispatcherBuilder builder,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            IEncounterTurnStartAdapter[] copied =
                turnStartAdapters?.ToArray()
                ?? throw new ArgumentNullException(nameof(turnStartAdapters));
            if (copied.Any(adapter => adapter == null))
                throw new ArgumentException(
                    "Turn-start adapters cannot contain null entries.",
                    nameof(turnStartAdapters)
                );
            return builder
                .RegisterHandler<StartEncounterOp, EncounterStartOutcome>(
                    new StartEncounterHandler()
                )
                .RegisterHandler<JoinEncounterOp, EncounterJoinOutcome>(new JoinEncounterHandler())
                .RegisterHandler<AdvanceEncounterOp, EncounterAdvanceOutcome>(
                    new AdvanceEncounterHandler()
                )
                .RegisterHandler<EndTurnOp, EncounterAdvanceOutcome>(new EndTurnHandler())
                .RegisterHandler<SuspendEncounterOp, EncounterSuspensionOutcome>(
                    new SuspendEncounterHandler()
                )
                .RegisterHandler<EndEncounterOp, EncounterEndOutcome>(new EndEncounterHandler())
                .RegisterHandler<EvaluateEncounterOutcomeOp, EncounterEvaluationOutcome>(
                    new EvaluateEncounterOutcomeHandler()
                )
                .RegisterHandler<TurnStartingOp, TurnStartContribution>(
                    new TurnStartingHandler(copied),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<TurnEndingOp, TurnEndContribution>(
                    new TurnEndingHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<SpendLegacyActionsOp, LegacyActionSpendOutcome>(
                    new SpendLegacyActionsHandler()
                )
                .RegisterHandler<IncrementLegacyMapOp, LegacyMapOutcome>(
                    new IncrementLegacyMapHandler()
                )
                .RegisterEngineReducer<CommitEncounterStartOp, EncounterStartOutcome>(
                    new CommitEncounterStartReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitEncounterJoinOp, EncounterJoinOutcome>(
                    new CommitEncounterJoinReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitInitiativeAssignmentsOp, InitiativeAssignmentsOutcome>(
                    new CommitInitiativeAssignmentsReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitInitiativeBoundaryOp, InitiativeBoundaryOutcome>(
                    new CommitInitiativeBoundaryReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitTurnBeginOp, EncounterAdvanceOutcome>(
                    new CommitTurnBeginReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitTurnEndOp, EncounterAdvanceOutcome>(
                    new CommitTurnEndReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitEncounterSuspendOp, EncounterSuspensionOutcome>(
                    new CommitEncounterSuspendReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitEncounterEndOp, EncounterEndOutcome>(
                    new CommitEncounterEndReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitLegacyActionsOp, LegacyActionSpendOutcome>(
                    new SpendLegacyActionsReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitLegacyMapOp, LegacyMapOutcome>(
                    new IncrementLegacyMapReducer(),
                    Source
                );
        }

        internal static EncounterState RequireEncounter(RulesSnapshot snapshot, EncounterId id)
        {
            if (!snapshot.Encounters.TryGet(id, out EncounterState encounter))
                throw new InvalidOperationException($"Encounter {id.Value} is unknown.");
            return encounter;
        }

        internal static EncounterOutcome? Evaluate(
            RulesSnapshot snapshot,
            EncounterState encounter
        ) =>
            EncounterEndValidation.Evaluate(
                encounter,
                creature => EncounterEndValidation.IsLiving(snapshot, creature)
            );
    }

    internal static class EncounterHandlerResults
    {
        public static TResult Require<TResult>(OpResult<TResult> result, string work)
        {
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(
                    $"Mandatory {work} was invalid: {invalid.Reason}"
                );
            throw new InvalidOperationException($"Mandatory {work} returned {result.Status}.");
        }
    }

    internal sealed class StartEncounterHandler
        : IOpHandler<StartEncounterOp, EncounterStartOutcome>
    {
        public async ValueTask<EncounterStartOutcome> Handle(
            OpFrame<StartEncounterOp> frame,
            OpHandlerContext context
        )
        {
            if (
                context.Snapshot.Encounters.Contains(frame.Op.Encounter)
                || context.Snapshot.Encounters.Any(pair =>
                    pair.Value.Phase == EncounterPhase.Active
                )
            )
                throw new InvalidOperationException(
                    "The encounter is duplicate or another encounter is active."
                );
            InitiativeEntry[] roster = frame
                .Op.Participants.Select(
                    (participant, index) =>
                        new InitiativeEntry(
                            participant.Creature,
                            participant.Team,
                            context.Rolls.Roll(DiceExpressions.D20).Total,
                            participant.InitiativeModifier,
                            index,
                            RoundNumber.First
                        )
                )
                .OrderByDescending(entry => entry.Total)
                .ThenBy(entry => entry.RegistrationOrder)
                .ToArray();
            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitEncounterStartOp(
                        frame.Op.Encounter,
                        frame.Op.ProtagonistTeam,
                        Array.AsReadOnly(roster)
                    )
                ),
                "encounter start"
            );
            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitInitiativeAssignmentsOp(frame.Op.Encounter, Array.AsReadOnly(roster))
                ),
                "initial initiative assignment"
            );
            EncounterAdvanceOutcome advanced = EncounterHandlerResults.Require(
                await context.Dispatch(new AdvanceEncounterOp(frame.Op.Encounter)),
                "first turn advance"
            );
            return new EncounterStartOutcome(advanced.State);
        }
    }

    internal sealed class JoinEncounterHandler : IOpHandler<JoinEncounterOp, EncounterJoinOutcome>
    {
        public async ValueTask<EncounterJoinOutcome> Handle(
            OpFrame<JoinEncounterOp> frame,
            OpHandlerContext context
        )
        {
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            if (encounter.Phase != EncounterPhase.Active || !encounter.CurrentTurn.HasValue)
                throw new InvalidOperationException(
                    "Reinforcements require an active encounter turn."
                );
            InitiativeEntry current = encounter.Roster[encounter.Cursor];
            InitiativeEntry[] additions = frame
                .Op.Participants.Select(
                    (participant, index) =>
                    {
                        int natural = context.Rolls.Roll(DiceExpressions.D20).Total;
                        int total = checked(natural + participant.Participant.InitiativeModifier);
                        RoundNumber eligible =
                            total > current.Total ? encounter.Round.Next() : encounter.Round;
                        return new InitiativeEntry(
                            participant.Participant.Creature,
                            participant.Participant.Team,
                            natural,
                            participant.Participant.InitiativeModifier,
                            encounter.Roster.Count + index,
                            eligible
                        );
                    }
                )
                .ToArray();
            EncounterJoinOutcome joined = EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitEncounterJoinOp(
                        frame.Op.Encounter,
                        Array.AsReadOnly(additions),
                        frame.Op.Participants.ToDictionary(
                            value => value.Participant.Creature,
                            value => value.Combatant
                        )
                    )
                ),
                "encounter join"
            );
            // A binding created by the join reducer cannot observe Facts sourced from that same
            // frame. Publishing assignments from this later authoritative frame lets every
            // reinforcement feature observe its committed initiative exactly once.
            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitInitiativeAssignmentsOp(
                        frame.Op.Encounter,
                        Array.AsReadOnly(additions)
                    )
                ),
                "reinforcement initiative assignment"
            );
            return joined;
        }
    }

    internal sealed class AdvanceEncounterHandler
        : IOpHandler<AdvanceEncounterOp, EncounterAdvanceOutcome>
    {
        public async ValueTask<EncounterAdvanceOutcome> Handle(
            OpFrame<AdvanceEncounterOp> frame,
            OpHandlerContext context
        )
        {
            EncounterState initial = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            if (initial.Phase != EncounterPhase.Active || initial.CurrentTurn.HasValue)
                throw new InvalidOperationException(
                    "Encounter advancement requires an active encounter without a current turn."
                );
            EncounterOutcome? immediate = EncounterRuleRuntime.Evaluate(context.Snapshot, initial);
            if (immediate.HasValue)
            {
                EncounterEndOutcome ended = EncounterHandlerResults.Require(
                    await context.Dispatch(new EndEncounterOp(initial.Id, immediate.Value)),
                    "encounter outcome"
                );
                return new EncounterAdvanceOutcome(ended.State);
            }
            int attempts = initial.Roster.Count;
            while (attempts-- > 0)
            {
                InitiativeBoundaryOutcome boundary = EncounterHandlerResults.Require(
                    await context.Dispatch(new CommitInitiativeBoundaryOp(frame.Op.Encounter)),
                    "initiative boundary"
                );
                foreach (ActiveEffectTimingState timing in boundary.DueEffects)
                {
                    if (
                        !context.Snapshot.ActiveEffects.TryGet(
                            timing.Effect,
                            out ActiveEffectInstance effect
                        )
                        || effect.Status != ActiveEffectStatus.Active
                    )
                        continue;
                    EncounterHandlerResults.Require(
                        await context.Dispatch(
                            new ExpireActiveEffectOp(
                                effect.Id,
                                timing.Binding,
                                effect.EffectStateVersion,
                                effect.Source
                            )
                        ),
                        "timed effect expiration"
                    );
                }
                if (boundary.Entry.EligibleFromRound.CompareTo(boundary.State.Round) > 0)
                    continue;
                if (!EncounterEndValidation.IsLiving(context.Snapshot, boundary.Entry.Creature))
                    continue;
                TurnStartContribution contribution = EncounterHandlerResults.Require(
                    await context.Dispatch(
                        new TurnStartingOp(frame.Op.Encounter, boundary.Entry.Creature)
                    ),
                    "turn-start hook"
                );
                EncounterState latest = EncounterRuleRuntime.RequireEncounter(
                    context.Snapshot,
                    frame.Op.Encounter
                );
                if (!EncounterEndValidation.IsLiving(context.Snapshot, boundary.Entry.Creature))
                {
                    // The hook's zero-HP Fact still belongs to this outer root. Return without a
                    // turn so its Reaction listeners settle before the encounter-owned
                    // Observation listener decides the outcome or advances beyond this slot.
                    return new EncounterAdvanceOutcome(latest);
                }
                return EncounterHandlerResults.Require(
                    await context.Dispatch(
                        new CommitTurnBeginOp(
                            frame.Op.Encounter,
                            boundary.Entry.Creature,
                            contribution.Actions
                        )
                    ),
                    "turn begin"
                );
            }
            throw new InvalidOperationException(
                "An active encounter had no eligible living slot and no outcome."
            );
        }
    }

    internal sealed class EndTurnHandler : IOpHandler<EndTurnOp, EncounterAdvanceOutcome>
    {
        public async ValueTask<EncounterAdvanceOutcome> Handle(
            OpFrame<EndTurnOp> frame,
            OpHandlerContext context
        )
        {
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Turn.Encounter
            );
            if (
                encounter.Phase != EncounterPhase.Active
                || !encounter.CurrentTurn.HasValue
                || encounter.CurrentTurn.Value != frame.Op.Turn
            )
                throw new InvalidOperationException("The turn identity or actor is stale.");
            EncounterHandlerResults.Require(
                await context.Dispatch(new TurnEndingOp(frame.Op.Turn)),
                "turn-end hook"
            );
            EncounterHandlerResults.Require(
                await context.Dispatch(new ResetMovementBudgetOp(frame.Op.Turn.Actor)),
                "turn-end movement reset"
            );
            EncounterHandlerResults.Require(
                await context.Dispatch(new CommitTurnEndOp(frame.Op.Turn)),
                "turn end"
            );
            return EncounterHandlerResults.Require(
                await context.Dispatch(new AdvanceEncounterOp(frame.Op.Turn.Encounter)),
                "turn advance"
            );
        }
    }

    internal sealed class SuspendEncounterHandler
        : IOpHandler<SuspendEncounterOp, EncounterSuspensionOutcome>
    {
        public async ValueTask<EncounterSuspensionOutcome> Handle(
            OpFrame<SuspendEncounterOp> frame,
            OpHandlerContext context
        )
        {
            await ExpireEncounterOwnedEffects(frame.Op.Encounter, context);
            return EncounterHandlerResults.Require(
                await context.Dispatch(new CommitEncounterSuspendOp(frame.Op.Encounter)),
                "encounter suspension"
            );
        }

        // Counted durations use initiative boundaries, so ending or suspending their owning
        // encounter permanently retires that clock. Dungeon resume creates a fresh encounter;
        // retaining the old timing would leave an enabled binding that can never advance.
        internal static async ValueTask ExpireEncounterOwnedEffects(
            EncounterId encounter,
            OpHandlerContext context
        )
        {
            ActiveEffectTimingState[] timings = context
                .Snapshot.ActiveEffectTimings.Where(pair => pair.Value.Encounter == encounter)
                .Select(pair => pair.Value)
                .OrderBy(value => value.CreationOrder)
                .ThenBy(value => value.Effect.Value, StringComparer.Ordinal)
                .ToArray();
            foreach (ActiveEffectTimingState timing in timings)
            {
                if (
                    !context.Snapshot.ActiveEffects.TryGet(
                        timing.Effect,
                        out ActiveEffectInstance effect
                    )
                    || effect.Status != ActiveEffectStatus.Active
                )
                    continue;
                EncounterHandlerResults.Require(
                    await context.Dispatch(
                        new ExpireActiveEffectOp(
                            effect.Id,
                            timing.Binding,
                            effect.EffectStateVersion,
                            effect.Source
                        )
                    ),
                    "encounter effect expiration"
                );
            }
        }
    }

    internal sealed class EndEncounterHandler : IOpHandler<EndEncounterOp, EncounterEndOutcome>
    {
        public async ValueTask<EncounterEndOutcome> Handle(
            OpFrame<EndEncounterOp> frame,
            OpHandlerContext context
        )
        {
            await SuspendEncounterHandler.ExpireEncounterOwnedEffects(frame.Op.Encounter, context);
            return EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitEncounterEndOp(frame.Op.Encounter, frame.Op.Outcome)
                ),
                "encounter end"
            );
        }
    }

    internal sealed class EncounterEndValidationMiddleware
        : IOpMiddleware<EndEncounterOp, EncounterEndOutcome>
    {
        public ValueTask<OpResult<EncounterEndOutcome>> Invoke(
            OpFrame<EndEncounterOp> frame,
            OpMiddlewareContext context,
            OpNext<EncounterEndOutcome> next
        )
        {
            if (
                !EncounterEndValidation.TryValidate(
                    context.Snapshot,
                    frame.Op.Encounter,
                    frame.Op.Outcome,
                    out _,
                    out _,
                    out string rejection
                )
            )
                return new ValueTask<OpResult<EncounterEndOutcome>>(
                    OpResult<EncounterEndOutcome>.Invalid(rejection)
                );
            return next();
        }
    }

    internal sealed class EvaluateEncounterOutcomeHandler
        : IOpHandler<EvaluateEncounterOutcomeOp, EncounterEvaluationOutcome>
    {
        public async ValueTask<EncounterEvaluationOutcome> Handle(
            OpFrame<EvaluateEncounterOutcomeOp> frame,
            OpHandlerContext context
        )
        {
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            if (encounter.Phase != EncounterPhase.Active)
                return new EncounterEvaluationOutcome(encounter);
            if (
                encounter.CurrentTurn.HasValue
                && !EncounterEndValidation.IsLiving(
                    context.Snapshot,
                    encounter.CurrentTurn.Value.Actor
                )
            )
            {
                TurnIdentity defeated = encounter.CurrentTurn.Value;
                EncounterAdvanceOutcome advanced = EncounterHandlerResults.Require(
                    await context.Dispatch(new EndTurnOp(defeated)),
                    "defeated turn end"
                );
                return new EncounterEvaluationOutcome(advanced.State);
            }
            EncounterOutcome? outcome = EncounterRuleRuntime.Evaluate(context.Snapshot, encounter);
            if (outcome.HasValue)
            {
                EncounterEndOutcome ended = EncounterHandlerResults.Require(
                    await context.Dispatch(new EndEncounterOp(encounter.Id, outcome.Value)),
                    "evaluated encounter end"
                );
                return new EncounterEvaluationOutcome(ended.State);
            }
            if (!encounter.CurrentTurn.HasValue && encounter.Cursor >= 0)
            {
                EncounterAdvanceOutcome advanced = EncounterHandlerResults.Require(
                    await context.Dispatch(new AdvanceEncounterOp(encounter.Id)),
                    "unstarted boundary advance"
                );
                return new EncounterEvaluationOutcome(advanced.State);
            }
            return new EncounterEvaluationOutcome(encounter);
        }
    }

    internal sealed class TurnStartingHandler : IOpHandler<TurnStartingOp, TurnStartContribution>
    {
        private readonly IReadOnlyList<IEncounterTurnStartAdapter> adapters;

        public TurnStartingHandler(IEnumerable<IEncounterTurnStartAdapter> adapters) =>
            this.adapters = Array.AsReadOnly(adapters.ToArray());

        public async ValueTask<TurnStartContribution> Handle(
            OpFrame<TurnStartingOp> frame,
            OpHandlerContext context
        )
        {
            TurnStartContribution contribution = TurnStartContribution.Standard;
            EncounterTurnStartContext adapterContext = new EncounterTurnStartContext(
                frame.Op.Encounter,
                frame.Op.Actor,
                context
            );
            foreach (IEncounterTurnStartAdapter adapter in adapters)
            {
                contribution = await adapter.Apply(adapterContext, contribution);
                if (!EncounterEndValidation.IsLiving(context.Snapshot, frame.Op.Actor))
                    break;
            }
            return contribution;
        }
    }

    internal sealed class TurnEndingHandler : IOpHandler<TurnEndingOp, TurnEndContribution>
    {
        public ValueTask<TurnEndContribution> Handle(
            OpFrame<TurnEndingOp> frame,
            OpHandlerContext context
        ) => new ValueTask<TurnEndContribution>(TurnEndContribution.Complete);
    }

    internal sealed class SpendLegacyActionsHandler
        : IOpHandler<SpendLegacyActionsOp, LegacyActionSpendOutcome>
    {
        public async ValueTask<LegacyActionSpendOutcome> Handle(
            OpFrame<SpendLegacyActionsOp> frame,
            OpHandlerContext context
        ) =>
            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitLegacyActionsOp(
                        frame.Op.Actor,
                        frame.Op.Amount,
                        frame.Op.RequiredLivingTargets
                    )
                ),
                "legacy action spend"
            );
    }

    internal sealed class IncrementLegacyMapHandler
        : IOpHandler<IncrementLegacyMapOp, LegacyMapOutcome>
    {
        public async ValueTask<LegacyMapOutcome> Handle(
            OpFrame<IncrementLegacyMapOp> frame,
            OpHandlerContext context
        ) =>
            EncounterHandlerResults.Require(
                await context.Dispatch(new CommitLegacyMapOp(frame.Op.Actor)),
                "legacy MAP increment"
            );
    }

    internal sealed class EncounterOutcomeListener
        : IRuleFactBatchListener<CreatureReducedToZeroFact>
    {
        public async ValueTask OnFactsCommitted(
            CommittedFactBatch<CreatureReducedToZeroFact> batch,
            FactContext context
        )
        {
            // Observation runs after every Reaction listener for these Facts. Commit defeat only
            // after those reactions have had their chance to restore each creature above zero.
            foreach (CreatureReducedToZeroFact fact in batch.Facts)
            {
                if (
                    !context.Snapshot.Health.TryGet(fact.Creature, out HealthState health)
                    || health.Current > 0
                )
                    continue;
                EncounterHandlerResults.Require(
                    await context.Dispatch(new FinalizeCreatureDefeatOp(fact.Creature)),
                    "creature defeat finalization"
                );
            }
            EncounterState encounter = context
                .Snapshot.Encounters.FirstOrDefault(pair =>
                    pair.Value.Phase == EncounterPhase.Active
                )
                .Value;
            if (encounter == null)
                return;
            EncounterHandlerResults.Require(
                await context.Dispatch(new EvaluateEncounterOutcomeOp(encounter.Id)),
                "encounter outcome evaluation"
            );
        }
    }
}
