using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class EncounterEndValidation
    {
        internal const string OutcomeMismatch =
            "The requested outcome does not match authoritative health and teams.";

        internal static bool IsLiving(RulesSnapshot snapshot, CreatureId creature) =>
            snapshot.Health.TryGet(creature, out HealthState health) && health.IsLiving;

        internal static bool IsLiving(RulesStateDraft state, CreatureId creature) =>
            state.Health.TryGet(creature, out HealthState health) && health.IsLiving;

        internal static bool TryValidate(
            RulesSnapshot snapshot,
            EncounterId id,
            EncounterOutcome requested,
            out EncounterState encounter,
            out EncounterOutcome actual,
            out string rejection
        )
        {
            bool found = snapshot.Encounters.TryGet(id, out encounter);
            return TryValidate(
                found,
                encounter,
                id,
                requested,
                creature => IsLiving(snapshot, creature),
                out actual,
                out rejection
            );
        }

        internal static bool TryValidate(
            RulesStateDraft state,
            EncounterId id,
            EncounterOutcome requested,
            out EncounterState encounter,
            out EncounterOutcome actual,
            out string rejection
        )
        {
            bool found = state.Encounters.TryGet(id, out encounter);
            return TryValidate(
                found,
                encounter,
                id,
                requested,
                creature => IsLiving(state, creature),
                out actual,
                out rejection
            );
        }

        internal static EncounterOutcome? Evaluate(
            EncounterState encounter,
            Func<CreatureId, bool> isLiving
        )
        {
            bool protagonistLives = encounter.Roster.Any(entry =>
                entry.Team == encounter.ProtagonistTeam && isLiving(entry.Creature)
            );
            if (!protagonistLives)
                return EncounterOutcome.PlayerDefeat;
            bool oppositionLives = encounter.Roster.Any(entry =>
                entry.Team != encounter.ProtagonistTeam && isLiving(entry.Creature)
            );
            return oppositionLives ? (EncounterOutcome?)null : EncounterOutcome.PlayerVictory;
        }

        private static bool TryValidate(
            bool found,
            EncounterState encounter,
            EncounterId id,
            EncounterOutcome requested,
            Func<CreatureId, bool> isLiving,
            out EncounterOutcome actual,
            out string rejection
        )
        {
            actual = default;
            if (!found)
            {
                rejection = $"Encounter {id.Value} is unknown.";
                return false;
            }
            if (encounter.Phase != EncounterPhase.Active)
            {
                rejection = $"Encounter {id.Value} is not active.";
                return false;
            }
            EncounterOutcome? evaluated = Evaluate(encounter, isLiving);
            if (!evaluated.HasValue || evaluated.Value != requested)
            {
                rejection = OutcomeMismatch;
                return false;
            }
            actual = evaluated.Value;
            rejection = string.Empty;
            return true;
        }
    }

    internal static class EncounterReduction
    {
        public static bool TryGetActive(
            RulesStateDraft state,
            EncounterId id,
            out EncounterState encounter,
            out string rejection
        )
        {
            if (!state.Encounters.TryGet(id, out encounter))
            {
                rejection = $"Encounter {id.Value} is unknown.";
                return false;
            }
            if (encounter.Phase != EncounterPhase.Active)
            {
                rejection = $"Encounter {id.Value} is not active.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        public static bool IsLiving(RulesStateDraft state, CreatureId creature) =>
            EncounterEndValidation.IsLiving(state, creature);

        public static EncounterOutcome? Evaluate(RulesStateDraft state, EncounterState encounter) =>
            EncounterEndValidation.Evaluate(encounter, creature => IsLiving(state, creature));
    }

    internal sealed class CommitEncounterStartReducer
        : IOpReducer<CommitEncounterStartOp, EncounterStartOutcome>
    {
        public ReductionResult<EncounterStartOutcome> Reduce(
            ReductionContext<CommitEncounterStartOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (state.Encounters.Any(pair => pair.Value.Phase == EncounterPhase.Active))
                return ReductionResult<EncounterStartOutcome>.Reject(
                    "A rules store already has an active encounter."
                );
            if (state.Encounters.Contains(context.Op.Encounter))
                return ReductionResult<EncounterStartOutcome>.Reject(
                    $"Encounter {context.Op.Encounter.Value} already exists."
                );
            if (!context.Op.Roster.Any(entry => entry.Team == context.Op.ProtagonistTeam))
                return ReductionResult<EncounterStartOutcome>.Reject(
                    "An encounter roster must contain its designated protagonist team."
                );
            if (
                context.Op.Roster.Select(entry => entry.Creature).Distinct().Count()
                != context.Op.Roster.Count
            )
                return ReductionResult<EncounterStartOutcome>.Reject(
                    "An encounter roster contains a duplicate creature."
                );
            foreach (InitiativeEntry entry in context.Op.Roster)
            {
                if (!state.Health.Contains(entry.Creature))
                    return ReductionResult<EncounterStartOutcome>.Reject(
                        $"Creature {entry.Creature.Value} has no authoritative health state."
                    );
            }
            EncounterState encounter = new EncounterState(
                context.Op.Encounter,
                EncounterPhase.Active,
                context.Op.ProtagonistTeam,
                RoundNumber.First,
                context.Op.Roster,
                -1,
                null,
                1,
                null
            );
            state.Encounters.Set(encounter.Id, encounter);
            state.RuleBindings.Set(
                EncounterRuleRuntime.OutcomeBindingId(encounter.Id),
                new ActiveRuleBinding(
                    EncounterRuleRuntime.OutcomeBindingId(encounter.Id),
                    EncounterRuleRuntime.OutcomeDefinitionId,
                    encounter.Roster[0].Creature,
                    null,
                    EncounterRuleRuntime.Source,
                    0
                )
            );
            foreach (InitiativeEntry entry in encounter.Roster)
            {
                state.ActionEconomy.Set(entry.Creature, new ActionEconomyState(0, false));
                state.MultipleAttackPenalty.Set(entry.Creature, new MultipleAttackPenaltyState(0));
            }
            facts.Stage(new EncounterStartedFact(encounter));
            return ReductionResult<EncounterStartOutcome>.Accept(
                new EncounterStartOutcome(encounter)
            );
        }
    }

    internal sealed class CommitEncounterJoinReducer
        : IOpReducer<CommitEncounterJoinOp, EncounterJoinOutcome>
    {
        public ReductionResult<EncounterJoinOutcome> Reduce(
            ReductionContext<CommitEncounterJoinOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !EncounterReduction.TryGetActive(
                    state,
                    context.Op.Encounter,
                    out EncounterState encounter,
                    out string rejection
                )
            )
                return ReductionResult<EncounterJoinOutcome>.Reject(rejection);
            if (!encounter.CurrentTurn.HasValue)
                return ReductionResult<EncounterJoinOutcome>.Reject(
                    "Reinforcements require an active turn boundary."
                );
            HashSet<CreatureId> existing = new HashSet<CreatureId>(
                encounter.Roster.Select(entry => entry.Creature)
            );
            foreach (InitiativeEntry entry in context.Op.Additions)
            {
                if (!existing.Add(entry.Creature))
                    return ReductionResult<EncounterJoinOutcome>.Reject(
                        $"Creature {entry.Creature.Value} is already in the roster."
                    );
                if (!context.Op.InitialHealth.ContainsKey(entry.Creature))
                    return ReductionResult<EncounterJoinOutcome>.Reject(
                        $"Creature {entry.Creature.Value} has no captured initial health."
                    );
            }
            foreach (InitiativeEntry entry in context.Op.Additions)
                if (!state.Health.Contains(entry.Creature))
                    state.Health.Set(entry.Creature, context.Op.InitialHealth[entry.Creature]);
            InitiativeEntry[] roster = encounter
                .Roster.Concat(context.Op.Additions)
                .OrderByDescending(entry => entry.Total)
                .ThenBy(entry => entry.RegistrationOrder)
                .ToArray();
            int cursor = Array.FindIndex(
                roster,
                entry => entry.Creature == encounter.CurrentTurn.Value.Actor
            );
            EncounterState updated = encounter.Replace(
                roster: roster,
                cursor: cursor,
                currentTurn: encounter.CurrentTurn.Value
            );
            state.Encounters.Set(updated.Id, updated);
            foreach (InitiativeEntry entry in context.Op.Additions)
            {
                state.ActionEconomy.Set(entry.Creature, new ActionEconomyState(0, false));
                state.MultipleAttackPenalty.Set(entry.Creature, new MultipleAttackPenaltyState(0));
            }
            facts.Stage(new EncounterJoinedFact(updated));
            return ReductionResult<EncounterJoinOutcome>.Accept(new EncounterJoinOutcome(updated));
        }
    }

    internal sealed class CommitInitiativeBoundaryReducer
        : IOpReducer<CommitInitiativeBoundaryOp, InitiativeBoundaryOutcome>
    {
        public ReductionResult<InitiativeBoundaryOutcome> Reduce(
            ReductionContext<CommitInitiativeBoundaryOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !EncounterReduction.TryGetActive(
                    state,
                    context.Op.Encounter,
                    out EncounterState encounter,
                    out string rejection
                )
            )
                return ReductionResult<InitiativeBoundaryOutcome>.Reject(rejection);
            if (encounter.CurrentTurn.HasValue)
                return ReductionResult<InitiativeBoundaryOutcome>.Reject(
                    "The current turn must end before initiative advances."
                );
            int cursor = encounter.Cursor + 1;
            RoundNumber round = encounter.Round;
            if (cursor >= encounter.Roster.Count)
            {
                cursor = 0;
                round = round.Next();
            }
            InitiativeEntry entry = encounter.Roster[cursor];
            EncounterState updated = encounter.Replace(
                round: round,
                cursor: cursor,
                clearCurrentTurn: true
            );
            state.Encounters.Set(updated.Id, updated);
            List<ActiveEffectTimingState> due = new List<ActiveEffectTimingState>();
            foreach (
                KeyValuePair<
                    ActiveEffectId,
                    ActiveEffectTimingState
                > pair in state.ActiveEffectTimings.ToArray()
            )
            {
                ActiveEffectTimingState timing = pair.Value;
                if (
                    timing.Encounter != encounter.Id
                    || timing.ExpiresWithEncounter
                    || timing.SourceCreature != entry.Creature
                    || timing.RemainingBoundaries <= 0
                )
                    continue;
                int remaining = timing.RemainingBoundaries - 1;
                ActiveEffectTimingState changed = timing.WithRemaining(remaining);
                state.ActiveEffectTimings.Set(pair.Key, changed);
                if (remaining == 0)
                    due.Add(changed);
            }
            due.Sort(
                (left, right) =>
                {
                    int order = left.CreationOrder.CompareTo(right.CreationOrder);
                    return order != 0
                        ? order
                        : string.Compare(
                            left.Effect.Value,
                            right.Effect.Value,
                            StringComparison.Ordinal
                        );
                }
            );
            facts.Stage(new InitiativeBoundaryReachedFact(encounter.Id, round, entry.Creature));
            return ReductionResult<InitiativeBoundaryOutcome>.Accept(
                new InitiativeBoundaryOutcome(updated, entry, Array.AsReadOnly(due.ToArray()))
            );
        }
    }

    internal sealed class CommitTurnBeginReducer
        : IOpReducer<CommitTurnBeginOp, EncounterAdvanceOutcome>
    {
        public ReductionResult<EncounterAdvanceOutcome> Reduce(
            ReductionContext<CommitTurnBeginOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !EncounterReduction.TryGetActive(
                    state,
                    context.Op.Encounter,
                    out EncounterState encounter,
                    out string rejection
                )
            )
                return ReductionResult<EncounterAdvanceOutcome>.Reject(rejection);
            if (
                encounter.CurrentTurn.HasValue
                || encounter.Roster[encounter.Cursor].Creature != context.Op.Actor
            )
                return ReductionResult<EncounterAdvanceOutcome>.Reject(
                    "The requested actor is not the reached initiative slot."
                );
            if (!EncounterReduction.IsLiving(state, context.Op.Actor))
                return ReductionResult<EncounterAdvanceOutcome>.Reject(
                    "A zero-HP creature cannot begin a turn."
                );
            TurnIdentity turn = new TurnIdentity(
                encounter.Id,
                new TurnId(encounter.NextTurnSequence),
                context.Op.Actor,
                encounter.Round,
                encounter.Cursor
            );
            EncounterState updated = encounter.Replace(
                currentTurn: turn,
                nextTurnSequence: checked(encounter.NextTurnSequence + 1)
            );
            state.Encounters.Set(updated.Id, updated);
            state.ActionEconomy.Set(
                context.Op.Actor,
                new ActionEconomyState(context.Op.Actions, true)
            );
            state.MultipleAttackPenalty.Set(context.Op.Actor, new MultipleAttackPenaltyState(0));
            facts.Stage(new TurnBeganFact(turn));
            return ReductionResult<EncounterAdvanceOutcome>.Accept(
                new EncounterAdvanceOutcome(updated)
            );
        }
    }

    internal sealed class CommitTurnEndReducer
        : IOpReducer<CommitTurnEndOp, EncounterAdvanceOutcome>
    {
        public ReductionResult<EncounterAdvanceOutcome> Reduce(
            ReductionContext<CommitTurnEndOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            TurnIdentity requested = context.Op.Turn;
            if (
                !EncounterReduction.TryGetActive(
                    state,
                    requested.Encounter,
                    out EncounterState encounter,
                    out string rejection
                )
            )
                return ReductionResult<EncounterAdvanceOutcome>.Reject(rejection);
            if (!encounter.CurrentTurn.HasValue)
                return ReductionResult<EncounterAdvanceOutcome>.Reject(
                    "The encounter has no active turn."
                );
            if (encounter.CurrentTurn.Value != requested)
                return ReductionResult<EncounterAdvanceOutcome>.Reject(
                    "The turn identity or actor is stale."
                );
            ActionEconomyState economy = state.ActionEconomy.TryGet(
                requested.Actor,
                out ActionEconomyState current
            )
                ? current
                : new ActionEconomyState(0, false);
            state.ActionEconomy.Set(
                requested.Actor,
                new ActionEconomyState(0, economy.ReactionAvailable)
            );
            state.MultipleAttackPenalty.Set(requested.Actor, new MultipleAttackPenaltyState(0));
            EncounterState updated = encounter.Replace(clearCurrentTurn: true);
            state.Encounters.Set(updated.Id, updated);
            facts.Stage(new TurnEndedFact(requested));
            return ReductionResult<EncounterAdvanceOutcome>.Accept(
                new EncounterAdvanceOutcome(updated)
            );
        }
    }

    internal sealed class CommitEncounterSuspendReducer
        : IOpReducer<CommitEncounterSuspendOp, EncounterSuspensionOutcome>
    {
        public ReductionResult<EncounterSuspensionOutcome> Reduce(
            ReductionContext<CommitEncounterSuspendOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !EncounterReduction.TryGetActive(
                    state,
                    context.Op.Encounter,
                    out EncounterState encounter,
                    out string rejection
                )
            )
                return ReductionResult<EncounterSuspensionOutcome>.Reject(rejection);
            ClearTurnResources(state, encounter);
            EncounterState updated = encounter.Replace(
                phase: EncounterPhase.Suspended,
                clearCurrentTurn: true
            );
            state.Encounters.Set(updated.Id, updated);
            state.RuleBindings.Remove(EncounterRuleRuntime.OutcomeBindingId(updated.Id));
            facts.Stage(new EncounterSuspendedFact(updated.Id));
            return ReductionResult<EncounterSuspensionOutcome>.Accept(
                new EncounterSuspensionOutcome(updated)
            );
        }

        internal static void ClearTurnResources(RulesStateDraft state, EncounterState encounter)
        {
            foreach (InitiativeEntry entry in encounter.Roster)
            {
                state.ActionEconomy.Set(entry.Creature, new ActionEconomyState(0, false));
                state.MultipleAttackPenalty.Set(entry.Creature, new MultipleAttackPenaltyState(0));
                state.MovementBudgets.Remove(entry.Creature);
            }
        }
    }

    internal sealed class CommitEncounterEndReducer
        : IOpReducer<CommitEncounterEndOp, EncounterEndOutcome>
    {
        public ReductionResult<EncounterEndOutcome> Reduce(
            ReductionContext<CommitEncounterEndOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !EncounterEndValidation.TryValidate(
                    state,
                    context.Op.Encounter,
                    context.Op.Outcome,
                    out EncounterState encounter,
                    out EncounterOutcome actual,
                    out string rejection
                )
            )
                return ReductionResult<EncounterEndOutcome>.Reject(rejection);
            CommitEncounterSuspendReducer.ClearTurnResources(state, encounter);
            EncounterState updated = encounter.Replace(
                phase: EncounterPhase.Ended,
                clearCurrentTurn: true,
                outcome: actual
            );
            state.Encounters.Set(updated.Id, updated);
            state.RuleBindings.Remove(EncounterRuleRuntime.OutcomeBindingId(updated.Id));
            facts.Stage(new EncounterEndedFact(updated.Id, actual));
            return ReductionResult<EncounterEndOutcome>.Accept(new EncounterEndOutcome(updated));
        }
    }

    internal sealed class SpendLegacyActionsReducer
        : IOpReducer<CommitLegacyActionsOp, LegacyActionSpendOutcome>
    {
        public ReductionResult<LegacyActionSpendOutcome> Reduce(
            ReductionContext<CommitLegacyActionsOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            bool ownsActiveTurn = state.Encounters.Any(pair =>
                pair.Value.Phase == EncounterPhase.Active
                && pair.Value.CurrentTurn.HasValue
                && pair.Value.CurrentTurn.Value.Actor == context.Op.Actor
            );
            if (!ownsActiveTurn)
                return ReductionResult<LegacyActionSpendOutcome>.Reject(
                    "The actor does not own an active current turn."
                );
            if (
                !state.ActionEconomy.TryGet(context.Op.Actor, out ActionEconomyState economy)
                || economy.ActionsRemaining < context.Op.Amount
            )
                return ReductionResult<LegacyActionSpendOutcome>.Reject(
                    "The actor has insufficient authoritative actions."
                );
            if (context.Op.Amount == 0)
                return ReductionResult<LegacyActionSpendOutcome>.Accept(
                    new LegacyActionSpendOutcome(economy.ActionsRemaining)
                );
            int remaining = economy.ActionsRemaining - context.Op.Amount;
            state.ActionEconomy.Set(
                context.Op.Actor,
                new ActionEconomyState(remaining, economy.ReactionAvailable)
            );
            facts.Stage(new LegacyActionsSpentFact(context.Op.Actor, context.Op.Amount, remaining));
            return ReductionResult<LegacyActionSpendOutcome>.Accept(
                new LegacyActionSpendOutcome(remaining)
            );
        }
    }

    internal sealed class IncrementLegacyMapReducer
        : IOpReducer<CommitLegacyMapOp, LegacyMapOutcome>
    {
        public ReductionResult<LegacyMapOutcome> Reduce(
            ReductionContext<CommitLegacyMapOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            bool ownsActiveTurn = state.Encounters.Any(pair =>
                pair.Value.Phase == EncounterPhase.Active
                && pair.Value.CurrentTurn.HasValue
                && pair.Value.CurrentTurn.Value.Actor == context.Op.Actor
            );
            if (!ownsActiveTurn)
                return ReductionResult<LegacyMapOutcome>.Reject(
                    "The actor does not own an active current turn."
                );

            int count = state.MultipleAttackPenalty.TryGet(
                context.Op.Actor,
                out MultipleAttackPenaltyState current
            )
                ? checked(current.AttackCount + 1)
                : 1;
            state.MultipleAttackPenalty.Set(
                context.Op.Actor,
                new MultipleAttackPenaltyState(count)
            );
            facts.Stage(new LegacyMapIncrementedFact(context.Op.Actor, count));
            return ReductionResult<LegacyMapOutcome>.Accept(new LegacyMapOutcome(count));
        }
    }
}
