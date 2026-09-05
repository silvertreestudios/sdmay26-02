using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class EncounterEndValidation
    {
        internal const string OutcomeMismatch =
            "The requested outcome does not match authoritative health and teams.";

        internal static bool IsLiving(RulesSnapshot snapshot, CreatureId creature)
        {
            if (!snapshot.Health.TryGet(creature, out HealthState health))
                throw new InvalidOperationException(
                    $"Encounter participant {creature.Value} has no authoritative health state."
                );
            return health.Current > 0;
        }

        internal static bool IsLiving(RulesStateDraft state, CreatureId creature)
        {
            if (!state.Health.TryGet(creature, out HealthState health))
                throw new InvalidOperationException(
                    $"Encounter participant {creature.Value} has no authoritative health state."
                );
            return health.Current > 0;
        }

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
            if (encounter.ConclusionPolicy == EncounterConclusionPolicy.ProtagonistDefeatOnly)
                return null;
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
                if (
                    state.Creatures.TryGet(entry.Creature, out CreatureState creature)
                    && creature.Player != entry.Team
                )
                    return ReductionResult<EncounterStartOutcome>.Reject(
                        $"Creature {entry.Creature.Value} has a team that conflicts with its authoritative creature state."
                    );
            }
            BindingId outcomeBindingId = EncounterRuleRuntime.OutcomeBindingId(
                context.Op.Encounter
            );
            if (state.RuleBindings.Contains(outcomeBindingId))
                return ReductionResult<EncounterStartOutcome>.Reject(
                    $"Rule binding {outcomeBindingId.Value} is already registered."
                );
            EncounterState encounter = new EncounterState(
                context.Op.Encounter,
                EncounterPhase.Active,
                context.Op.ProtagonistTeam,
                context.Op.ConclusionPolicy,
                RoundNumber.First,
                context.Op.Roster,
                -1,
                null,
                1,
                null
            );
            HashSet<CreatureId> rosterCreatures = new HashSet<CreatureId>(
                context.Op.Roster.Select(entry => entry.Creature)
            );
            foreach (
                ActiveEffectInstance effect in state
                    .ActiveEffects.Select(pair => pair.Value)
                    .Where(effect =>
                        effect.Duration.Kind != EffectDurationKind.Indefinite
                        && rosterCreatures.Contains(effect.SourceCreature)
                    )
                    .OrderBy(effect => effect.Id.Value, StringComparer.Ordinal)
            )
            {
                if (state.ActiveEffectTimings.Contains(effect.Id))
                    return ReductionResult<EncounterStartOutcome>.Reject(
                        $"Active effect {effect.Id.Value} already has encounter timing."
                    );
                if (
                    !ActiveEffectReduction.TryGetSingleAssociatedBinding(
                        state.RuleBindings.Select(pair => pair.Value),
                        effect,
                        true,
                        out _,
                        out string bindingRejection
                    )
                )
                    return ReductionResult<EncounterStartOutcome>.Reject(bindingRejection);
                state.ActiveEffectTimings.Set(
                    effect.Id,
                    ActiveEffectTimingState.ForEncounter(effect, encounter)
                );
            }
            state.Encounters.Set(encounter.Id, encounter);
            state.RuleBindings.Set(
                outcomeBindingId,
                new ActiveRuleBinding(
                    outcomeBindingId,
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
            HashSet<SpellSlotPoolId> incomingSpellSlots = new HashSet<SpellSlotPoolId>();
            HashSet<BindingId> incomingRuleBindings = new HashSet<BindingId>();
            if (context.Op.Registrations.Count != context.Op.Additions.Count)
                return ReductionResult<EncounterJoinOutcome>.Reject(
                    "Every reinforcement must have exactly one captured rules registration."
                );
            foreach (InitiativeEntry entry in context.Op.Additions)
            {
                if (!existing.Add(entry.Creature))
                    return ReductionResult<EncounterJoinOutcome>.Reject(
                        $"Creature {entry.Creature.Value} is already in the roster."
                    );
                if (!context.Op.Registrations.ContainsKey(entry.Creature))
                    return ReductionResult<EncounterJoinOutcome>.Reject(
                        $"Creature {entry.Creature.Value} has no captured rules registration."
                    );
                CombatantRulesState registration = context.Op.Registrations[entry.Creature];
                if (registration.Creature.Id != entry.Creature)
                    return ReductionResult<EncounterJoinOutcome>.Reject(
                        $"Creature {entry.Creature.Value} has a mismatched rules registration."
                    );
                if (
                    state.Creatures.Contains(entry.Creature)
                    || state.Health.Contains(entry.Creature)
                    || state.Positions.Contains(entry.Creature)
                    || state.LandSpeeds.Contains(entry.Creature)
                    || state.ActionEconomy.Contains(entry.Creature)
                    || state.MultipleAttackPenalty.Contains(entry.Creature)
                )
                    return ReductionResult<EncounterJoinOutcome>.Reject(
                        $"Creature {entry.Creature.Value} collides with existing registration state."
                    );
                foreach (SpellSlotState slot in registration.SpellSlots)
                    if (state.SpellSlots.Contains(slot.Id) || !incomingSpellSlots.Add(slot.Id))
                        return ReductionResult<EncounterJoinOutcome>.Reject(
                            $"Spell-slot pool {slot.Id.Value} is already registered."
                        );
                foreach (ActiveRuleBinding binding in registration.RuleBindings)
                    if (
                        state.RuleBindings.Contains(binding.Id)
                        || !incomingRuleBindings.Add(binding.Id)
                    )
                        return ReductionResult<EncounterJoinOutcome>.Reject(
                            $"Rule binding {binding.Id.Value} is already registered."
                        );
            }
            foreach (InitiativeEntry entry in context.Op.Additions)
            {
                CombatantRulesState registration = context.Op.Registrations[entry.Creature];
                state.Creatures.Set(entry.Creature, registration.Creature);
                state.Health.Set(entry.Creature, registration.Health);
                state.Positions.Set(entry.Creature, registration.Position);
                state.LandSpeeds.Set(entry.Creature, registration.LandSpeed);
                foreach (SpellSlotState slot in registration.SpellSlots)
                    state.SpellSlots.Set(slot.Id, slot);
                foreach (ActiveRuleBinding binding in registration.RuleBindings)
                    state.RuleBindings.Set(binding.Id, binding);
            }
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

    internal sealed class CommitInitiativeAssignmentsReducer
        : IOpReducer<CommitInitiativeAssignmentsOp, InitiativeAssignmentsOutcome>
    {
        public ReductionResult<InitiativeAssignmentsOutcome> Reduce(
            ReductionContext<CommitInitiativeAssignmentsOp> context,
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
                return ReductionResult<InitiativeAssignmentsOutcome>.Reject(rejection);
            if (context.Op.Entries == null || context.Op.Entries.Count == 0)
                return ReductionResult<InitiativeAssignmentsOutcome>.Reject(
                    "At least one initiative assignment is required."
                );

            foreach (InitiativeEntry entry in context.Op.Entries)
            {
                InitiativeEntry committed = encounter.Roster.SingleOrDefault(candidate =>
                    candidate.Creature == entry.Creature
                );
                if (committed == null || !committed.Equals(entry))
                    return ReductionResult<InitiativeAssignmentsOutcome>.Reject(
                        $"Creature {entry.Creature.Value} has no matching committed initiative slot."
                    );
            }
            foreach (InitiativeEntry entry in context.Op.Entries)
                facts.Stage(new InitiativeAssignedFact(encounter.Id, entry));
            return ReductionResult<InitiativeAssignmentsOutcome>.Accept(
                new InitiativeAssignmentsOutcome(context.Op.Entries.Count)
            );
        }
    }

    internal sealed class CommitInitiativeBoundaryReducer
        : IOpReducer<CommitInitiativeBoundaryOp, EncounterAdvanceOutcome>
    {
        public ReductionResult<EncounterAdvanceOutcome> Reduce(
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
                return ReductionResult<EncounterAdvanceOutcome>.Reject(rejection);
            if (encounter.CurrentTurn.HasValue)
                return ReductionResult<EncounterAdvanceOutcome>.Reject(
                    "The current turn must settle before initiative advances."
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
            List<(ActiveEffectInstance Effect, ActiveRuleBinding Binding)> due =
                new List<(ActiveEffectInstance, ActiveRuleBinding)>();
            foreach (
                KeyValuePair<
                    ActiveEffectId,
                    ActiveEffectTimingState
                > pair in state.ActiveEffectTimings.ToArray()
            )
            {
                ActiveEffectTimingState timing = pair.Value;
                if (timing.Encounter != encounter.Id)
                    continue;
                if (!state.ActiveEffects.TryGet(pair.Key, out ActiveEffectInstance effect))
                    return ReductionResult<EncounterAdvanceOutcome>.Reject(
                        $"Active effect {pair.Key.Value} is unknown."
                    );
                if (
                    effect.Duration.Kind == EffectDurationKind.Encounter
                    || effect.Duration.Kind == EffectDurationKind.Indefinite
                    || effect.SourceCreature != entry.Creature
                    || timing.RemainingBoundaries <= 0
                )
                    continue;
                int remaining = timing.RemainingBoundaries - 1;
                ActiveEffectTimingState changed = timing.WithRemaining(remaining);
                state.ActiveEffectTimings.Set(pair.Key, changed);
                if (remaining == 0)
                {
                    if (
                        !ActiveEffectReduction.TryGetSingleAssociatedBinding(
                            state.RuleBindings.Select(binding => binding.Value),
                            effect,
                            false,
                            out ActiveRuleBinding binding,
                            out string bindingRejection
                        )
                    )
                        return ReductionResult<EncounterAdvanceOutcome>.Reject(bindingRejection);
                    due.Add((effect, binding));
                }
            }
            due.Sort(
                (left, right) =>
                {
                    int order = left.Binding.CreationOrder.CompareTo(right.Binding.CreationOrder);
                    return order != 0
                        ? order
                        : string.Compare(
                            left.Effect.Id.Value,
                            right.Effect.Id.Value,
                            StringComparison.Ordinal
                        );
                }
            );
            foreach ((ActiveEffectInstance effect, ActiveRuleBinding binding) in due)
            {
                if (
                    !ActiveEffectReduction.TryRemove(
                        state,
                        effect.Id,
                        binding.Id,
                        effect.EffectStateVersion,
                        ActiveEffectRemovalReason.Expired,
                        facts,
                        out string removalRejection
                    )
                )
                    return ReductionResult<EncounterAdvanceOutcome>.Reject(removalRejection);
            }
            facts.Stage(
                new InitiativeBoundaryReachedFact(updated.Id, updated.Round, entry.Creature)
            );
            return ReductionResult<EncounterAdvanceOutcome>.Accept(
                new EncounterAdvanceOutcome(updated)
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
            if (!state.ActionEconomy.TryGet(requested.Actor, out ActionEconomyState economy))
                return ReductionResult<EncounterAdvanceOutcome>.Reject(
                    "The turn actor has no authoritative action-economy state."
                );
            if (!state.MultipleAttackPenalty.Contains(requested.Actor))
                return ReductionResult<EncounterAdvanceOutcome>.Reject(
                    "The turn actor has no authoritative multiple-attack-penalty state."
                );
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
            foreach (InitiativeEntry entry in encounter.Roster)
            {
                if (
                    state.Health.TryGet(entry.Creature, out HealthState health)
                    && health.Current == 0
                )
                    HealthDefeatState.Commit(state, entry.Creature, facts);
            }
            CommitEncounterSuspendReducer.ClearTurnResources(state, encounter);
            EncounterState updated = encounter.Replace(
                phase: EncounterPhase.Ended,
                clearCurrentTurn: true,
                outcome: actual
            );
            state.Encounters.Set(updated.Id, updated);
            state.RuleBindings.Remove(EncounterRuleRuntime.OutcomeBindingId(updated.Id));
            facts.Stage(new EncounterOutcomeCommittedFact(updated.Id, actual));
            return ReductionResult<EncounterEndOutcome>.Accept(new EncounterEndOutcome(updated));
        }
    }

    internal sealed class SpendEncounterActionsReducer
        : IOpReducer<CommitEncounterActionsOp, EncounterActionSpendOutcome>
    {
        public ReductionResult<EncounterActionSpendOutcome> Reduce(
            ReductionContext<CommitEncounterActionsOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            EncounterState encounter = state
                .Encounters.Where(pair =>
                    pair.Value.Phase == EncounterPhase.Active
                    && pair.Value.CurrentTurn.HasValue
                    && pair.Value.CurrentTurn.Value.Actor == context.Op.Actor
                )
                .Select(pair => pair.Value)
                .FirstOrDefault();
            if (encounter == null)
                return ReductionResult<EncounterActionSpendOutcome>.Reject(
                    "The actor does not own an active current turn."
                );
            if (
                context.Op.RequiredLivingTargets.Any(target =>
                    !encounter.Roster.Any(entry => entry.Creature == target)
                    || !EncounterReduction.IsLiving(state, target)
                )
            )
                return ReductionResult<EncounterActionSpendOutcome>.Reject(
                    "A selected action target is no longer a living participant in the actor's encounter."
                );
            if (
                !state.ActionEconomy.TryGet(context.Op.Actor, out ActionEconomyState economy)
                || economy.ActionsRemaining < context.Op.Amount
            )
                return ReductionResult<EncounterActionSpendOutcome>.Reject(
                    "The actor has insufficient authoritative actions."
                );
            if (context.Op.Amount == 0)
                return ReductionResult<EncounterActionSpendOutcome>.Accept(
                    new EncounterActionSpendOutcome(economy.ActionsRemaining)
                );
            int remaining = economy.ActionsRemaining - context.Op.Amount;
            state.ActionEconomy.Set(
                context.Op.Actor,
                new ActionEconomyState(remaining, economy.ReactionAvailable)
            );
            facts.Stage(
                new EncounterActionsSpentFact(context.Op.Actor, context.Op.Amount, remaining)
            );
            return ReductionResult<EncounterActionSpendOutcome>.Accept(
                new EncounterActionSpendOutcome(remaining)
            );
        }
    }
}
