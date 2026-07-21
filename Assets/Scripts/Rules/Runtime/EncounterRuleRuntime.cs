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

        /// <summary>Adds the encounter-owned Observation listener to a shared registry builder.</summary>
        public static RuleRegistryBuilder AddOutcomeRule(this RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder
                .Define(OutcomeDefinitionId)
                .FactListener(RuleLifecyclePhase.Observation, new EncounterOutcomeListener());
            return builder;
        }

        /// <summary>Registers encounter handlers and reducer-owned transitions on one dispatcher.</summary>
        public static RuleDispatcherBuilder UseEncounterRules(this RuleDispatcherBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
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
                    new TurnStartingHandler(),
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

        internal static EncounterOutcome? Evaluate(RulesSnapshot snapshot, EncounterState encounter)
        {
            bool protagonistLives = encounter.Roster.Any(entry =>
                entry.Team == encounter.ProtagonistTeam
                && snapshot.Health.TryGet(entry.Creature, out HealthState health)
                && health.Current > 0
            );
            if (!protagonistLives)
                return EncounterOutcome.PlayerDefeat;
            bool oppositionLives = encounter.Roster.Any(entry =>
                entry.Team != encounter.ProtagonistTeam
                && snapshot.Health.TryGet(entry.Creature, out HealthState health)
                && health.Current > 0
            );
            return oppositionLives ? (EncounterOutcome?)null : EncounterOutcome.PlayerVictory;
        }
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
            return EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitEncounterJoinOp(
                        frame.Op.Encounter,
                        Array.AsReadOnly(additions),
                        frame.Op.Participants.ToDictionary(
                            value => value.Participant.Creature,
                            value => value.InitialHealth
                        )
                    )
                ),
                "encounter join"
            );
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
                EncounterOutcome? outcome = EncounterRuleRuntime.Evaluate(context.Snapshot, latest);
                if (outcome.HasValue)
                {
                    EncounterEndOutcome ended = EncounterHandlerResults.Require(
                        await context.Dispatch(new EndEncounterOp(latest.Id, outcome.Value)),
                        "turn-start outcome"
                    );
                    return new EncounterAdvanceOutcome(ended.State);
                }
                if (
                    !context.Snapshot.Health.TryGet(boundary.Entry.Creature, out HealthState health)
                    || health.Current <= 0
                )
                    continue;
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
            await ExpireEncounterEffects(frame.Op.Encounter, context);
            return EncounterHandlerResults.Require(
                await context.Dispatch(new CommitEncounterSuspendOp(frame.Op.Encounter)),
                "encounter suspension"
            );
        }

        internal static async ValueTask ExpireEncounterEffects(
            EncounterId encounter,
            OpHandlerContext context
        )
        {
            ActiveEffectTimingState[] timings = context
                .Snapshot.ActiveEffectTimings.Where(pair =>
                    pair.Value.Encounter == encounter && pair.Value.ExpiresWithEncounter
                )
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
            await SuspendEncounterHandler.ExpireEncounterEffects(frame.Op.Encounter, context);
            return EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitEncounterEndOp(frame.Op.Encounter, frame.Op.Outcome)
                ),
                "encounter end"
            );
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
            EncounterOutcome? outcome = EncounterRuleRuntime.Evaluate(context.Snapshot, encounter);
            if (outcome.HasValue)
            {
                EncounterEndOutcome ended = EncounterHandlerResults.Require(
                    await context.Dispatch(new EndEncounterOp(encounter.Id, outcome.Value)),
                    "evaluated encounter end"
                );
                return new EncounterEvaluationOutcome(ended.State);
            }
            if (
                encounter.CurrentTurn.HasValue
                && (
                    !context.Snapshot.Health.TryGet(
                        encounter.CurrentTurn.Value.Actor,
                        out HealthState health
                    )
                    || health.Current <= 0
                )
            )
            {
                TurnIdentity defeated = encounter.CurrentTurn.Value;
                EncounterHandlerResults.Require(
                    await context.Dispatch(new CommitTurnEndOp(defeated)),
                    "defeated turn close"
                );
                EncounterAdvanceOutcome advanced = EncounterHandlerResults.Require(
                    await context.Dispatch(new AdvanceEncounterOp(encounter.Id)),
                    "defeated turn advance"
                );
                return new EncounterEvaluationOutcome(advanced.State);
            }
            return new EncounterEvaluationOutcome(encounter);
        }
    }

    internal sealed class TurnStartingHandler : IOpHandler<TurnStartingOp, TurnStartContribution>
    {
        public ValueTask<TurnStartContribution> Handle(
            OpFrame<TurnStartingOp> frame,
            OpHandlerContext context
        ) => new ValueTask<TurnStartContribution>(TurnStartContribution.Standard);
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
                await context.Dispatch(new CommitLegacyActionsOp(frame.Op.Actor, frame.Op.Amount)),
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

    internal sealed class EncounterOutcomeListener : IRuleFactListener<CreatureReducedToZeroFact>
    {
        public async ValueTask OnFactCommitted(CreatureReducedToZeroFact fact, FactContext context)
        {
            EncounterState encounter = context
                .Snapshot.Encounters.FirstOrDefault(pair =>
                    pair.Value.Phase == EncounterPhase.Active
                )
                .Value;
            if (encounter == null)
                return;
            await context.Dispatch(new EvaluateEncounterOutcomeOp(encounter.Id));
            if (
                context.Snapshot.Encounters.TryGet(encounter.Id, out EncounterState refreshed)
                && refreshed.Phase == EncounterPhase.Active
                && refreshed.CurrentTurn.HasValue
                && refreshed.CurrentTurn.Value.Actor == fact.Creature
                && context.Snapshot.Health.TryGet(fact.Creature, out HealthState health)
                && health.Current == 0
            )
                await context.Dispatch(new EndTurnOp(refreshed.CurrentTurn.Value));
        }
    }
}
