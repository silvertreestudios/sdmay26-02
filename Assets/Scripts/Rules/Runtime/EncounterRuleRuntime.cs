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
                .FactListener(
                    RuleLifecyclePhase.Observation,
                    new EncounterInitiativeBoundaryListener()
                )
                .FactBatchListener(RuleLifecyclePhase.Observation, new EncounterOutcomeListener());
            return builder;
        }

        /// <summary>Registers encounter handlers and reducer-owned transitions on one dispatcher.</summary>
        /// <param name="builder">The shared dispatcher builder.</param>
        /// <param name="registry">
        /// The exact immutable registry used to validate every combatant enrollment binding.
        /// </param>
        /// <returns>The same builder with encounter rules and no transitional start adapters.</returns>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public static RuleDispatcherBuilder UseEncounterRules(
            this RuleDispatcherBuilder builder,
            RuleRegistry registry
        ) => UseEncounterRules(builder, registry, Array.Empty<IEncounterTurnStartAdapter>());

        /// <summary>
        /// Registers encounter transitions plus ordered adapters for unmigrated turn-start behavior.
        /// </summary>
        /// <param name="builder">The shared dispatcher builder that owns all encounter rules.</param>
        /// <param name="registry">
        /// The exact immutable registry used by the dispatcher and enrollment reducer.
        /// </param>
        /// <param name="turnStartAdapters">
        /// The spell, aura, and action-contribution adapters to await in exact registration order.
        /// </param>
        /// <returns>The same builder so composition can continue.</returns>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public static RuleDispatcherBuilder UseEncounterRules(
            this RuleDispatcherBuilder builder,
            RuleRegistry registry,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
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
                .RegisterHandler<InitEncounterOp, EncounterInitializationOutcome>(
                    new InitEncounterHandler()
                )
                .RegisterHandler<AddCombatantsOp, CombatantsAddedOutcome>(
                    new AddCombatantsHandler()
                )
                .RegisterHandler<AdvanceEncounterOp, EncounterAdvanceOutcome>(
                    new AdvanceEncounterHandler()
                )
                .RegisterHandler<BeginInitiativeTurnOp, EncounterAdvanceOutcome>(
                    new BeginInitiativeTurnHandler()
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
                .RegisterHandler<SpendEncounterActionsOp, EncounterActionSpendOutcome>(
                    new SpendEncounterActionsHandler()
                )
                .RegisterEngineReducer<
                    CommitEncounterInitializationOp,
                    EncounterInitializationOutcome
                >(new CommitEncounterInitializationReducer(), Source)
                .RegisterEngineReducer<CommitCombatantsAdditionOp, CombatantsAddedOutcome>(
                    new CommitCombatantsAdditionReducer(registry),
                    Source
                )
                .RegisterEngineReducer<CommitEncounterActivationOp, EncounterAdvanceOutcome>(
                    new CommitEncounterActivationReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitInitiativeAssignmentsOp, InitiativeAssignmentsOutcome>(
                    new CommitInitiativeAssignmentsReducer(),
                    Source
                )
                .RegisterEngineReducer<CommitInitiativeBoundaryOp, EncounterAdvanceOutcome>(
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
                .RegisterEngineReducer<CommitEncounterActionsOp, EncounterActionSpendOutcome>(
                    new SpendEncounterActionsReducer(),
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

    internal sealed class InitEncounterHandler
        : IOpHandler<InitEncounterOp, EncounterInitializationOutcome>
    {
        public async ValueTask<EncounterInitializationOutcome> Handle(
            OpFrame<InitEncounterOp> frame,
            OpHandlerContext context
        )
        {
            if (
                context.Snapshot.Encounters.Contains(frame.Op.Encounter)
                || context.Snapshot.Encounters.Any(pair =>
                    pair.Value.Phase == EncounterPhase.Initialized
                    || pair.Value.Phase == EncounterPhase.Active
                )
            )
                throw new InvalidOperationException(
                    "The encounter is duplicate or another encounter is active or initialized."
                );
            return EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitEncounterInitializationOp(
                        frame.Op.Encounter,
                        frame.Op.ProtagonistTeam,
                        frame.Op.ConclusionPolicy
                    )
                ),
                "encounter initialization"
            );
        }
    }

    internal sealed class AddCombatantsHandler : IOpHandler<AddCombatantsOp, CombatantsAddedOutcome>
    {
        public async ValueTask<CombatantsAddedOutcome> Handle(
            OpFrame<AddCombatantsOp> frame,
            OpHandlerContext context
        )
        {
            EncounterState encounter = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            if (
                encounter.Phase != EncounterPhase.Initialized
                && encounter.Phase != EncounterPhase.Active
            )
                throw new InvalidOperationException(
                    "Combatants can be added only to an initialized or active encounter."
                );
            if (encounter.Phase == EncounterPhase.Active && !encounter.CurrentTurn.HasValue)
                throw new InvalidOperationException(
                    "Combatants require the active encounter's exact current turn."
                );
            HashSet<CreatureId> existing = new HashSet<CreatureId>(
                encounter.Roster.Select(entry => entry.Creature)
            );
            if (frame.Op.Combatants.Any(combatant => existing.Contains(combatant.Creature.Id)))
                throw new InvalidOperationException(
                    "A combatant is already in the encounter roster."
                );

            long nextRegistrationOrder =
                encounter.Roster.Count == 0
                    ? 0
                    : checked(encounter.Roster.Max(entry => entry.RegistrationOrder) + 1);
            InitiativeEntry[] rolled = frame
                .Op.Combatants.Select(
                    (combatant, index) =>
                        new InitiativeEntry(
                            combatant.Creature.Id,
                            combatant.Creature.Player,
                            context.Rolls.Roll(DiceExpressions.D20).Total,
                            combatant.InitiativeModifier,
                            checked(nextRegistrationOrder + index),
                            encounter.Round
                        )
                )
                .ToArray();
            InitiativeEntry[] ordered = encounter
                .Roster.Concat(rolled)
                .OrderByDescending(entry => entry.Total)
                .ThenBy(entry => entry.RegistrationOrder)
                .ToArray();
            int reachedIndex = encounter.CurrentTurn.HasValue
                ? Array.FindIndex(
                    ordered,
                    entry => entry.Creature == encounter.CurrentTurn.Value.Actor
                )
                : -1;
            CombatantAddition[] additions = rolled
                .Select(
                    (entry, index) =>
                    {
                        int insertionIndex = Array.IndexOf(ordered, entry);
                        RoundNumber eligible =
                            reachedIndex >= 0 && insertionIndex <= reachedIndex
                                ? encounter.Round.Next()
                                : encounter.Round;
                        InitiativeEntry normalized = new InitiativeEntry(
                            entry.Creature,
                            entry.Team,
                            entry.NaturalRoll,
                            entry.Modifier,
                            entry.RegistrationOrder,
                            eligible
                        );
                        return new CombatantAddition(normalized, frame.Op.Combatants[index]);
                    }
                )
                .ToArray();
            CombatantsAddedOutcome added = EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitCombatantsAdditionOp(frame.Op.Encounter, Array.AsReadOnly(additions))
                ),
                "combatant addition"
            );
            // A binding created by the addition reducer cannot observe Facts sourced from that same
            // frame. Publishing assignments from this later authoritative frame lets every
            // newly added feature observe its committed initiative exactly once.
            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitInitiativeAssignmentsOp(
                        frame.Op.Encounter,
                        Array.AsReadOnly(additions.Select(value => value.Initiative).ToArray())
                    )
                ),
                "initiative assignment"
            );
            return added;
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
            if (initial.Phase == EncounterPhase.Initialized)
            {
                EncounterHandlerResults.Require(
                    await context.Dispatch(new CommitEncounterActivationOp(initial.Id)),
                    "encounter activation"
                );
                initial = EncounterRuleRuntime.RequireEncounter(context.Snapshot, initial.Id);
            }
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
            EncounterHandlerResults.Require(
                await context.Dispatch(new CommitInitiativeBoundaryOp(frame.Op.Encounter)),
                "initiative boundary"
            );
            return new EncounterAdvanceOutcome(
                EncounterRuleRuntime.RequireEncounter(context.Snapshot, frame.Op.Encounter)
            );
        }
    }

    internal sealed class BeginInitiativeTurnHandler
        : IOpHandler<BeginInitiativeTurnOp, EncounterAdvanceOutcome>
    {
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

            TurnStartContribution contribution = EncounterHandlerResults.Require(
                await context.Dispatch(new TurnStartingOp(frame.Op.Encounter, entry.Creature)),
                "turn-start hook"
            );
            EncounterState latest = EncounterRuleRuntime.RequireEncounter(
                context.Snapshot,
                frame.Op.Encounter
            );
            if (!EncounterEndValidation.IsLiving(context.Snapshot, entry.Creature))
            {
                // The hook's zero-HP Fact belongs to this causal root. Return without a turn so
                // its Reaction listeners settle before encounter Observation evaluates outcome.
                return new EncounterAdvanceOutcome(latest);
            }
            return EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitTurnBeginOp(frame.Op.Encounter, entry.Creature, contribution.Actions)
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
                )
                    continue;
                EncounterHandlerResults.Require(
                    await context.Dispatch(
                        new RemoveActiveEffectOp(
                            effect.Id,
                            timing.Binding,
                            effect.EffectStateVersion,
                            ActiveEffectRemovalReason.Expired,
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

    internal sealed class SpendEncounterActionsHandler
        : IOpHandler<SpendEncounterActionsOp, EncounterActionSpendOutcome>
    {
        public async ValueTask<EncounterActionSpendOutcome> Handle(
            OpFrame<SpendEncounterActionsOp> frame,
            OpHandlerContext context
        ) =>
            EncounterHandlerResults.Require(
                await context.Dispatch(
                    new CommitEncounterActionsOp(
                        frame.Op.Actor,
                        frame.Op.Amount,
                        frame.Op.RequiredLivingTargets
                    )
                ),
                "encounter action spend"
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
