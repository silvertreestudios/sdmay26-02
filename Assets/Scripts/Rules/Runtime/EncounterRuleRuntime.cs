using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
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
                .FactBatchListener(
                    RuleLifecyclePhase.Observation,
                    new EncounterInitiativeAssignmentsListener()
                )
                .FactListener(
                    RuleLifecyclePhase.Observation,
                    new EncounterInitiativeBoundaryListener()
                )
                .FactBatchListener(RuleLifecyclePhase.Observation, new EncounterOutcomeListener());
            return builder;
        }

        /// <summary>Registers encounter handlers and reducer-owned transitions on one dispatcher.</summary>
        /// <param name="builder">The shared dispatcher builder.</param>
        /// <returns>The same builder with encounter rules and no transitional start adapters.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        public static RuleDispatcherBuilder UseEncounterRules(this RuleDispatcherBuilder builder) =>
            UseEncounterRules(
                builder,
                Array.Empty<IEncounterTurnStartAdapter>(),
                RuleRegistry.Empty
            );

        /// <summary>
        /// Registers encounter transitions plus ordered adapters for unmigrated turn-start behavior.
        /// </summary>
        /// <param name="builder">The shared dispatcher builder that owns all encounter rules.</param>
        /// <param name="turnStartAdapters">
        /// The completion-only turn-start adapters to await in exact registration order.
        /// </param>
        /// <returns>The same builder so composition can continue.</returns>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        public static RuleDispatcherBuilder UseEncounterRules(
            this RuleDispatcherBuilder builder,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters
        ) => UseEncounterRules(builder, turnStartAdapters, RuleRegistry.Empty);

        /// <summary>
        /// Registers encounter transitions, ordered turn-start adapters, and atomic effect adoption.
        /// </summary>
        /// <param name="builder">The shared dispatcher builder that owns all encounter rules.</param>
        /// <param name="turnStartAdapters">
        /// The completion-only turn-start adapters to await in exact registration order.
        /// </param>
        /// <param name="registry">
        /// The registry used to validate atomic reinforcement effects. Unity enrollment and these
        /// encounter reducers must receive the same <see cref="RuleRegistry"/> instance used by
        /// active-effect rules.
        /// </param>
        /// <returns>The same builder so composition can continue.</returns>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public static RuleDispatcherBuilder UseEncounterRules(
            this RuleDispatcherBuilder builder,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters,
            RuleRegistry registry
        ) =>
            UseEncounterRules(
                builder,
                turnStartAdapters,
                registry,
                new TurnResourceStrategy(Array.Empty<ITurnResourceContributionProvider>())
            );

        /// <summary>
        /// Registers encounter transitions with explicit adapters, registry, and turn resources.
        /// </summary>
        /// <param name="builder">The shared dispatcher builder.</param>
        /// <param name="turnStartAdapters">Ordered completion-only Unity adapters.</param>
        /// <param name="registry">The active-effect registry shared by enrollment.</param>
        /// <param name="turnResources">The explicitly composed generic refresh strategy.</param>
        /// <returns>The same builder so composition can continue.</returns>
        public static RuleDispatcherBuilder UseEncounterRules(
            this RuleDispatcherBuilder builder,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters,
            RuleRegistry registry,
            TurnResourceStrategy turnResources
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (turnResources == null)
                throw new ArgumentNullException(nameof(turnResources));
            IEncounterTurnStartAdapter[] copied =
                turnStartAdapters?.ToArray()
                ?? throw new ArgumentNullException(nameof(turnStartAdapters));
            if (copied.Any(adapter => adapter == null))
                throw new ArgumentException(
                    "Turn-start adapters cannot contain null entries.",
                    nameof(turnStartAdapters)
                );
            return builder
                .UseMovementBudgetResetRules()
                .RegisterHandler<StartEncounterOp, EncounterStartOutcome>(
                    new StartEncounterHandler()
                )
                .RegisterHandler<JoinEncounterOp, EncounterJoinOutcome>(new JoinEncounterHandler())
                .RegisterHandler<AdvanceEncounterOp, EncounterAdvanceOutcome>(
                    new AdvanceEncounterHandler()
                )
                .RegisterHandler<BeginInitiativeTurnOp, EncounterAdvanceOutcome>(
                    new BeginInitiativeTurnHandler(turnResources)
                )
                .RegisterHandler<EndTurnOp, EncounterAdvanceOutcome>(new EndTurnHandler())
                .RegisterHandler<SuspendEncounterOp, EncounterSuspensionOutcome>(
                    new SuspendEncounterHandler()
                )
                .RegisterHandler<EndEncounterOp, EncounterEndOutcome>(new EndEncounterHandler())
                .RegisterHandler<EvaluateEncounterOutcomeOp, EncounterEvaluationOutcome>(
                    new EvaluateEncounterOutcomeHandler()
                )
                .RegisterHandler<TurnStartingOp, TurnStartCompletion>(
                    new TurnStartingHandler(copied),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<TurnEndingOp, TurnEndContribution>(
                    new TurnEndingHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterEngineReducer<CommitEncounterStartOp, EncounterStartOutcome>(
                    new CommitEncounterStartReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitEncounterJoinOp, EncounterJoinOutcome>(
                    new CommitEncounterJoinReducer(registry),
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
                .RegisterEngineReducer<
                    CommitInitiativeBoundaryPublicationOp,
                    EncounterAdvanceOutcome
                >(new CommitInitiativeBoundaryPublicationReducer(), Source)
                .RegisterEngineReducer<CommitTurnBeginOp, EncounterAdvanceOutcome>(
                    new CommitTurnBeginReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitTurnStartAdapterProgressOp, TurnStartAdapterProgress>(
                    new CommitTurnStartAdapterProgressReducer(),
                    Source
                )
                .RegisterEngineReducer<
                    CommitTurnStartDamageBatchOp,
                    CommitTurnStartDamageBatchOutcome
                >(new CommitTurnStartDamageBatchReducer(), Source)
                .RegisterEngineReducer<CommitInitiativeTurnStartSkippedOp, EncounterAdvanceOutcome>(
                    new CommitInitiativeTurnStartSkippedReducer(),
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

    /// <summary>Identifies when an encounter owns zero-HP reaction and defeat finalization.</summary>
    internal static class EncounterDefeatAuthority
    {
        internal static bool Owns(RulesStateDraft state, CreatureId creature) =>
            state.Encounters.Any(pair =>
                pair.Value.Phase == EncounterPhase.Active
                && pair.Value.Roster.Any(entry => entry.Creature == creature)
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
            EncounterStartOutcome started = EncounterHandlerResults.Require(
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
            return started;
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
            Exception checkpointFailure = null;
            InitiativeEntry[] additions;
            if (!TryResolveExactReplay(frame.Op, encounter, out additions))
            {
                InitiativeEntry current = encounter.Roster[encounter.Cursor];
                additions = frame
                    .Op.Participants.Select(
                        (participant, index) =>
                        {
                            int natural = context.Rolls.Roll(DiceExpressions.D20).Total;
                            int total = checked(
                                natural + participant.Participant.InitiativeModifier
                            );
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
                try
                {
                    EncounterHandlerResults.Require(
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
                }
                catch (Exception failure)
                {
                    encounter = EncounterRuleRuntime.RequireEncounter(
                        context.Snapshot,
                        frame.Op.Encounter
                    );
                    if (!TryResolveExactReplay(frame.Op, encounter, out additions))
                        throw;
                    // Fact observers run after a reducer commits. Finish the workflow's next
                    // durable checkpoint before preserving that observer failure for the root.
                    checkpointFailure = failure;
                }
            }
            // A binding created by the join reducer cannot observe Facts sourced from that same
            // frame. Publishing assignments from this later authoritative frame lets every
            // reinforcement feature observe its committed initiative exactly once.
            try
            {
                EncounterHandlerResults.Require(
                    await context.Dispatch(
                        new CommitInitiativeAssignmentsOp(
                            frame.Op.Encounter,
                            Array.AsReadOnly(additions)
                        )
                    ),
                    "reinforcement initiative assignment"
                );
            }
            catch (Exception failure)
            {
                encounter = EncounterRuleRuntime.RequireEncounter(
                    context.Snapshot,
                    frame.Op.Encounter
                );
                if (!MatchesPublishedAssignments(encounter, additions))
                    throw;
                checkpointFailure =
                    checkpointFailure == null
                        ? failure
                        : new AggregateException(checkpointFailure, failure);
            }
            if (checkpointFailure != null)
                ExceptionDispatchInfo.Capture(checkpointFailure).Throw();
            return new EncounterJoinOutcome(
                EncounterRuleRuntime.RequireEncounter(context.Snapshot, frame.Op.Encounter)
            );
        }

        private static bool TryResolveExactReplay(
            JoinEncounterOp operation,
            EncounterState encounter,
            out InitiativeEntry[] additions
        )
        {
            int committedCount = operation.Participants.Count(participant =>
                encounter.ReinforcementRegistrations.ContainsKey(participant.Participant.Creature)
                || encounter.Roster.Any(entry => entry.Creature == participant.Participant.Creature)
            );
            if (committedCount == 0)
            {
                additions = Array.Empty<InitiativeEntry>();
                return false;
            }
            if (committedCount != operation.Participants.Count)
                throw new InvalidOperationException(
                    "The reinforcement batch conflicts with a partially matching encounter roster."
                );

            additions = new InitiativeEntry[operation.Participants.Count];
            long firstRegistrationOrder = -1;
            for (int index = 0; index < operation.Participants.Count; index++)
            {
                EncounterJoinParticipant participant = operation.Participants[index];
                InitiativeEntry entry = encounter.Roster.Single(candidate =>
                    candidate.Creature == participant.Participant.Creature
                );
                if (index == 0)
                    firstRegistrationOrder = entry.RegistrationOrder;
                if (
                    entry.Team != participant.Participant.Team
                    || entry.Modifier != participant.Participant.InitiativeModifier
                    || entry.RegistrationOrder != firstRegistrationOrder + index
                    || !encounter.ReinforcementRegistrations.TryGetValue(
                        participant.Participant.Creature,
                        out CombatantRulesState registration
                    )
                    || !registration.Equals(participant.Combatant)
                )
                    throw new InvalidOperationException(
                        $"Creature {participant.Participant.Creature.Value} conflicts with its committed reinforcement registration."
                    );
                additions[index] = entry;
            }

            return true;
        }

        private static bool MatchesPublishedAssignments(
            EncounterState encounter,
            IEnumerable<InitiativeEntry> additions
        ) =>
            additions.All(entry =>
                encounter.PublishedInitiativeAssignments.Contains(entry.Creature)
                && encounter.Roster.Any(committed => committed.Equals(entry))
            );
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
            await ExpireEffects(PendingExpirations(context.Snapshot, initial.Id), context);
            initial = EncounterRuleRuntime.RequireEncounter(context.Snapshot, frame.Op.Encounter);
            if (initial.Phase != EncounterPhase.Active)
                return new EncounterAdvanceOutcome(initial);
            EncounterOutcome? immediate = EncounterRuleRuntime.Evaluate(context.Snapshot, initial);
            if (immediate.HasValue)
            {
                EncounterEndOutcome ended = EncounterHandlerResults.Require(
                    await context.Dispatch(new EndEncounterOp(initial.Id, immediate.Value)),
                    "encounter outcome"
                );
                return new EncounterAdvanceOutcome(ended.State);
            }
            if (initial.IsTurnStartPending)
            {
                InitiativeEntry pending = initial.Roster[initial.Cursor];
                if (
                    pending.EligibleFromRound.CompareTo(initial.Round) <= 0
                    && EncounterEndValidation.IsLiving(context.Snapshot, pending.Creature)
                )
                {
                    return EncounterHandlerResults.Require(
                        await context.Dispatch(
                            new BeginInitiativeTurnOp(
                                initial.Id,
                                initial.Round,
                                initial.Cursor,
                                pending.Creature
                            )
                        ),
                        "published initiative turn resume"
                    );
                }
            }
            if (initial.IsInitiativeBoundaryPending)
            {
                await PublishBoundary(initial, context);
                return new EncounterAdvanceOutcome(
                    EncounterRuleRuntime.RequireEncounter(context.Snapshot, frame.Op.Encounter)
                );
            }
            InitiativeBoundaryOutcome boundary = EncounterHandlerResults.Require(
                await context.Dispatch(new CommitInitiativeBoundaryOp(frame.Op.Encounter)),
                "initiative boundary"
            );
            await ExpireEffects(boundary.DueEffects, context);
            EncounterState reached = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            if (reached.Phase == EncounterPhase.Active)
                await PublishBoundary(reached, context);
            return new EncounterAdvanceOutcome(
                EncounterRuleRuntime.RequireEncounter(context.Snapshot, frame.Op.Encounter)
            );
        }

        private static ActiveEffectTimingState[] PendingExpirations(
            RulesSnapshot snapshot,
            EncounterId encounter
        ) =>
            snapshot
                // Zero remains durable until ExpireActiveEffectOp commits. A later Advance request
                // therefore resumes deterministic expiration before it consumes another boundary
                // when an earlier expiration callback failed after the boundary transaction.
                .ActiveEffectTimings.Where(pair =>
                    pair.Value.Encounter == encounter
                    && !pair.Value.ExpiresWithEncounter
                    && pair.Value.RemainingBoundaries == 0
                )
                .Select(pair => pair.Value)
                .OrderBy(value => value.CreationOrder)
                .ThenBy(value => value.Effect.Value, StringComparer.Ordinal)
                .ToArray();

        private static async ValueTask PublishBoundary(
            EncounterState encounter,
            OpHandlerContext context
        )
        {
            InitiativeEntry entry = encounter.Roster[encounter.Cursor];
            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitInitiativeBoundaryPublicationOp(
                        encounter.Id,
                        encounter.Round,
                        encounter.Cursor,
                        entry.Creature
                    )
                ),
                "initiative boundary publication"
            );
        }

        private static async ValueTask ExpireEffects(
            IEnumerable<ActiveEffectTimingState> timings,
            OpHandlerContext context
        )
        {
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
                    "timed effect expiration"
                );
            }
        }
    }

    internal sealed class BeginInitiativeTurnHandler
        : IOpHandler<BeginInitiativeTurnOp, EncounterAdvanceOutcome>
    {
        private readonly TurnResourceStrategy turnResources;

        internal BeginInitiativeTurnHandler(TurnResourceStrategy turnResources) =>
            this.turnResources =
                turnResources ?? throw new ArgumentNullException(nameof(turnResources));

        public async ValueTask<EncounterAdvanceOutcome> Handle(
            OpFrame<BeginInitiativeTurnOp> frame,
            OpHandlerContext context
        )
        {
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            if (
                encounter.Phase != EncounterPhase.Active
                || encounter.CurrentTurn.HasValue
                || encounter.IsInitiativeBoundaryPending
                || !encounter.IsTurnStartPending
                || encounter.Round != frame.Op.Round
                || encounter.Cursor != frame.Op.Slot
                || encounter.Roster[encounter.Cursor].Creature != frame.Op.Actor
            )
                throw new InvalidOperationException(
                    "The reached initiative boundary is no longer current."
                );
            InitiativeEntry entry = encounter.Roster[encounter.Cursor];
            if (
                entry.EligibleFromRound.CompareTo(encounter.Round) > 0
                || !EncounterEndValidation.IsLiving(context.Snapshot, entry.Creature)
            )
                throw new InvalidOperationException(
                    "Only an eligible living initiative boundary can begin a turn."
                );

            EncounterHandlerResults.Require(
                await context.Dispatch(new ResetMovementBudgetOp(entry.Creature)),
                "turn-start movement reset"
            );

            EncounterHandlerResults.Require(
                await context.Dispatch(new TurnStartingOp(frame.Op.Encounter, entry.Creature)),
                "turn-start hook"
            );
            if (!EncounterEndValidation.IsLiving(context.Snapshot, entry.Creature))
            {
                // The hook's zero-HP Fact belongs to this causal root. Return without a turn so
                // its Reaction listeners settle before encounter Observation evaluates outcome.
                return EncounterHandlerResults.Require(
                    await context.Dispatch(
                        new CommitInitiativeTurnStartSkippedOp(
                            frame.Op.Encounter,
                            frame.Op.Round,
                            frame.Op.Slot,
                            frame.Op.Actor
                        )
                    ),
                    "initiative turn-start skip"
                );
            }
            return EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitTurnBeginOp(
                        frame.Op.Encounter,
                        entry.Creature,
                        turnResources.CreatePlan(context.Snapshot, entry.Creature)
                    )
                ),
                "turn begin"
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
            if (!context.Snapshot.ActionEconomy.Contains(frame.Op.Turn.Actor))
                throw new InvalidOperationException(
                    "The turn actor has no authoritative action-economy state."
                );
            if (!context.Snapshot.MultipleAttackPenalty.Contains(frame.Op.Turn.Actor))
                throw new InvalidOperationException(
                    "The turn actor has no authoritative multiple-attack-penalty state."
                );
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
            return await AdvanceAfterCommittedTurnEnd(frame.Op.Turn.Encounter, context);
        }

        /// <summary>
        /// Advances the encounter after a committed turn end, resuming one uncertain post-commit
        /// advance before reporting its original failure.
        /// </summary>
        /// <remarks>
        /// A committed initiative boundary can invoke arbitrary fact delivery before it starts the
        /// next turn. If that delivery fails, the durable encounter state is the recovery
        /// checkpoint: only the same active encounter with no current turn can be resumed. A
        /// completed next turn is never replayed, and failures before <see cref="CommitTurnEndOp"/>
        /// never reach this method.
        /// </remarks>
        private static async ValueTask<EncounterAdvanceOutcome> AdvanceAfterCommittedTurnEnd(
            EncounterId encounter,
            OpHandlerContext context
        )
        {
            try
            {
                return EncounterHandlerResults.Require(
                    await context.Dispatch(new AdvanceEncounterOp(encounter)),
                    "turn advance"
                );
            }
            catch (Exception failure)
            {
                EncounterState latest = EncounterRuleRuntime.RequireEncounter(
                    context.Snapshot,
                    encounter
                );
                if (
                    latest.Phase != EncounterPhase.Active
                    || latest.CurrentTurn.HasValue
                    || latest.IsTurnStartPending
                )
                    ExceptionDispatchInfo.Capture(failure).Throw();

                ObserverFailureState failures = ObserverFailureState
                    .CreateEmpty("Multiple turn-advance recovery failures were preserved.")
                    .Add(failure);
                try
                {
                    EncounterHandlerResults.Require(
                        await context.Dispatch(new AdvanceEncounterOp(encounter)),
                        "turn-advance recovery"
                    );
                }
                catch (Exception recoveryFailure)
                {
                    failures = failures.Add(recoveryFailure);
                }
                failures.ThrowIfAny();
                throw new InvalidOperationException(
                    "Turn-advance recovery did not report a failure."
                );
            }
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

    internal sealed class TurnStartingHandler : IOpHandler<TurnStartingOp, TurnStartCompletion>
    {
        private readonly IReadOnlyList<IEncounterTurnStartAdapter> adapters;

        public TurnStartingHandler(IEnumerable<IEncounterTurnStartAdapter> adapters) =>
            this.adapters = Array.AsReadOnly(adapters.ToArray());

        public async ValueTask<TurnStartCompletion> Handle(
            OpFrame<TurnStartingOp> frame,
            OpHandlerContext context
        )
        {
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            TurnStartAdapterProgress progress = encounter.TurnStartAdapterProgress;
            if (
                encounter.Phase != EncounterPhase.Active
                || encounter.CurrentTurn.HasValue
                || encounter.IsInitiativeBoundaryPending
                || !encounter.IsTurnStartPending
                || encounter.Cursor < 0
                || encounter.Roster[encounter.Cursor].Creature != frame.Op.Actor
                || progress == null
                || progress.NextAdapterIndex > adapters.Count
            )
                throw new InvalidOperationException(
                    "Turn-start adapters require the exact current published boundary progress."
                );
            for (int index = progress.NextAdapterIndex; index < adapters.Count; index++)
            {
                EncounterTurnStartContext adapterContext = new EncounterTurnStartContext(
                    frame.Op.Encounter,
                    frame.Op.Actor,
                    encounter.Round,
                    encounter.Cursor,
                    index,
                    context
                );
                await adapters[index].Apply(adapterContext);
                progress = EncounterRuleRuntime
                    .RequireEncounter(context.Snapshot, frame.Op.Encounter)
                    .TurnStartAdapterProgress;
                if (adapterContext.HasCommittedCompletion)
                {
                    // The adapter used its atomic mutation-plus-completion boundary. Its durable
                    // checkpoint already owns recovery, so a second progress commit would conflict.
                    if (progress == null || progress.NextAdapterIndex != index + 1)
                        throw new InvalidOperationException(
                            "The atomically completed adapter has conflicting progress."
                        );
                }
                else
                {
                    progress = EncounterHandlerResults.Require(
                        await context.Dispatch(
                            new CommitTurnStartAdapterProgressOp(
                                frame.Op.Encounter,
                                encounter.Round,
                                encounter.Cursor,
                                frame.Op.Actor,
                                index
                            )
                        ),
                        "turn-start adapter progress"
                    );
                }
                if (!EncounterEndValidation.IsLiving(context.Snapshot, frame.Op.Actor))
                    break;
            }
            return TurnStartCompletion.Complete;
        }
    }

    internal sealed class TurnEndingHandler : IOpHandler<TurnEndingOp, TurnEndContribution>
    {
        public ValueTask<TurnEndContribution> Handle(
            OpFrame<TurnEndingOp> frame,
            OpHandlerContext context
        ) => new ValueTask<TurnEndContribution>(TurnEndContribution.Complete);
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

    internal sealed class EncounterInitiativeAssignmentsListener
        : IRuleFactBatchListener<InitiativeAssignedFact>
    {
        public async ValueTask OnFactsCommitted(
            CommittedFactBatch<InitiativeAssignedFact> batch,
            FactContext context
        )
        {
            EncounterId encounterId = batch.Facts[0].Encounter;
            if (batch.Facts.Any(fact => fact.Encounter != encounterId))
                throw new InvalidOperationException(
                    "One initiative-assignment batch cannot span encounters."
                );
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                encounterId
            );
            if (
                encounter.Phase != EncounterPhase.Active
                || encounter.CurrentTurn.HasValue
                || encounter.Cursor >= 0
            )
                return;
            EncounterHandlerResults.Require(
                await context.Dispatch(new AdvanceEncounterOp(encounter.Id)),
                "first initiative boundary"
            );
        }
    }

    internal sealed class EncounterInitiativeBoundaryListener
        : IRuleFactListener<InitiativeBoundaryReachedFact>
    {
        public async ValueTask OnFactCommitted(
            InitiativeBoundaryReachedFact fact,
            FactContext context
        )
        {
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                fact.Encounter
            );
            if (encounter.Phase != EncounterPhase.Active || encounter.CurrentTurn.HasValue)
                return;
            if (
                encounter.Cursor < 0
                || encounter.IsInitiativeBoundaryPending
                || !encounter.IsTurnStartPending
                || encounter.Round != fact.Round
                || encounter.Roster[encounter.Cursor].Creature != fact.Creature
            )
                throw new InvalidOperationException(
                    "The committed initiative boundary no longer matches encounter state."
                );

            EncounterOutcome? outcome = EncounterRuleRuntime.Evaluate(context.Snapshot, encounter);
            if (outcome.HasValue)
            {
                EncounterHandlerResults.Require(
                    await context.Dispatch(new EndEncounterOp(encounter.Id, outcome.Value)),
                    "initiative-boundary encounter outcome"
                );
                return;
            }

            InitiativeEntry entry = encounter.Roster[encounter.Cursor];
            if (
                entry.EligibleFromRound.CompareTo(encounter.Round) > 0
                || !EncounterEndValidation.IsLiving(context.Snapshot, entry.Creature)
            )
            {
                EncounterHandlerResults.Require(
                    await context.Dispatch(new AdvanceEncounterOp(encounter.Id)),
                    "skipped initiative boundary"
                );
                return;
            }

            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new BeginInitiativeTurnOp(
                        encounter.Id,
                        encounter.Round,
                        encounter.Cursor,
                        entry.Creature
                    )
                ),
                "initiative turn begin"
            );
        }
    }
}
