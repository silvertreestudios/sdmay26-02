using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class EncounterRuntimeTests
    {
        private static readonly CreatureId Hero = new CreatureId("hero");
        private static readonly CreatureId Enemy = new CreatureId("enemy");
        private static readonly CreatureId Reinforcement = new CreatureId("reinforcement");
        private static readonly CreatureId SecondReinforcement = new CreatureId(
            "second-reinforcement"
        );
        private static readonly PlayerId Players = new PlayerId("players");
        private static readonly PlayerId Enemies = new PlayerId("enemies");
        private static readonly EncounterId Encounter = new EncounterId("test-encounter");
        private static readonly RuleSource Source = RuleSource.FromSlug("encounter-test");

        /// <summary>Identifies one state collection that can reject an atomic join draft.</summary>
        public enum JoinRegistrationCollision
        {
            /// <summary>The creature identity slice.</summary>
            Creature,

            /// <summary>The immutable prepared-input slice.</summary>
            PreparedInputs,

            /// <summary>The health slice.</summary>
            Health,

            /// <summary>The position slice.</summary>
            Position,

            /// <summary>The land Speed slice.</summary>
            LandSpeed,

            /// <summary>The action-economy slice.</summary>
            ActionEconomy,

            /// <summary>The multiple-attack-penalty slice.</summary>
            MultipleAttackPenalty,

            /// <summary>The spell-slot pool slice.</summary>
            SpellSlot,

            /// <summary>The active rule-binding slice.</summary>
            RuleBinding,
        }

        [Test]
        public void StartRequestRequiresProtagonistMembershipAfterRosterCopy()
        {
            ArgumentException missing = Assert.Throws<ArgumentException>(() =>
                new StartEncounterOp(
                    Encounter,
                    Players,
                    new[] { new EncounterParticipant(Enemy, Enemies, 0) }
                )
            );

            Assert.That(missing.ParamName, Is.EqualTo("participants"));
            Assert.That(missing.Message, Does.Contain("protagonist team"));
            Assert.DoesNotThrow(() =>
                new StartEncounterOp(
                    Encounter,
                    Players,
                    new[]
                    {
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0),
                    }
                )
            );
            Assert.DoesNotThrow(() =>
                new StartEncounterOp(
                    Encounter,
                    Players,
                    new[] { new EncounterParticipant(Hero, Players, 0) }
                )
            );
        }

        [Test]
        public void PublishedTurnStartCheckpointIsValidatedAndParticipatesInSnapshotIdentity()
        {
            InitiativeEntry[] roster = { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) };
            EncounterState pending = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                roster,
                0,
                null,
                1,
                null,
                isTurnStartPending: true
            );
            EncounterState matching = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                roster,
                0,
                null,
                1,
                null,
                isTurnStartPending: true
            );
            EncounterState ordinary = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                roster,
                0,
                null,
                1,
                null
            );
            EncounterState progressed = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                roster,
                0,
                null,
                1,
                null,
                isTurnStartPending: true,
                turnStartAdapterProgress: new TurnStartAdapterProgress(
                    1,
                    new TurnStartContribution(2)
                )
            );

            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed().SeedEncounter(pending)
            ).Snapshot;

            Assert.That(snapshot.Encounters[Encounter].IsTurnStartPending, Is.True);
            Assert.That(
                snapshot.Encounters[Encounter].TurnStartAdapterProgress,
                Is.EqualTo(TurnStartAdapterProgress.Initial)
            );
            Assert.That(pending, Is.EqualTo(matching));
            Assert.That(pending.GetHashCode(), Is.EqualTo(matching.GetHashCode()));
            Assert.That(pending, Is.Not.EqualTo(ordinary));
            Assert.That(pending, Is.Not.EqualTo(progressed));
            Assert.Throws<ArgumentException>(() =>
                new EncounterState(
                    Encounter,
                    EncounterPhase.Active,
                    Players,
                    RoundNumber.First,
                    roster,
                    -1,
                    null,
                    1,
                    null,
                    isTurnStartPending: true
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new EncounterState(
                    Encounter,
                    EncounterPhase.Active,
                    Players,
                    RoundNumber.First,
                    roster,
                    0,
                    null,
                    1,
                    null,
                    isInitiativeBoundaryPending: true,
                    isTurnStartPending: true
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new EncounterState(
                    Encounter,
                    EncounterPhase.Suspended,
                    Players,
                    RoundNumber.First,
                    roster,
                    0,
                    null,
                    1,
                    null,
                    isTurnStartPending: true
                )
            );
        }

        [Test]
        public async Task AdvanceResumesExactPublishedBoundaryWithoutConsumingAnotherSlot()
        {
            RecordingTurnStartAdapter adapter = new RecordingTurnStartAdapter("resume");
            EncounterState pending = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                new RoundNumber(3),
                new[] { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) },
                1,
                null,
                7,
                null,
                isTurnStartPending: true
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(),
                BaseSeed().SeedEncounter(pending),
                turnStartAdapters: new[] { adapter }
            );

            EncounterState resumed = Resolved(
                await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter))
            ).Value.State;

            Assert.That(resumed.Round, Is.EqualTo(new RoundNumber(3)));
            Assert.That(resumed.Cursor, Is.EqualTo(1));
            Assert.That(resumed.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
            Assert.That(resumed.CurrentTurn.Value.Round, Is.EqualTo(new RoundNumber(3)));
            Assert.That(resumed.CurrentTurn.Value.RosterIndex, Is.EqualTo(1));
            Assert.That(resumed.CurrentTurn.Value.Turn, Is.EqualTo(new TurnId(7)));
            Assert.That(resumed.IsTurnStartPending, Is.False);
            Assert.That(adapter.Actors, Is.EqualTo(new[] { Enemy }));
        }

        [Test]
        public async Task TurnStartProgressCheckpointsCompletedAdaptersBeforeRetry()
        {
            EncounterState pending = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                new[] { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) },
                0,
                null,
                1,
                null,
                isTurnStartPending: true
            );
            DamageAndContributionTurnStartAdapter first = new DamageAndContributionTurnStartAdapter(
                Hero,
                1,
                2
            );
            ThrowOnceTurnStartAdapter second = new ThrowOnceTurnStartAdapter();
            RecordingTurnStartAdapter third = new RecordingTurnStartAdapter("third", actions: 1);
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(),
                BaseSeed().SeedEncounter(pending),
                turnStartAdapters: new IEncounterTurnStartAdapter[] { first, second, third }
            );
            CountingFactObserver<DamageAppliedFact> healthFacts =
                new CountingFactObserver<DamageAppliedFact>();
            using IDisposable healthRegistration = dispatcher.RegisterFactObserver(healthFacts);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter))
            );

            EncounterState checkpoint = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(checkpoint.IsTurnStartPending, Is.True);
            Assert.That(checkpoint.TurnStartAdapterProgress.NextAdapterIndex, Is.EqualTo(1));
            Assert.That(checkpoint.TurnStartAdapterProgress.Contribution.Actions, Is.EqualTo(2));
            Assert.That(dispatcher.Snapshot.Health[Hero].Current, Is.EqualTo(9));
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(1));
            Assert.That(third.Actors, Is.Empty);
            Assert.That(healthFacts.Calls, Is.EqualTo(1));

            EncounterState begun = Resolved(
                await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter))
            ).Value.State;

            Assert.That(begun.CurrentTurn.Value.Actor, Is.EqualTo(Hero));
            Assert.That(begun.IsTurnStartPending, Is.False);
            Assert.That(begun.TurnStartAdapterProgress, Is.Null);
            Assert.That(dispatcher.Snapshot.ActionEconomy[Hero].ActionsRemaining, Is.EqualTo(1));
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(2));
            Assert.That(third.Actors, Is.EqualTo(new[] { Hero }));
            Assert.That(healthFacts.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task TurnStartProgressSurvivesPostCommitObserverFailureWithoutReplayingAdapter()
        {
            EncounterState pending = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                new[] { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) },
                0,
                null,
                1,
                null,
                isTurnStartPending: true
            );
            RecordingTurnStartAdapter first = new RecordingTurnStartAdapter("first");
            RecordingTurnStartAdapter second = new RecordingTurnStartAdapter("second");
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(),
                BaseSeed().SeedEncounter(pending),
                turnStartAdapters: new IEncounterTurnStartAdapter[] { first, second }
            );
            using (
                dispatcher.RegisterFactObserver<TurnStartAdapterProgressCommittedFact>(
                    new ThrowOnceFactObserver<TurnStartAdapterProgressCommittedFact>()
                )
            )
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter))
                );
            }

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].TurnStartAdapterProgress.NextAdapterIndex,
                Is.EqualTo(1)
            );
            EncounterState begun = Resolved(
                await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter))
            ).Value.State;

            Assert.That(begun.CurrentTurn.Value.Actor, Is.EqualTo(Hero));
            Assert.That(first.Actors, Is.EqualTo(new[] { Hero }));
            Assert.That(second.Actors, Is.EqualTo(new[] { Hero }));
        }

        [TestCase("encounter")]
        [TestCase("round")]
        [TestCase("slot")]
        [TestCase("actor")]
        [TestCase("index")]
        [TestCase("contribution")]
        public async Task TurnStartProgressReducerRejectsEveryExactMismatchWithoutMutation(
            string mismatch
        )
        {
            EncounterState pending = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                new[] { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) },
                0,
                null,
                1,
                null,
                isTurnStartPending: true
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(),
                BaseSeed().SeedEncounter(pending)
            );
            RulesSnapshot before = dispatcher.Snapshot;
            CommitTurnStartAdapterProgressOp operation = new CommitTurnStartAdapterProgressOp(
                mismatch == "encounter" ? new EncounterId("stale-encounter") : Encounter,
                mismatch == "round" ? RoundNumber.First.Next() : RoundNumber.First,
                mismatch == "slot" ? 1 : 0,
                mismatch == "actor" ? Enemy : Hero,
                mismatch == "index" ? 1 : 0,
                mismatch == "contribution"
                    ? new TurnStartContribution(2)
                    : TurnStartContribution.Standard,
                new TurnStartContribution(2)
            );

            OpResult<TurnStartAdapterProgress> result = Resolved(
                await dispatcher.Dispatch(new CommitTurnStartAdapterProgressWorkflowOp(operation))
            ).Value;

            Assert.That(result, Is.TypeOf<InvalidOpResult<TurnStartAdapterProgress>>());
            Assert.That(result.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(dispatcher.Snapshot.Encounters[Encounter], Is.EqualTo(pending));
        }

        [Test]
        public async Task CommitStartReducerRejectsRosterWithoutDesignatedProtagonist()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService());
            InitiativeEntry[] roster = { Entry(Enemy, Enemies, 10, 0) };

            OpResult<OpResult<EncounterStartOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterStartWorkflowOp(
                    new CommitEncounterStartOp(Encounter, Players, Array.AsReadOnly(roster))
                )
            );
            OpResult<EncounterStartOutcome> result = Resolved(workflow).Value;

            Assert.That(result, Is.TypeOf<InvalidOpResult<EncounterStartOutcome>>());
            Assert.That(
                ((InvalidOpResult<EncounterStartOutcome>)result).Reason,
                Does.Contain("designated protagonist team")
            );
            Assert.That(dispatcher.Snapshot.Encounters.Contains(Encounter), Is.False);
        }

        [Test]
        public async Task CommitStartReducerRejectsTeamConflictWithoutMutation()
        {
            RulesStateSeed seed = BaseSeed()
                .SeedCreature(new CreatureState(Hero, Enemies))
                .SeedCreature(new CreatureState(Enemy, Enemies));
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed);
            InitiativeEntry[] roster = { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) };

            OpResult<OpResult<EncounterStartOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterStartWorkflowOp(
                    new CommitEncounterStartOp(Encounter, Players, Array.AsReadOnly(roster))
                )
            );
            OpResult<EncounterStartOutcome> result = Resolved(workflow).Value;

            Assert.That(result, Is.TypeOf<InvalidOpResult<EncounterStartOutcome>>());
            Assert.That(
                ((InvalidOpResult<EncounterStartOutcome>)result).Reason,
                Does.Contain("conflicts with its authoritative creature state")
            );
            Assert.That(dispatcher.Snapshot.Encounters.Contains(Encounter), Is.False);
        }

        [Test]
        public async Task CommitStartReducerRejectsOutcomeBindingCollisionWithoutMutation()
        {
            BindingId bindingId = EncounterRuleRuntime.OutcomeBindingId(Encounter);
            ActiveRuleBinding existing = new ActiveRuleBinding(
                bindingId,
                EncounterRuleRuntime.OutcomeDefinitionId,
                Enemy,
                null,
                Source,
                99
            );
            RulesStateSeed seed = BaseSeed().SeedRuleBinding(existing);
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed);
            InitiativeEntry[] roster = { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) };

            OpResult<OpResult<EncounterStartOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterStartWorkflowOp(
                    new CommitEncounterStartOp(Encounter, Players, Array.AsReadOnly(roster))
                )
            );
            OpResult<EncounterStartOutcome> result = Resolved(workflow).Value;

            Assert.That(result, Is.TypeOf<InvalidOpResult<EncounterStartOutcome>>());
            Assert.That(
                ((InvalidOpResult<EncounterStartOutcome>)result).Reason,
                Does.Contain("already registered")
            );
            Assert.That(dispatcher.Snapshot.Encounters.Contains(Encounter), Is.False);
            Assert.That(dispatcher.Snapshot.RuleBindings[bindingId], Is.EqualTo(existing));
        }

        [Test]
        public async Task StartAcceptsMixedAndSingleProtagonistTeamRosters()
        {
            RuleDispatcher mixedDispatcher = CreateDispatcher(new ScriptedRollService(20, 10));
            RuleDispatcher singleDispatcher = CreateDispatcher(new ScriptedRollService(20));

            EncounterState mixed = Resolved(
                await mixedDispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            EncounterState single = Resolved(
                await singleDispatcher.Dispatch(Start(new EncounterParticipant(Hero, Players, 0)))
            ).Value.State;

            Assert.That(mixed.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(mixed.CurrentTurn.Value.Actor, Is.EqualTo(Hero));
            Assert.That(single.Phase, Is.EqualTo(EncounterPhase.Ended));
            Assert.That(single.Outcome, Is.EqualTo(EncounterOutcome.PlayerVictory));
        }

        [Test]
        public async Task StartRollsThroughContextAndRetainsRegistrationOrderTies()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(12, 10));

            OpResult<EncounterStartOutcome> result = await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 2)
                )
            );

            EncounterState state = Resolved(result).Value.State;
            Assert.That(state.Round, Is.EqualTo(RoundNumber.First));
            Assert.That(
                state.Roster.Select(entry => entry.Creature),
                Is.EqualTo(new[] { Hero, Enemy })
            );
            Assert.That(
                state.Roster.Select(entry => entry.NaturalRoll),
                Is.EqualTo(new[] { 12, 10 })
            );
            Assert.That(state.CurrentTurn.Value.Actor, Is.EqualTo(Hero));
            Assert.That(
                result.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(
                    new[]
                    {
                        typeof(EncounterStartedFact),
                        typeof(InitiativeAssignedFact),
                        typeof(InitiativeAssignedFact),
                    }
                )
            );
            Assert.That(dispatcher.Trace.GetRolls(new OpId(1)), Has.Count.EqualTo(2));
        }

        [Test]
        public async Task TurnStartClearsStaleMovementThenEndTurnResetsActionsMapAndWrapsRoundOnce()
        {
            MovementBudgetState budget = new MovementBudgetState(
                new MovementBudgetId(new OpId(99)),
                Hero,
                new GridDistance(15),
                DiagonalMovementPhase.NextCostsTenFeet
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                BaseSeed()
                    .SeedCreature(new CreatureState(Hero, Players))
                    .SeedMovementBudget(Hero, budget)
            );
            CountingFactObserver<MovementBudgetResetFact> movementResets =
                new CountingFactObserver<MovementBudgetResetFact>();
            dispatcher.RegisterFactObserver<MovementBudgetResetFact>(movementResets);
            EncounterState started = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            Assert.That(dispatcher.Snapshot.MovementBudgets.Contains(Hero), Is.False);
            Assert.That(movementResets.Calls, Is.EqualTo(1));
            await dispatcher.Dispatch(new AdvanceMultipleAttackPenaltyOp(Hero));
            await dispatcher.Dispatch(new SpendEncounterActionsOp(Hero, 1));

            EncounterState enemyTurn = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(started.CurrentTurn.Value))
            ).Value.State;
            EncounterState secondHeroTurn = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(enemyTurn.CurrentTurn.Value))
            ).Value.State;

            Assert.That(secondHeroTurn.Round.Value, Is.EqualTo(2));
            Assert.That(secondHeroTurn.CurrentTurn.Value.Actor, Is.EqualTo(Hero));
            Assert.That(
                dispatcher.Snapshot.ActionEconomy[Hero],
                Is.EqualTo(new ActionEconomyState(3, true))
            );
            Assert.That(dispatcher.Snapshot.MultipleAttackPenalty[Hero].AttackCount, Is.Zero);
            Assert.That(dispatcher.Snapshot.MovementBudgets.Contains(Hero), Is.False);
            Assert.That(movementResets.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ActiveTurnEndClearsCurrentMovementBudget()
        {
            EncounterState active = ActiveTurnEncounter();
            MovementBudgetState budget = new MovementBudgetState(
                new MovementBudgetId(new OpId(99)),
                Hero,
                new GridDistance(15),
                DiagonalMovementPhase.NextCostsTenFeet
            );
            RulesStateSeed seed = BaseSeed()
                .SeedEncounter(active)
                .SeedActionEconomy(Hero, new ActionEconomyState(3, true))
                .SeedMultipleAttackPenalty(Hero, new MultipleAttackPenaltyState(1))
                .SeedMovementBudget(Hero, budget);
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed);
            CountingFactObserver<MovementBudgetResetFact> movementResets =
                new CountingFactObserver<MovementBudgetResetFact>();
            dispatcher.RegisterFactObserver<MovementBudgetResetFact>(movementResets);

            Resolved(await dispatcher.Dispatch(new EndTurnOp(active.CurrentTurn.Value)));

            Assert.That(dispatcher.Snapshot.MovementBudgets.Contains(Hero), Is.False);
            Assert.That(movementResets.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ZeroCostActionAuthorizationRequiresExactTurnWithoutSpendMutation()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10));
            CountingFactObserver<EncounterActionsSpentFact> spends =
                new CountingFactObserver<EncounterActionsSpentFact>();
            dispatcher.RegisterFactObserver<EncounterActionsSpentFact>(spends);
            EncounterState started = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;

            EncounterActionSpendOutcome authorized = Resolved(
                await dispatcher.Dispatch(new SpendEncounterActionsOp(Hero, 0))
            ).Value;

            Assert.That(authorized.Remaining, Is.EqualTo(3));
            Assert.That(
                dispatcher.Snapshot.ActionEconomy[Hero],
                Is.EqualTo(new ActionEconomyState(3, true))
            );
            Assert.That(spends.Calls, Is.Zero);

            EncounterState advanced = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(started.CurrentTurn.Value))
            ).Value.State;
            InvalidOperationException rejected = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new SpendEncounterActionsOp(Hero, 0))
            );

            Assert.That(rejected.Message, Does.Contain("does not own an active current turn"));
            Assert.That(advanced.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
            Assert.That(spends.Calls, Is.Zero);
        }

        [Test]
        public async Task TargetAwareActionSpendRejectsAnyCommittedDefeatAtomically()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10, 5));
            CountingFactObserver<EncounterActionsSpentFact> spends =
                new CountingFactObserver<EncounterActionsSpentFact>();
            dispatcher.RegisterFactObserver<EncounterActionsSpentFact>(spends);
            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0),
                    new EncounterParticipant(Reinforcement, Enemies, 0)
                )
            );
            await dispatcher.Dispatch(
                new ApplyDamageOp(
                    Enemy,
                    10,
                    new HealthChangeOriginId("target-aware-defeat"),
                    Source
                )
            );

            InvalidOperationException rejected = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(
                        new SpendEncounterActionsOp(
                            Hero,
                            1,
                            new[] { Reinforcement, Enemy, Reinforcement }
                        )
                    )
            );

            Assert.That(rejected.Message, Does.Contain("no longer a living participant"));
            Assert.That(
                dispatcher.Snapshot.ActionEconomy[Hero],
                Is.EqualTo(new ActionEconomyState(3, true))
            );
            Assert.That(spends.Calls, Is.Zero);

            EncounterActionSpendOutcome livingTargetSpend = Resolved(
                await dispatcher.Dispatch(
                    new SpendEncounterActionsOp(Hero, 1, new[] { Reinforcement })
                )
            ).Value;
            Assert.That(livingTargetSpend.Remaining, Is.EqualTo(2));
            Assert.That(spends.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task StaleAndDuplicateTurnEndRejectWithoutCommit()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10));
            EncounterState started = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            TurnIdentity stale = new TurnIdentity(
                Encounter,
                new TurnId(99),
                Hero,
                RoundNumber.First,
                0
            );
            long version = dispatcher.Snapshot.Version;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new EndTurnOp(stale))
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(version));
            Resolved(await dispatcher.Dispatch(new EndTurnOp(started.CurrentTurn.Value)));
            long after = dispatcher.Snapshot.Version;
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new EndTurnOp(started.CurrentTurn.Value))
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(after));
        }

        [Test]
        public async Task ZeroHpSlotsStillReachBoundaryAndAreSkippedIteratively()
        {
            RulesStateSeed seed = BaseSeed();
            seed.SeedHealth(Enemy, new HealthState(0, 10));
            RecordingTurnStartAdapter adapter = new RecordingTurnStartAdapter("hook");
            CollectingFactObserver<InitiativeBoundaryReachedFact> boundaries =
                new CollectingFactObserver<InitiativeBoundaryReachedFact>();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 15, 10),
                seed,
                turnStartAdapters: new[] { adapter }
            );
            dispatcher.RegisterFactObserver<InitiativeBoundaryReachedFact>(boundaries);

            EncounterState heroTurn = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0),
                        new EncounterParticipant(Reinforcement, Enemies, 0)
                    )
                )
            ).Value.State;
            OpResult<EncounterAdvanceOutcome> advanced = await dispatcher.Dispatch(
                new EndTurnOp(heroTurn.CurrentTurn.Value)
            );
            EncounterState state = Resolved(advanced).Value.State;

            Assert.That(state.CurrentTurn.Value.Actor, Is.EqualTo(Reinforcement));
            Assert.That(
                boundaries.Facts.Select(fact => fact.Creature),
                Is.EqualTo(new[] { Hero, Enemy, Reinforcement })
            );
            Assert.That(adapter.Actors, Is.EqualTo(new[] { Hero, Reinforcement }));
        }

        [Test]
        public async Task OrderedStartAdaptersSetFinalActionsBeforeTurnBeganFact()
        {
            List<string> order = new List<string>();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                turnStartAdapters: new IEncounterTurnStartAdapter[]
                {
                    new RecordingTurnStartAdapter("spell", order),
                    new RecordingTurnStartAdapter("aura", order),
                    new RecordingTurnStartAdapter("slowed", order, 2),
                }
            );
            TurnBeganSnapshotObserver observer = new TurnBeganSnapshotObserver(order);
            dispatcher.RegisterFactObserver<TurnBeganFact>(observer);

            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );

            Assert.That(order, Is.EqualTo(new[] { "spell", "aura", "slowed", "fact" }));
            Assert.That(observer.ActionsAtFact, Is.EqualTo(2));
            Assert.That(
                dispatcher.Snapshot.ActionEconomy[Hero],
                Is.EqualTo(new ActionEconomyState(2, true))
            );
        }

        [Test]
        public async Task TurnStartDamageDefeatEndsEncounterBeforeTurnPresentationFact()
        {
            LethalTurnStartAdapter adapter = new LethalTurnStartAdapter(Hero);
            RecordingTurnStartAdapter afterLethal = new RecordingTurnStartAdapter("after-lethal");
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                turnStartAdapters: new IEncounterTurnStartAdapter[] { adapter, afterLethal }
            );
            CountingFactObserver<EncounterOutcomeCommittedFact> ended =
                new CountingFactObserver<EncounterOutcomeCommittedFact>();
            CountingFactObserver<TurnBeganFact> began = new CountingFactObserver<TurnBeganFact>();
            dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(ended);
            dispatcher.RegisterFactObserver<TurnBeganFact>(began);

            OpResult<EncounterStartOutcome> result = await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );

            EncounterState returned = Resolved(result).Value.State;
            EncounterState state = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(adapter.Calls, Is.EqualTo(1));
            Assert.That(afterLethal.Actors, Is.Empty);
            Assert.That(dispatcher.Snapshot.Health[Hero].Current, Is.Zero);
            Assert.That(state.Phase, Is.EqualTo(EncounterPhase.Ended));
            Assert.That(state.Outcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
            Assert.That(returned, Is.SameAs(state));
            Assert.That(began.Calls, Is.Zero);
            Assert.That(ended.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task StartReturnsSettledStateAfterLethalAdapterAdvancesToLivingAlly()
        {
            LethalTurnStartAdapter lethal = new LethalTurnStartAdapter(Hero);
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 15, 10),
                turnStartAdapters: new[] { lethal }
            );

            EncounterState returned = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Reinforcement, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            EncounterState settled = dispatcher.Snapshot.Encounters[Encounter];

            Assert.That(lethal.Calls, Is.EqualTo(1));
            Assert.That(returned, Is.SameAs(settled));
            Assert.That(returned.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(returned.CurrentTurn.Value.Actor, Is.EqualTo(Reinforcement));
            Assert.That(returned.Round, Is.EqualTo(RoundNumber.First));
            Assert.That(
                returned.Roster.Select(entry => entry.Creature),
                Is.EqualTo(new[] { Hero, Reinforcement, Enemy })
            );
        }

        [Test]
        public async Task EndTurnReturnsSettledStateAfterLethalAdapterAdvancesAgain()
        {
            LethalTurnStartAdapter lethal = new LethalTurnStartAdapter(Reinforcement);
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 15, 10),
                turnStartAdapters: new[] { lethal }
            );
            EncounterState heroTurn = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Reinforcement, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;

            EncounterState returned = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(heroTurn.CurrentTurn.Value))
            ).Value.State;
            EncounterState settled = dispatcher.Snapshot.Encounters[Encounter];

            Assert.That(lethal.Calls, Is.EqualTo(1));
            Assert.That(returned, Is.SameAs(settled));
            Assert.That(returned.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(returned.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
            Assert.That(returned.Round, Is.EqualTo(RoundNumber.First));
            Assert.That(
                returned.Roster.Select(entry => entry.Creature),
                Is.EqualTo(new[] { Hero, Reinforcement, Enemy })
            );
        }

        [Test]
        public async Task TurnStartDefeatReactionSettlesBeforeOutcomeAndSkipsRescuedActor()
        {
            RuleDefinitionId definition = new RuleDefinitionId("turn-start-rescue-reaction");
            RescueListener rescue = new RescueListener();
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition).FactListener(RuleLifecyclePhase.Reaction, rescue);
            RulesStateSeed seed = BaseSeed()
                .SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId("turn-start-rescue-binding"),
                        definition,
                        Hero,
                        null,
                        Source,
                        1
                    )
                );
            LethalTurnStartAdapter lethal = new LethalTurnStartAdapter(Hero);
            RecordingTurnStartAdapter afterLethal = new RecordingTurnStartAdapter("after-lethal");
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                seed,
                registryBuilder.Build(),
                turnStartAdapters: new IEncounterTurnStartAdapter[] { lethal, afterLethal }
            );
            TurnBeganActorsObserver began = new TurnBeganActorsObserver();
            CountingFactObserver<InitiativeTurnStartSkippedFact> skipped =
                new CountingFactObserver<InitiativeTurnStartSkippedFact>();
            dispatcher.RegisterFactObserver<TurnBeganFact>(began);
            dispatcher.RegisterFactObserver<InitiativeTurnStartSkippedFact>(skipped);

            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );

            EncounterState state = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(lethal.Calls, Is.EqualTo(1));
            Assert.That(rescue.Calls, Is.EqualTo(1));
            Assert.That(skipped.Calls, Is.EqualTo(1));
            Assert.That(dispatcher.Snapshot.Health[Hero].Current, Is.EqualTo(1));
            Assert.That(
                dispatcher.Snapshot.ActionEconomy[Hero],
                Is.EqualTo(new ActionEconomyState(0, false))
            );
            Assert.That(state.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(state.Outcome, Is.Null);
            Assert.That(state.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
            Assert.That(afterLethal.Actors, Is.EqualTo(new[] { Enemy }));
            Assert.That(began.Actors, Is.EqualTo(new[] { Enemy }));
        }

        [Test]
        public async Task DefeatedActiveActorClosesThroughFullEndTurnLifecycleOnce()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10));
            CountingFactObserver<TurnEndedFact> turnsEnded =
                new CountingFactObserver<TurnEndedFact>();
            dispatcher.RegisterFactObserver<TurnEndedFact>(turnsEnded);
            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );

            await dispatcher.Dispatch(
                new ApplyDamageOp(Hero, 10, new HealthChangeOriginId("forced-turn-close"), Source)
            );

            EncounterState state = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(state.Phase, Is.EqualTo(EncounterPhase.Ended));
            Assert.That(state.Outcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
            Assert.That(dispatcher.Snapshot.MovementBudgets.Contains(Hero), Is.False);
            Assert.That(turnsEnded.Calls, Is.EqualTo(1));
            Assert.That(
                dispatcher.Trace.OrderedFrames.Count(frame => frame.OpType == typeof(EndTurnOp)),
                Is.EqualTo(1)
            );
            Assert.That(
                dispatcher.Trace.OrderedFrames.Count(frame => frame.OpType == typeof(TurnEndingOp)),
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task NonterminalDefeatCommitsOnceAndReleasesAuthoritativePosition()
        {
            RulesStateSeed seed = BaseSeed().SeedPosition(Enemy, new GridPosition(2, 0, 1));
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10, 5), seed);
            CountingFactObserver<CreatureDefeatCommittedFact> committed =
                new CountingFactObserver<CreatureDefeatCommittedFact>();
            dispatcher.RegisterFactObserver<CreatureDefeatCommittedFact>(committed);
            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0),
                    new EncounterParticipant(Reinforcement, Enemies, 0)
                )
            );

            await dispatcher.Dispatch(
                new ApplyDamageOp(Enemy, 10, new HealthChangeOriginId("nonterminal-defeat"), Source)
            );
            await dispatcher.Dispatch(
                new ApplyDamageOp(
                    Enemy,
                    1,
                    new HealthChangeOriginId("repeated-zero-damage"),
                    Source
                )
            );

            HealthState health = dispatcher.Snapshot.Health[Enemy];
            EncounterState encounter = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(health.Current, Is.Zero);
            Assert.That(health.IsCommittedDefeated, Is.True);
            Assert.That(dispatcher.Snapshot.Positions.Contains(Enemy), Is.False);
            Assert.That(committed.Calls, Is.EqualTo(1));
            Assert.That(encounter.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(encounter.Outcome, Is.Null);
        }

        [Test]
        public async Task HigherInitiativeReinforcementWaitsUntilNextRound()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10, 20),
                JoinSeed()
            );
            EncounterState heroTurn = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            EncounterState joined = Resolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                new HealthState(10, 10, 0)
                            ),
                        }
                    )
                )
            ).Value.State;
            InitiativeEntry reinforcement = joined.Roster.Single(entry =>
                entry.Creature == Reinforcement
            );
            EncounterState enemyTurn = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(heroTurn.CurrentTurn.Value))
            ).Value.State;
            EncounterState reinforcementTurn = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(enemyTurn.CurrentTurn.Value))
            ).Value.State;

            Assert.That(reinforcement.EligibleFromRound.Value, Is.EqualTo(2));
            Assert.That(reinforcementTurn.Round.Value, Is.EqualTo(2));
            Assert.That(reinforcementTurn.CurrentTurn.Value.Actor, Is.EqualTo(Reinforcement));
        }

        /// <summary>Verifies every join-owned base slice rejects before the draft commits.</summary>
        [TestCase(JoinRegistrationCollision.Creature)]
        [TestCase(JoinRegistrationCollision.PreparedInputs)]
        [TestCase(JoinRegistrationCollision.Health)]
        [TestCase(JoinRegistrationCollision.Position)]
        [TestCase(JoinRegistrationCollision.LandSpeed)]
        [TestCase(JoinRegistrationCollision.ActionEconomy)]
        [TestCase(JoinRegistrationCollision.MultipleAttackPenalty)]
        [TestCase(JoinRegistrationCollision.SpellSlot)]
        [TestCase(JoinRegistrationCollision.RuleBinding)]
        public async Task JoinRegistrationCollisionRejectsBeforeAnyStateMutation(
            JoinRegistrationCollision collision
        )
        {
            SpellSlotPoolId slotId = new SpellSlotPoolId("reinforcement-slot");
            BindingId bindingId = new BindingId("reinforcement-binding");
            RuleDefinitionId definitionId = new RuleDefinitionId("reinforcement-rule");
            ActiveRuleBinding binding = new ActiveRuleBinding(
                bindingId,
                definitionId,
                Reinforcement,
                null,
                Source,
                0,
                false
            );
            CombatantRulesState registration = new CombatantRulesState(
                new CreatureState(Reinforcement, Enemies),
                new HealthState(10, 10),
                new GridPosition(3, 0, 2),
                new GridDistance(25),
                PreparedCreatureInputs.Empty,
                new[] { new SpellSlotState(slotId, Reinforcement, 1, 1) },
                new[] { binding }
            );
            RulesStateSeed seed = JoinSeed();
            switch (collision)
            {
                case JoinRegistrationCollision.Creature:
                    seed.SeedCreature(new CreatureState(Reinforcement, Enemies));
                    break;
                case JoinRegistrationCollision.PreparedInputs:
                    seed.SeedPreparedInputs(Reinforcement, PreparedCreatureInputs.Empty);
                    break;
                case JoinRegistrationCollision.Health:
                    seed.SeedHealth(Reinforcement, new HealthState(7, 10));
                    break;
                case JoinRegistrationCollision.Position:
                    seed.SeedPosition(Reinforcement, new GridPosition(9, 0, 9));
                    break;
                case JoinRegistrationCollision.LandSpeed:
                    seed.SeedLandSpeed(Reinforcement, new GridDistance(30));
                    break;
                case JoinRegistrationCollision.ActionEconomy:
                    seed.SeedActionEconomy(Reinforcement, new ActionEconomyState(2, true));
                    break;
                case JoinRegistrationCollision.MultipleAttackPenalty:
                    seed.SeedMultipleAttackPenalty(
                        Reinforcement,
                        new MultipleAttackPenaltyState(1)
                    );
                    break;
                case JoinRegistrationCollision.SpellSlot:
                    seed.SeedSpellSlot(new SpellSlotState(slotId, Reinforcement, 0, 1));
                    break;
                case JoinRegistrationCollision.RuleBinding:
                    seed.SeedRuleBinding(binding);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(collision), collision, null);
            }

            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(15, 10), seed);
            EncounterState active = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            RulesSnapshot before = dispatcher.Snapshot;
            InitiativeEntry addition = new InitiativeEntry(
                Reinforcement,
                Enemies,
                12,
                0,
                active.Roster.Count,
                RoundNumber.First
            );

            OpResult<OpResult<EncounterJoinOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterJoinWorkflowOp(
                    new CommitEncounterJoinOp(
                        Encounter,
                        Array.AsReadOnly(new[] { addition }),
                        new Dictionary<CreatureId, CombatantRulesState>
                        {
                            [Reinforcement] = registration,
                        }
                    )
                )
            );
            OpResult<EncounterJoinOutcome> result = Resolved(workflow).Value;

            Assert.That(result, Is.TypeOf<InvalidOpResult<EncounterJoinOutcome>>());
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Roster,
                Is.EqualTo(before.Encounters[Encounter].Roster)
            );
            Assert.That(
                dispatcher.Snapshot.Creatures.Count(),
                Is.EqualTo(before.Creatures.Count())
            );
            Assert.That(
                dispatcher.Snapshot.PreparedInputs.Count(),
                Is.EqualTo(before.PreparedInputs.Count())
            );
            Assert.That(dispatcher.Snapshot.Health.Count(), Is.EqualTo(before.Health.Count()));
            Assert.That(
                dispatcher.Snapshot.Positions.Count(),
                Is.EqualTo(before.Positions.Count())
            );
            Assert.That(
                dispatcher.Snapshot.LandSpeeds.Count(),
                Is.EqualTo(before.LandSpeeds.Count())
            );
            Assert.That(
                dispatcher.Snapshot.ActionEconomy.Count(),
                Is.EqualTo(before.ActionEconomy.Count())
            );
            Assert.That(
                dispatcher.Snapshot.MultipleAttackPenalty.Count(),
                Is.EqualTo(before.MultipleAttackPenalty.Count())
            );
            Assert.That(
                dispatcher.Snapshot.SpellSlots.Count(),
                Is.EqualTo(before.SpellSlots.Count())
            );
            Assert.That(
                dispatcher.Snapshot.RuleBindings.Count(),
                Is.EqualTo(before.RuleBindings.Count())
            );
        }

        [Test]
        public void ReinforcementRegistrationAdvancesStatelessBindingGenerationHistory()
        {
            BindingId bindingId = new BindingId("reinforcement-generation-binding");
            SpellSlotPoolId slotId = new SpellSlotPoolId("reinforcement-generation-slot");
            InitiativeEntry hero = Entry(Hero, Players, 15, 0);
            EncounterState active = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                new[] { hero },
                0,
                new TurnIdentity(Encounter, new TurnId(1), Hero, RoundNumber.First, 0),
                2,
                null
            );
            InitiativeEntry addition = new InitiativeEntry(
                Reinforcement,
                Enemies,
                10,
                0,
                1,
                RoundNumber.First
            );
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedEncounter(active)
                    .SeedStatelessRuleBindingGeneration(bindingId, 3)
            );
            CommitEncounterJoinReducer reducer = new CommitEncounterJoinReducer(
                new RuleRegistryBuilder().Build()
            );

            ReductionResult<EncounterJoinOutcome> stale = Commit(
                CreateJoinRegistration(Reinforcement, slotId, bindingId, 3)
            );
            Assert.That(stale.IsRejected, Is.True);
            Assert.That(stale.Facts, Is.Empty);
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
            Assert.That(store.Snapshot.StatelessRuleBindingGenerations[bindingId], Is.EqualTo(3));

            ReductionResult<EncounterJoinOutcome> current = Commit(
                CreateJoinRegistration(Reinforcement, slotId, bindingId, 4)
            );
            Assert.That(current.IsAccepted, Is.True);
            Assert.That(current.Snapshot.RuleBindings[bindingId].CreationOrder, Is.EqualTo(4));
            Assert.That(current.Snapshot.StatelessRuleBindingGenerations[bindingId], Is.EqualTo(4));

            ReductionResult<EncounterJoinOutcome> Commit(CombatantRulesState registration) =>
                store.Reduce(
                    new ReductionContext<CommitEncounterJoinOp>(
                        new CommitEncounterJoinOp(
                            Encounter,
                            Array.AsReadOnly(new[] { addition }),
                            new Dictionary<CreatureId, CombatantRulesState>
                            {
                                [Reinforcement] = registration,
                            }
                        ),
                        new OpId(2),
                        new OpId(1),
                        Source
                    ),
                    reducer
                );
        }

        /// <summary>Verifies feature IDs must also be unique across one reinforcement batch.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task JoinRegistrationRejectsIdentifiersDuplicatedAcrossReinforcements(
            bool duplicateSpellSlot
        )
        {
            SpellSlotPoolId sharedSlot = new SpellSlotPoolId("shared-reinforcement-slot");
            BindingId sharedBinding = new BindingId("shared-reinforcement-binding");
            CombatantRulesState first = CreateJoinRegistration(
                Reinforcement,
                sharedSlot,
                duplicateSpellSlot ? new BindingId("first-reinforcement-binding") : sharedBinding
            );
            CombatantRulesState second = CreateJoinRegistration(
                SecondReinforcement,
                duplicateSpellSlot ? sharedSlot : new SpellSlotPoolId("second-reinforcement-slot"),
                duplicateSpellSlot ? new BindingId("second-reinforcement-binding") : sharedBinding
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10),
                JoinSeed()
            );
            EncounterState active = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            InitiativeEntry[] additions =
            {
                new InitiativeEntry(
                    Reinforcement,
                    Enemies,
                    12,
                    0,
                    active.Roster.Count,
                    RoundNumber.First
                ),
                new InitiativeEntry(
                    SecondReinforcement,
                    Enemies,
                    11,
                    0,
                    active.Roster.Count + 1,
                    RoundNumber.First
                ),
            };
            RulesSnapshot before = dispatcher.Snapshot;

            OpResult<OpResult<EncounterJoinOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterJoinWorkflowOp(
                    new CommitEncounterJoinOp(
                        Encounter,
                        Array.AsReadOnly(additions),
                        new Dictionary<CreatureId, CombatantRulesState>
                        {
                            [Reinforcement] = first,
                            [SecondReinforcement] = second,
                        }
                    )
                )
            );

            Assert.That(
                Resolved(workflow).Value,
                Is.TypeOf<InvalidOpResult<EncounterJoinOutcome>>()
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Roster,
                Is.EqualTo(before.Encounters[Encounter].Roster)
            );
        }

        [Test]
        public async Task JoinAdoptsPreparedEffectsInOneRosterTransaction()
        {
            RuleDefinitionId conditionDefinition = new RuleDefinitionId(
                "atomic-condition-definition"
            );
            RuleDefinitionId spellDefinition = new RuleDefinitionId("atomic-spell-definition");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(conditionDefinition);
            registryBuilder.Define(spellDefinition);
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10),
                JoinSeed(),
                registry
            );
            EncounterState active = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            ActiveEffectRegistration condition = CreateActiveJoinRegistration(
                "atomic-condition",
                conditionDefinition,
                Reinforcement,
                Reinforcement,
                20,
                EffectDuration.Rounds(2),
                new ActiveEffectTimingState(
                    new ActiveEffectId("atomic-condition-effect"),
                    Encounter,
                    new BindingId("atomic-condition-binding"),
                    Reinforcement,
                    2,
                    false,
                    20
                )
            );
            ActiveEffectRegistration spell = CreateActiveJoinRegistration(
                "atomic-spell",
                spellDefinition,
                Reinforcement,
                Hero,
                21,
                EffectDuration.Rounds(3)
            );
            CombatantRulesState registration = CreateActiveJoinState(
                Reinforcement,
                condition,
                spell
            );
            InitiativeEntry addition = new InitiativeEntry(
                Reinforcement,
                Enemies,
                12,
                0,
                active.Roster.Count,
                RoundNumber.First
            );
            long beforeVersion = dispatcher.Snapshot.Version;

            OpResult<EncounterJoinOutcome> result = Resolved(
                await dispatcher.Dispatch(
                    new CommitEncounterJoinWorkflowOp(
                        new CommitEncounterJoinOp(
                            Encounter,
                            new[] { addition },
                            new Dictionary<CreatureId, CombatantRulesState>
                            {
                                [Reinforcement] = registration,
                            }
                        )
                    )
                )
            ).Value;
            ResolvedOpResult<EncounterJoinOutcome> joined = Resolved(result);

            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(beforeVersion + 1));
            Assert.That(joined.Facts.OfType<ActiveEffectAdoptedFact>().Count(), Is.EqualTo(2));
            Assert.That(joined.Facts.OfType<ActiveEffectCreatedFact>(), Is.Empty);
            Assert.That(joined.Facts.Last(), Is.TypeOf<EncounterJoinedFact>());
            Assert.That(
                dispatcher
                    .Snapshot.Encounters[Encounter]
                    .Roster.Any(entry => entry.Creature == Reinforcement),
                Is.True
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings[condition.Effect.Id],
                Is.EqualTo(condition.Timing)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings[spell.Effect.Id].RemainingBoundaries,
                Is.EqualTo(3)
            );
            ActiveEffectAdoptedFact adopted = joined
                .Facts.OfType<ActiveEffectAdoptedFact>()
                .First();
            Assert.That(adopted.Source, Is.Not.EqualTo(adopted.Effect.Source));
            Assert.That(adopted.Effect.Source, Is.EqualTo(condition.Effect.Source));
        }

        [TestCase("effect")]
        [TestCase("binding")]
        [TestCase("timing")]
        public async Task ExistingActiveEffectSliceCollisionRejectsWholeJoinDraft(string collision)
        {
            RuleDefinitionId definition = new RuleDefinitionId("collision-definition");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            ActiveEffectRegistration incoming = CreateActiveJoinRegistration(
                "existing-collision",
                definition,
                Reinforcement,
                Reinforcement,
                30,
                EffectDuration.Rounds(2)
            );
            RulesStateSeed seed = JoinSeed().SeedEncounter(ActiveTurnEncounter());
            if (collision == "effect")
                seed.SeedActiveEffect(incoming.Effect);
            else if (collision == "binding")
                seed.SeedRuleBinding(incoming.Binding);
            else
                seed.SeedActiveEffectTiming(
                    new ActiveEffectTimingState(
                        incoming.Effect.Id,
                        Encounter,
                        incoming.Binding.Id,
                        incoming.Effect.SourceCreature,
                        1,
                        false,
                        incoming.Binding.CreationOrder
                    )
                );
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed, registry);
            RulesSnapshot before = dispatcher.Snapshot;
            CombatantRulesState registration = CreateActiveJoinState(Reinforcement, incoming);

            OpResult<OpResult<EncounterJoinOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterJoinWorkflowOp(
                    new CommitEncounterJoinOp(
                        Encounter,
                        new[]
                        {
                            new InitiativeEntry(Reinforcement, Enemies, 8, 0, 2, RoundNumber.First),
                        },
                        new Dictionary<CreatureId, CombatantRulesState>
                        {
                            [Reinforcement] = registration,
                        }
                    )
                )
            );

            Assert.That(
                Resolved(workflow).Value,
                Is.TypeOf<InvalidOpResult<EncounterJoinOutcome>>()
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Roster,
                Is.EqualTo(before.Encounters[Encounter].Roster)
            );
            Assert.That(dispatcher.Snapshot.Creatures.Contains(Reinforcement), Is.False);
            Assert.That(
                dispatcher.Snapshot.ActiveEffects.Count(),
                Is.EqualTo(before.ActiveEffects.Count())
            );
            Assert.That(
                dispatcher.Snapshot.RuleBindings.Count(),
                Is.EqualTo(before.RuleBindings.Count())
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings.Count(),
                Is.EqualTo(before.ActiveEffectTimings.Count())
            );
        }

        [TestCase("effect")]
        [TestCase("binding")]
        [TestCase("timing")]
        public async Task SameBatchActiveEffectCollisionRejectsWholeJoinDraft(string collision)
        {
            RuleDefinitionId definition = new RuleDefinitionId("batch-effect-definition");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            string firstIdentity = collision == "binding" ? "batch-first" : "batch-shared";
            string secondIdentity =
                collision == "effect" || collision == "timing" ? "batch-shared" : "batch-second";
            ActiveEffectRegistration first = CreateActiveJoinRegistration(
                firstIdentity,
                definition,
                Reinforcement,
                Reinforcement,
                40,
                EffectDuration.Rounds(2)
            );
            ActiveEffectRegistration second = CreateActiveJoinRegistration(
                secondIdentity,
                definition,
                SecondReinforcement,
                SecondReinforcement,
                41,
                EffectDuration.Rounds(2)
            );
            if (collision == "binding")
            {
                second = new ActiveEffectRegistration(
                    second.Effect,
                    new ActiveRuleBinding(
                        first.Binding.Id,
                        definition,
                        SecondReinforcement,
                        second.Effect.Id,
                        second.Effect.Source,
                        second.Binding.CreationOrder
                    )
                );
            }
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(),
                JoinSeed().SeedEncounter(ActiveTurnEncounter()),
                registry
            );
            RulesSnapshot before = dispatcher.Snapshot;

            OpResult<OpResult<EncounterJoinOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterJoinWorkflowOp(
                    new CommitEncounterJoinOp(
                        Encounter,
                        new[]
                        {
                            Entry(Reinforcement, Enemies, 8, 2),
                            Entry(SecondReinforcement, Enemies, 7, 3),
                        },
                        new Dictionary<CreatureId, CombatantRulesState>
                        {
                            [Reinforcement] = CreateActiveJoinState(Reinforcement, first),
                            [SecondReinforcement] = CreateActiveJoinState(
                                SecondReinforcement,
                                second
                            ),
                        }
                    )
                )
            );

            Assert.That(
                Resolved(workflow).Value,
                Is.TypeOf<InvalidOpResult<EncounterJoinOutcome>>()
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Roster,
                Is.EqualTo(before.Encounters[Encounter].Roster)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(dispatcher.Snapshot.RuleBindings, Is.Empty);
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings, Is.Empty);
        }

        [TestCase("unknown")]
        [TestCase("timing")]
        public async Task InvalidPreparedActiveEffectRejectsWholeJoinDraft(string invalidity)
        {
            RuleDefinitionId defined = new RuleDefinitionId("valid-join-effect");
            RuleDefinitionId selected =
                invalidity == "unknown" ? new RuleDefinitionId("unknown-join-effect") : defined;
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(defined);
            RuleRegistry registry = registryBuilder.Build();
            ActiveEffectRegistration prepared = CreateActiveJoinRegistration(
                "invalid-prepared",
                selected,
                Reinforcement,
                Reinforcement,
                50,
                EffectDuration.Rounds(2),
                invalidity == "timing"
                    ? new ActiveEffectTimingState(
                        new ActiveEffectId("invalid-prepared-effect"),
                        new EncounterId("other-encounter"),
                        new BindingId("invalid-prepared-binding"),
                        Reinforcement,
                        2,
                        false,
                        50
                    )
                    : null
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(),
                JoinSeed().SeedEncounter(ActiveTurnEncounter()),
                registry
            );
            RulesSnapshot before = dispatcher.Snapshot;

            OpResult<OpResult<EncounterJoinOutcome>> workflow = await dispatcher.Dispatch(
                new CommitEncounterJoinWorkflowOp(
                    new CommitEncounterJoinOp(
                        Encounter,
                        new[] { Entry(Reinforcement, Enemies, 8, 2) },
                        new Dictionary<CreatureId, CombatantRulesState>
                        {
                            [Reinforcement] = CreateActiveJoinState(Reinforcement, prepared),
                        }
                    )
                )
            );

            Assert.That(
                Resolved(workflow).Value,
                Is.TypeOf<InvalidOpResult<EncounterJoinOutcome>>()
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Roster,
                Is.EqualTo(before.Encounters[Encounter].Roster)
            );
            Assert.That(dispatcher.Snapshot.Creatures.Contains(Reinforcement), Is.False);
            Assert.That(dispatcher.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(dispatcher.Snapshot.RuleBindings, Is.Empty);
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings, Is.Empty);
        }

        [Test]
        public void CombatantRegistrationRejectsActiveEffectOwnedByAnotherCreature()
        {
            RuleDefinitionId definition = new RuleDefinitionId("wrong-owner-effect");
            ActiveEffectRegistration prepared = CreateActiveJoinRegistration(
                "wrong-owner",
                definition,
                Hero,
                Reinforcement,
                60,
                EffectDuration.Indefinite
            );

            Assert.Throws<ArgumentException>(() => CreateActiveJoinState(Reinforcement, prepared));
        }

        [Test]
        public async Task ExactJoinReplayAcceptsStructurallyRecreatedActiveEffectReceipt()
        {
            RuleDefinitionId definition = new RuleDefinitionId("recreated-replay-definition");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10, 8),
                JoinSeed(),
                registry
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );
            ActiveEffectTimingState timing = new ActiveEffectTimingState(
                new ActiveEffectId("recreated-replay-effect"),
                Encounter,
                new BindingId("recreated-replay-binding"),
                Reinforcement,
                2,
                false,
                70
            );
            ActiveEffectRegistration original = CreateActiveJoinRegistration(
                "recreated-replay",
                definition,
                Reinforcement,
                Reinforcement,
                70,
                EffectDuration.Rounds(2),
                timing
            );
            Resolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                CreateActiveJoinState(Reinforcement, original)
                            ),
                        }
                    )
                )
            );
            ActiveEffectInstance recreatedEffect = new ActiveEffectInstance(
                original.Effect.Id,
                original.Effect.DefinitionId,
                original.Effect.SourceCreature,
                original.Effect.Source,
                original.Effect.Duration,
                original.Effect.State,
                original.Effect.EffectStateVersion,
                original.Effect.Status
            );
            ActiveRuleBinding recreatedBinding = new ActiveRuleBinding(
                original.Binding.Id,
                original.Binding.DefinitionId,
                original.Binding.Owner,
                original.Binding.EffectId,
                original.Binding.Source,
                original.Binding.CreationOrder,
                original.Binding.IsEnabled
            );
            ActiveEffectTimingState recreatedTiming = new ActiveEffectTimingState(
                original.Timing.Effect,
                original.Timing.Encounter,
                original.Timing.Binding,
                original.Timing.SourceCreature,
                original.Timing.RemainingBoundaries,
                original.Timing.ExpiresWithEncounter,
                original.Timing.CreationOrder
            );
            ActiveEffectRegistration recreated = new ActiveEffectRegistration(
                recreatedEffect,
                recreatedBinding,
                recreatedTiming
            );
            long committedVersion = dispatcher.Snapshot.Version;

            Resolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                CreateActiveJoinState(Reinforcement, recreated)
                            ),
                        }
                    )
                )
            );

            Assert.That(recreated, Is.Not.EqualTo(original));
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
        }

        [Test]
        public async Task ExactJoinReplayAcceptsSeparatelyReconstructedPreparedInputs()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10, 8),
                JoinSeed()
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );
            PreparedCreatureInputs originalInputs = CreateReplayPreparedInputs(7);
            JoinEncounterOp original = new JoinEncounterOp(
                Encounter,
                new[]
                {
                    new EncounterJoinParticipant(
                        new EncounterParticipant(Reinforcement, Enemies, 0),
                        CreateJoinStateWithPreparedInputs(Reinforcement, originalInputs)
                    ),
                }
            );
            Resolved(await dispatcher.Dispatch(original));
            long committedVersion = dispatcher.Snapshot.Version;
            PreparedCreatureInputs reconstructedInputs = CreateReplayPreparedInputs(7);
            JoinEncounterOp replay = new JoinEncounterOp(
                Encounter,
                new[]
                {
                    new EncounterJoinParticipant(
                        new EncounterParticipant(Reinforcement, Enemies, 0),
                        CreateJoinStateWithPreparedInputs(Reinforcement, reconstructedInputs)
                    ),
                }
            );

            ResolvedOpResult<EncounterJoinOutcome> result = Resolved(
                await dispatcher.Dispatch(replay)
            );

            Assert.That(reconstructedInputs, Is.Not.SameAs(originalInputs));
            Assert.That(reconstructedInputs, Is.EqualTo(originalInputs));
            Assert.That(result.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
        }

        [Test]
        public async Task ExactJoinReplayRejectsOneChangedPreparedField()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10, 8),
                JoinSeed()
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );
            JoinEncounterOp original = new JoinEncounterOp(
                Encounter,
                new[]
                {
                    new EncounterJoinParticipant(
                        new EncounterParticipant(Reinforcement, Enemies, 0),
                        CreateJoinStateWithPreparedInputs(
                            Reinforcement,
                            CreateReplayPreparedInputs(7)
                        )
                    ),
                }
            );
            Resolved(await dispatcher.Dispatch(original));
            long committedVersion = dispatcher.Snapshot.Version;
            JoinEncounterOp changed = new JoinEncounterOp(
                Encounter,
                new[]
                {
                    new EncounterJoinParticipant(
                        new EncounterParticipant(Reinforcement, Enemies, 0),
                        CreateJoinStateWithPreparedInputs(
                            Reinforcement,
                            CreateReplayPreparedInputs(8)
                        )
                    ),
                }
            );

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(changed)
            );

            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(dispatcher.Snapshot.PreparedInputs[Reinforcement].Level, Is.EqualTo(7));
        }

        [TestCase("root")]
        [TestCase("grant-pool")]
        public async Task ExactJoinReplayUsesHiddenRageReceiptIdentity(string changedField)
        {
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(RageActionDefinition.EffectDefinitionId);
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10, 8),
                JoinSeed(),
                registryBuilder.Build()
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );
            ActiveEffectRegistration original = CreateRageJoinRegistration(
                CreateRageJoinReceipt(new OpId(71), 3)
            );
            JoinEncounterOp originalJoin = new JoinEncounterOp(
                Encounter,
                new[]
                {
                    new EncounterJoinParticipant(
                        new EncounterParticipant(Reinforcement, Enemies, 0),
                        CreateActiveJoinState(Reinforcement, original)
                    ),
                }
            );
            Resolved(await dispatcher.Dispatch(originalJoin));
            long committedVersion = dispatcher.Snapshot.Version;
            ActiveEffectRegistration recreated = CreateRageJoinRegistration(
                CreateRageJoinReceipt(new OpId(71), 3)
            );

            Resolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                CreateActiveJoinState(Reinforcement, recreated)
                            ),
                        }
                    )
                )
            );

            RageEffectState changedReceipt =
                changedField == "root"
                    ? CreateRageJoinReceipt(new OpId(72), 3)
                    : CreateRageJoinReceipt(new OpId(71), 4);
            ActiveEffectRegistration changed = CreateRageJoinRegistration(changedReceipt);

            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(changed.Effect, Is.EqualTo(original.Effect));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                CreateActiveJoinState(Reinforcement, changed)
                            ),
                        }
                    )
                )
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(
                ActiveEffectInstanceExactEquality.Equals(
                    dispatcher.Snapshot.ActiveEffects[original.Effect.Id],
                    original.Effect
                ),
                Is.True
            );
        }

        [TestCase("effect")]
        [TestCase("binding")]
        [TestCase("timing")]
        public async Task ExactJoinReplayRejectsChangedActiveEffectReceipt(string changed)
        {
            RuleDefinitionId definition = new RuleDefinitionId("replay-effect-definition");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10, 8),
                JoinSeed(),
                registry
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );
            ActiveEffectTimingState timing = new ActiveEffectTimingState(
                new ActiveEffectId("replay-effect"),
                Encounter,
                new BindingId("replay-binding"),
                Reinforcement,
                2,
                false,
                70
            );
            ActiveEffectRegistration original = CreateActiveJoinRegistration(
                "replay",
                definition,
                Reinforcement,
                Reinforcement,
                70,
                EffectDuration.Rounds(2),
                timing
            );
            CombatantRulesState committed = CreateActiveJoinState(Reinforcement, original);
            Resolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                committed
                            ),
                        }
                    )
                )
            );
            ActiveEffectInstance changedEffect =
                changed == "effect"
                    ? new ActiveEffectInstance(
                        original.Effect.Id,
                        definition,
                        Reinforcement,
                        original.Effect.Source,
                        EffectDuration.Rounds(3),
                        new TestEffectState()
                    )
                    : original.Effect;
            ActiveRuleBinding changedBinding =
                changed == "binding"
                    ? new ActiveRuleBinding(
                        original.Binding.Id,
                        definition,
                        Reinforcement,
                        original.Effect.Id,
                        original.Effect.Source,
                        original.Binding.CreationOrder + 1
                    )
                    : original.Binding;
            ActiveEffectTimingState changedTiming =
                changed == "timing"
                    ? new ActiveEffectTimingState(
                        original.Effect.Id,
                        Encounter,
                        original.Binding.Id,
                        Reinforcement,
                        1,
                        false,
                        original.Binding.CreationOrder
                    )
                : changed == "binding"
                    ? new ActiveEffectTimingState(
                        original.Effect.Id,
                        Encounter,
                        original.Binding.Id,
                        Reinforcement,
                        original.Timing.RemainingBoundaries,
                        false,
                        changedBinding.CreationOrder
                    )
                : original.Timing;
            ActiveEffectRegistration different = new ActiveEffectRegistration(
                changedEffect,
                changedBinding,
                changedTiming
            );
            long committedVersion = dispatcher.Snapshot.Version;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                CreateActiveJoinState(Reinforcement, different)
                            ),
                        }
                    )
                )
            );

            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[original.Effect.Id],
                Is.EqualTo(original.Effect)
            );
            Assert.That(
                dispatcher.Snapshot.RuleBindings[original.Binding.Id],
                Is.EqualTo(original.Binding)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings[original.Effect.Id],
                Is.EqualTo(original.Timing)
            );
        }

        [Test]
        public async Task JoinFactListenerCannotClaimIncomingEffectIdentity()
        {
            RuleDefinitionId incomingDefinition = new RuleDefinitionId("incoming-effect");
            RuleDefinitionId listenerDefinition = new RuleDefinitionId("join-claim-listener");
            ActiveEffectRegistration incoming = CreateActiveJoinRegistration(
                "listener-collision",
                incomingDefinition,
                Reinforcement,
                Reinforcement,
                80,
                EffectDuration.Indefinite
            );
            ActiveEffectRegistration claim = CreateActiveJoinRegistration(
                "listener-collision",
                incomingDefinition,
                Hero,
                Hero,
                81,
                EffectDuration.Indefinite
            );
            ClaimIncomingEffectOnJoinListener listener = new ClaimIncomingEffectOnJoinListener(
                claim
            );
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(incomingDefinition);
            registryBuilder
                .Define(listenerDefinition)
                .FactListener(RuleLifecyclePhase.Observation, listener);
            RuleRegistry registry = registryBuilder.Build();
            RulesStateSeed seed = JoinSeed()
                .SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId("join-claim-listener-binding"),
                        listenerDefinition,
                        Hero,
                        null,
                        Source,
                        0
                    )
                );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(15, 10, 8),
                seed,
                registry
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );

            Resolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Enemies, 0),
                                CreateActiveJoinState(Reinforcement, incoming)
                            ),
                        }
                    )
                )
            );

            Assert.That(listener.Calls, Is.EqualTo(1));
            Assert.That(listener.Result, Is.TypeOf<InvalidOpResult<ActiveEffectAdoptionOutcome>>());
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[incoming.Effect.Id],
                Is.EqualTo(incoming.Effect)
            );
            Assert.That(
                dispatcher.Snapshot.RuleBindings[incoming.Binding.Id],
                Is.EqualTo(incoming.Binding)
            );
            Assert.That(
                dispatcher
                    .Snapshot.Encounters[Encounter]
                    .Roster.Any(entry => entry.Creature == Reinforcement),
                Is.True
            );
        }

        [Test]
        public async Task ProtagonistDefeatWinsZeroLivingTieWithoutDrawState()
        {
            EncounterState active = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                new[] { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) },
                0,
                new TurnIdentity(Encounter, new TurnId(1), Hero, RoundNumber.First, 0),
                2,
                null
            );
            RulesStateSeed seed = BaseSeed()
                .SeedHealth(Hero, new HealthState(0, 10))
                .SeedHealth(Enemy, new HealthState(0, 10))
                .SeedActionEconomy(Hero, new ActionEconomyState(0, false))
                .SeedMultipleAttackPenalty(Hero, new MultipleAttackPenaltyState(0))
                .SeedEncounter(active);
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed);

            EncounterState ended = Resolved(
                await dispatcher.Dispatch(new EvaluateEncounterOutcomeOp(Encounter))
            ).Value.State;

            Assert.That(ended.Outcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
            Assert.That(Enum.GetNames(typeof(EncounterOutcome)), Does.Not.Contain("Draw"));
        }

        [Test]
        public void OutcomeEvaluationRejectsRosterParticipantMissingHealthWithoutCommit()
        {
            EncounterState active = ActiveTurnEncounter();
            RulesStateSeed seed = new RulesStateSeed()
                .SeedHealth(Hero, new HealthState(10, 10))
                .SeedEncounter(active);
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed);
            RulesSnapshot before = dispatcher.Snapshot;

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new EvaluateEncounterOutcomeOp(Encounter))
            );

            Assert.That(error.Message, Does.Contain("no authoritative health state"));
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(dispatcher.Snapshot.Encounters[Encounter], Is.EqualTo(active));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TurnEndRejectsMissingRequiredTurnSliceWithoutCommit(bool omitActionEconomy)
        {
            EncounterState active = ActiveTurnEncounter();
            RulesStateSeed seed = BaseSeed().SeedEncounter(active);
            if (omitActionEconomy)
                seed.SeedMultipleAttackPenalty(Hero, new MultipleAttackPenaltyState(0));
            else
                seed.SeedActionEconomy(Hero, new ActionEconomyState(3, true));
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed);
            RulesSnapshot before = dispatcher.Snapshot;

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new EndTurnOp(active.CurrentTurn.Value))
            );

            Assert.That(error.Message, Does.Contain("no authoritative"));
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(dispatcher.Snapshot.Encounters[Encounter], Is.EqualTo(active));
        }

        [Test]
        public async Task HealthBatchOutcomeWaitsForAllTargetsAndIgnoresProcessingOrder()
        {
            foreach (bool reverse in new[] { false, true })
            {
                RuleDispatcher dispatcher = CreateDispatcher(
                    new ScriptedRollService(20, 10),
                    BaseSeed()
                        .SeedHealth(Hero, new HealthState(1, 10))
                        .SeedHealth(Enemy, new HealthState(1, 10))
                        .SeedPosition(Hero, new GridPosition(0, 0, 0))
                        .SeedPosition(Enemy, new GridPosition(1, 0, 0))
                );
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                );
                BatchOutcomeObserver observer = new BatchOutcomeObserver(Hero, Enemy);
                dispatcher.RegisterFactObserver<CreatureReducedToZeroFact>(observer);
                dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(observer);
                HealthBatchChange hero = new HealthBatchChange(
                    HealthBatchChangeKind.Damage,
                    Hero,
                    1,
                    new HealthChangeOriginId($"batch-hero-{reverse}"),
                    Source
                );
                HealthBatchChange enemy = new HealthBatchChange(
                    HealthBatchChangeKind.Damage,
                    Enemy,
                    1,
                    new HealthChangeOriginId($"batch-enemy-{reverse}"),
                    Source
                );
                HealthBatchChange[] changes = reverse
                    ? new[] { enemy, hero }
                    : new[] { hero, enemy };

                HealthBatchOutcome batch = Resolved(
                    await dispatcher.Dispatch(new ApplyHealthBatchOp(changes))
                ).Value;

                EncounterState settled = dispatcher.Snapshot.Encounters[Encounter];
                Assert.That(batch.Changes.Select(change => change.Change), Is.EqualTo(changes));
                Assert.That(
                    batch.Changes.Select(change => change.Applied),
                    Is.EqualTo(new[] { 1, 1 })
                );
                Assert.That(dispatcher.Snapshot.Health[Hero].Current, Is.Zero);
                Assert.That(dispatcher.Snapshot.Health[Enemy].Current, Is.Zero);
                Assert.That(dispatcher.Snapshot.Health[Hero].IsCommittedDefeated, Is.True);
                Assert.That(dispatcher.Snapshot.Health[Enemy].IsCommittedDefeated, Is.True);
                Assert.That(dispatcher.Snapshot.Positions.Contains(Hero), Is.False);
                Assert.That(dispatcher.Snapshot.Positions.Contains(Enemy), Is.False);
                Assert.That(settled.Phase, Is.EqualTo(EncounterPhase.Ended));
                Assert.That(settled.Outcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
                Assert.That(
                    observer.Order.Take(2).All(value => value.StartsWith("zero:")),
                    Is.True
                );
                Assert.That(observer.Order.Last(), Is.EqualTo("ended"));
                Assert.That(observer.HeroAtOutcome, Is.Zero);
                Assert.That(observer.EnemyAtOutcome, Is.Zero);
                Assert.That(observer.EndCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ReactionCausalHealingSettlesBeforeOutcomeObservation()
        {
            RuleDefinitionId definition = new RuleDefinitionId("rescue-reaction");
            RescueListener rescue = new RescueListener();
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition).FactListener(RuleLifecyclePhase.Reaction, rescue);
            RuleRegistry registry = registryBuilder.Build();
            RulesStateSeed seed = BaseSeed()
                .SeedPosition(Hero, new GridPosition(0, 0, 0))
                .SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId("rescue-binding"),
                        definition,
                        Hero,
                        null,
                        Source,
                        1
                    )
                );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                seed,
                registry
            );
            CountingFactObserver<CreatureDefeatCommittedFact> committed =
                new CountingFactObserver<CreatureDefeatCommittedFact>();
            dispatcher.RegisterFactObserver<CreatureDefeatCommittedFact>(committed);
            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );

            await dispatcher.Dispatch(
                new ApplyDamageOp(Hero, 10, new HealthChangeOriginId("lethal-hit"), Source)
            );

            EncounterState state = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(rescue.Calls, Is.EqualTo(1));
            Assert.That(dispatcher.Snapshot.Health[Hero].Current, Is.EqualTo(1));
            Assert.That(dispatcher.Snapshot.Health[Hero].IsCommittedDefeated, Is.False);
            Assert.That(dispatcher.Snapshot.Positions.Contains(Hero), Is.True);
            Assert.That(committed.Calls, Is.Zero);
            Assert.That(state.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(state.Outcome, Is.Null);
        }

        [Test]
        public async Task EncounterEndFinalizesZeroCreatureAfterOutcomeBindingRemoval()
        {
            RuleDefinitionId definition = new RuleDefinitionId("lethal-cascade-reaction");
            LethalCascadeListener cascade = new LethalCascadeListener(Hero, Enemy);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition).FactListener(RuleLifecyclePhase.Reaction, cascade);
            RuleRegistry registry = registryBuilder.Build();
            RulesStateSeed seed = BaseSeed()
                .SeedPosition(Hero, new GridPosition(0, 0, 0))
                .SeedPosition(Enemy, new GridPosition(1, 0, 0))
                .SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId("lethal-cascade-binding"),
                        definition,
                        Hero,
                        null,
                        Source,
                        1
                    )
                );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                seed,
                registry
            );
            CountingFactObserver<CreatureDefeatCommittedFact> committed =
                new CountingFactObserver<CreatureDefeatCommittedFact>();
            dispatcher.RegisterFactObserver<CreatureDefeatCommittedFact>(committed);
            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );

            await dispatcher.Dispatch(
                new ApplyDamageOp(Hero, 10, new HealthChangeOriginId("cascade-trigger"), Source)
            );

            EncounterState ended = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(cascade.Calls, Is.EqualTo(1));
            Assert.That(ended.Phase, Is.EqualTo(EncounterPhase.Ended));
            Assert.That(ended.Outcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
            Assert.That(dispatcher.Snapshot.Health[Hero].IsCommittedDefeated, Is.True);
            Assert.That(dispatcher.Snapshot.Health[Enemy].IsCommittedDefeated, Is.True);
            Assert.That(dispatcher.Snapshot.Positions.Contains(Hero), Is.False);
            Assert.That(dispatcher.Snapshot.Positions.Contains(Enemy), Is.False);
            Assert.That(committed.Calls, Is.EqualTo(2));
        }

        [Test]
        public async Task IncorrectEndOutcomePreservesEncounterEffectAndSuccessfulEndExpiresItFirst()
        {
            RuleDefinitionId definition = new RuleDefinitionId("encounter-duration-effect");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                BaseSeed(),
                registry,
                true
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );
            ActiveEffectId effectId = new ActiveEffectId("encounter-duration-effect-instance");
            BindingId bindingId = new BindingId("encounter-duration-effect-binding");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                effectId,
                definition,
                Hero,
                Source,
                EffectDuration.Encounter,
                new TestEffectState()
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                bindingId,
                definition,
                Hero,
                effectId,
                Source,
                2
            );
            Resolved(await dispatcher.Dispatch(new CreateEffectWorkflowOp(effect, binding)));
            EncounterState beforeInvalid = dispatcher.Snapshot.Encounters[Encounter];
            EncounterEndFactOrderObserver endFacts = new EncounterEndFactOrderObserver();
            dispatcher.RegisterFactObserver<ActiveEffectExpiredFact>(endFacts);
            dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(endFacts);

            OpResult<EncounterEndOutcome> invalid = await dispatcher.Dispatch(
                new EndEncounterOp(Encounter, EncounterOutcome.PlayerVictory)
            );

            Assert.That(invalid, Is.TypeOf<InvalidOpResult<EncounterEndOutcome>>());
            Assert.That(invalid.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot.Encounters[Encounter], Is.SameAs(beforeInvalid));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Active)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffects[effectId], Is.EqualTo(effect));
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(effectId), Is.True);
            Assert.That(dispatcher.Snapshot.RuleBindings[bindingId], Is.EqualTo(binding));
            Assert.That(endFacts.Order, Is.Empty);

            Resolved(
                await dispatcher.Dispatch(
                    new ApplyDamageOp(
                        Enemy,
                        10,
                        new HealthChangeOriginId("successful-encounter-end"),
                        Source
                    )
                )
            );

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Ended)
            );
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Outcome,
                Is.EqualTo(EncounterOutcome.PlayerVictory)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[effectId].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(effectId), Is.False);
            Assert.That(dispatcher.Snapshot.RuleBindings[bindingId].IsEnabled, Is.False);
            Assert.That(endFacts.Order, Is.EqualTo(new[] { "expired", "ended" }));
        }

        [TestCase(EffectDurationKind.Rounds, 2, false)]
        [TestCase(EffectDurationKind.Minutes, 10, false)]
        [TestCase(EffectDurationKind.Encounter, 0, true)]
        public async Task StartAdoptsFinitePrecombatEffect(
            EffectDurationKind kind,
            int expectedBoundaries,
            bool expiresWithEncounter
        )
        {
            EffectDuration duration;
            switch (kind)
            {
                case EffectDurationKind.Rounds:
                    duration = EffectDuration.Rounds(2);
                    break;
                case EffectDurationKind.Minutes:
                    duration = EffectDuration.Minutes(1);
                    break;
                case EffectDurationKind.Encounter:
                    duration = EffectDuration.Encounter;
                    break;
                default:
                    throw new AssertionException($"Unsupported test duration {kind}.");
            }
            RuleDefinitionId definition = new RuleDefinitionId(
                $"precombat-{kind.ToString().ToLowerInvariant()}"
            );
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                BaseSeed(),
                registry,
                true
            );
            ActiveEffectId effectId = new ActiveEffectId($"precombat-effect-{kind}");
            BindingId bindingId = new BindingId($"precombat-binding-{kind}");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                effectId,
                definition,
                Enemy,
                Source,
                duration,
                new TestEffectState()
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                bindingId,
                definition,
                Enemy,
                effectId,
                Source,
                1
            );

            Resolved(await dispatcher.Dispatch(new CreateEffectWorkflowOp(effect, binding)));
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(effectId), Is.False);

            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );

            ActiveEffectTimingState timing = dispatcher.Snapshot.ActiveEffectTimings[effectId];
            Assert.That(timing.Encounter, Is.EqualTo(Encounter));
            Assert.That(timing.Binding, Is.EqualTo(bindingId));
            Assert.That(timing.RemainingBoundaries, Is.EqualTo(expectedBoundaries));
            Assert.That(timing.ExpiresWithEncounter, Is.EqualTo(expiresWithEncounter));
        }

        [Test]
        public async Task OneRoundEffectExpiresBeforeSourcesNextTurnFact()
        {
            RuleDefinitionId definition = new RuleDefinitionId("timed-effect");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                BaseSeed(),
                registry,
                true
            );
            EncounterState heroTurn = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;
            ActiveEffectId effectId = new ActiveEffectId("one-round-effect");
            BindingId bindingId = new BindingId("one-round-binding");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                effectId,
                definition,
                Hero,
                Source,
                EffectDuration.Rounds(1),
                new TestEffectState()
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                bindingId,
                definition,
                Hero,
                effectId,
                Source,
                2
            );
            Resolved(await dispatcher.Dispatch(new CreateEffectWorkflowOp(effect, binding)));
            InitiativeExpirationOrderObserver order = new InitiativeExpirationOrderObserver();
            dispatcher.RegisterFactObserver<ActiveEffectExpiredFact>(order);
            dispatcher.RegisterFactObserver<TurnBeganFact>(order);

            EncounterState enemyTurn = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(heroTurn.CurrentTurn.Value))
            ).Value.State;
            await dispatcher.Dispatch(new EndTurnOp(enemyTurn.CurrentTurn.Value));

            Assert.That(
                dispatcher.Snapshot.ActiveEffects[effectId].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(effectId), Is.False);
            Assert.That(order.Order, Is.EqualTo(new[] { "turn", "expired", "turn" }));
        }

        [Test]
        public async Task FailedExpirationLeavesLaterDueEffectsRetryableBeforeNextBoundary()
        {
            RuleDefinitionId definition = new RuleDefinitionId("retryable-timed-effect");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            EncounterState awaitingBoundary = new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                new[] { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) },
                -1,
                null,
                1,
                null
            );
            ActiveEffectId firstId = new ActiveEffectId("retryable-effect-first");
            BindingId firstBindingId = new BindingId("retryable-binding-first");
            ActiveEffectId secondId = new ActiveEffectId("retryable-effect-second");
            BindingId secondBindingId = new BindingId("retryable-binding-second");
            ActiveEffectInstance first = new ActiveEffectInstance(
                firstId,
                definition,
                Hero,
                Source,
                EffectDuration.Rounds(1),
                new TestEffectState()
            );
            ActiveEffectInstance second = new ActiveEffectInstance(
                secondId,
                definition,
                Hero,
                Source,
                EffectDuration.Rounds(1),
                new TestEffectState()
            );
            RulesStateSeed seed = BaseSeed()
                .SeedEncounter(awaitingBoundary)
                .SeedRuleBinding(
                    new ActiveRuleBinding(
                        EncounterRuleRuntime.OutcomeBindingId(Encounter),
                        EncounterRuleRuntime.OutcomeDefinitionId,
                        Hero,
                        null,
                        EncounterRuleRuntime.Source,
                        0
                    )
                )
                .SeedActiveEffect(first)
                .SeedActiveEffect(second)
                .SeedRuleBinding(
                    new ActiveRuleBinding(firstBindingId, definition, Hero, firstId, Source, 1)
                )
                .SeedRuleBinding(
                    new ActiveRuleBinding(secondBindingId, definition, Hero, secondId, Source, 2)
                )
                .SeedActiveEffectTiming(
                    new ActiveEffectTimingState(
                        firstId,
                        Encounter,
                        firstBindingId,
                        Hero,
                        1,
                        false,
                        1
                    )
                )
                .SeedActiveEffectTiming(
                    new ActiveEffectTimingState(
                        secondId,
                        Encounter,
                        secondBindingId,
                        Hero,
                        1,
                        false,
                        2
                    )
                );
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed, registry);
            ThrowOnceExpirationObserver observer = new ThrowOnceExpirationObserver();
            dispatcher.RegisterFactObserver<ActiveEffectExpiredFact>(observer);

            Assert.ThrowsAsync<ApplicationException>(async () =>
                await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter))
            );

            Assert.That(
                dispatcher.Snapshot.ActiveEffects[firstId].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[secondId].Status,
                Is.EqualTo(ActiveEffectStatus.Active)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings[secondId].RemainingBoundaries,
                Is.Zero
            );
            EncounterState pending = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(pending.Cursor, Is.Zero);
            Assert.That(pending.CurrentTurn, Is.Null);
            Assert.That(pending.IsInitiativeBoundaryPending, Is.True);

            Resolved(await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter)));

            Assert.That(
                dispatcher.Snapshot.ActiveEffects[secondId].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(secondId), Is.False);
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].CurrentTurn.Value.Actor,
                Is.EqualTo(Hero)
            );
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].IsInitiativeBoundaryPending,
                Is.False
            );
            Assert.That(observer.Calls, Is.EqualTo(2));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task CountedEffectsRetireWhenOwningEncounterCloses(bool minutes, bool suspend)
        {
            RuleDefinitionId definition = new RuleDefinitionId("counted-effect");
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder.Define(definition);
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10, 20, 10),
                BaseSeed(),
                registry,
                true
            );
            Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            );
            ActiveEffectId countedId = new ActiveEffectId("counted-effect-instance");
            BindingId countedBindingId = new BindingId("counted-effect-binding");
            ActiveEffectInstance counted = new ActiveEffectInstance(
                countedId,
                definition,
                Hero,
                Source,
                minutes ? EffectDuration.Minutes(1) : EffectDuration.Rounds(1),
                new TestEffectState()
            );
            ActiveRuleBinding countedBinding = new ActiveRuleBinding(
                countedBindingId,
                definition,
                Hero,
                countedId,
                Source,
                2
            );
            ActiveEffectId permanentId = new ActiveEffectId("permanent-effect-instance");
            BindingId permanentBindingId = new BindingId("permanent-effect-binding");
            ActiveEffectInstance permanent = new ActiveEffectInstance(
                permanentId,
                definition,
                Hero,
                Source,
                EffectDuration.Indefinite,
                new TestEffectState()
            );
            ActiveRuleBinding permanentBinding = new ActiveRuleBinding(
                permanentBindingId,
                definition,
                Hero,
                permanentId,
                Source,
                3
            );
            Resolved(
                await dispatcher.Dispatch(new CreateEffectWorkflowOp(counted, countedBinding))
            );
            Resolved(
                await dispatcher.Dispatch(new CreateEffectWorkflowOp(permanent, permanentBinding))
            );

            if (suspend)
            {
                Resolved(await dispatcher.Dispatch(new SuspendEncounterOp(Encounter)));
                EncounterId resumed = new EncounterId("resumed-encounter");
                Resolved(
                    await dispatcher.Dispatch(
                        new StartEncounterOp(
                            resumed,
                            Players,
                            new[]
                            {
                                new EncounterParticipant(Hero, Players, 0),
                                new EncounterParticipant(Enemy, Enemies, 0),
                            }
                        )
                    )
                );
                Assert.That(
                    dispatcher.Snapshot.Encounters[resumed].Phase,
                    Is.EqualTo(EncounterPhase.Active)
                );
            }
            else
            {
                Resolved(
                    await dispatcher.Dispatch(
                        new ApplyDamageOp(
                            Enemy,
                            10,
                            new HealthChangeOriginId("close-counted-effect-encounter"),
                            Source
                        )
                    )
                );
            }

            Assert.That(
                dispatcher.Snapshot.ActiveEffects[countedId].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(countedId), Is.False);
            Assert.That(dispatcher.Snapshot.RuleBindings[countedBindingId].IsEnabled, Is.False);
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[permanentId].Status,
                Is.EqualTo(ActiveEffectStatus.Active)
            );
            Assert.That(dispatcher.Snapshot.RuleBindings[permanentBindingId].IsEnabled, Is.True);
        }

        private static StartEncounterOp Start(params EncounterParticipant[] participants) =>
            new StartEncounterOp(Encounter, Players, participants);

        private static EncounterState ActiveTurnEncounter() =>
            new EncounterState(
                Encounter,
                EncounterPhase.Active,
                Players,
                RoundNumber.First,
                new[] { Entry(Hero, Players, 10, 0), Entry(Enemy, Enemies, 9, 1) },
                0,
                new TurnIdentity(Encounter, new TurnId(1), Hero, RoundNumber.First, 0),
                2,
                null
            );

        private static InitiativeEntry Entry(
            CreatureId creature,
            PlayerId team,
            int roll,
            long order
        ) => new InitiativeEntry(creature, team, roll, 0, order, RoundNumber.First);

        private static RulesStateSeed BaseSeed() =>
            new RulesStateSeed()
                .SeedHealth(Hero, new HealthState(10, 10))
                .SeedHealth(Enemy, new HealthState(10, 10))
                .SeedHealth(Reinforcement, new HealthState(10, 10));

        private static RulesStateSeed JoinSeed() =>
            new RulesStateSeed()
                .SeedHealth(Hero, new HealthState(10, 10))
                .SeedHealth(Enemy, new HealthState(10, 10));

        private static CombatantRulesState CreateJoinRegistration(
            CreatureId creature,
            SpellSlotPoolId slot,
            BindingId binding,
            long generation = 0
        ) =>
            new CombatantRulesState(
                new CreatureState(creature, Enemies),
                new HealthState(10, 10),
                new GridPosition(0, 0, 0),
                new GridDistance(25),
                PreparedCreatureInputs.Empty,
                new[] { new SpellSlotState(slot, creature, 1, 1) },
                new[]
                {
                    new ActiveRuleBinding(
                        binding,
                        new RuleDefinitionId("reinforcement-rule"),
                        creature,
                        null,
                        Source,
                        generation,
                        false
                    ),
                }
            );

        private static ActiveEffectRegistration CreateActiveJoinRegistration(
            string identity,
            RuleDefinitionId definition,
            CreatureId owner,
            CreatureId sourceCreature,
            long creationOrder,
            EffectDuration duration,
            ActiveEffectTimingState timing = null
        )
        {
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId($"{identity}-effect"),
                definition,
                sourceCreature,
                RuleSource.FromSlug(identity),
                duration,
                new TestEffectState()
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId($"{identity}-binding"),
                definition,
                owner,
                effect.Id,
                effect.Source,
                creationOrder
            );
            return new ActiveEffectRegistration(effect, binding, timing);
        }

        private static RageEffectState CreateRageJoinReceipt(
            OpId root,
            int committedTemporaryHitPoints
        )
        {
            HealthState before = new HealthState(10, 10);
            HealthState after = new HealthState(
                10,
                10,
                committedTemporaryHitPoints,
                RageRules.Source
            ).WithTemporaryHitPointRevision(1);
            return RageEffectState
                .CreatePending(Reinforcement, default, root, 3)
                .WithGrantTransition(
                    new TemporaryHitPointsGrantTransition(
                        before,
                        after,
                        new TemporaryHitPointsGrantOutcome(
                            true,
                            false,
                            0,
                            committedTemporaryHitPoints
                        )
                    )
                );
        }

        private static ActiveEffectRegistration CreateRageJoinRegistration(RageEffectState receipt)
        {
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId("rage-replay-effect"),
                RageActionDefinition.EffectDefinitionId,
                Reinforcement,
                RageRules.Source,
                EffectDuration.Indefinite,
                receipt
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("rage-replay-binding"),
                effect.DefinitionId,
                Reinforcement,
                effect.Id,
                effect.Source,
                70
            );
            return new ActiveEffectRegistration(effect, binding);
        }

        private static CombatantRulesState CreateActiveJoinState(
            CreatureId creature,
            params ActiveEffectRegistration[] activeEffects
        ) =>
            new CombatantRulesState(
                new CreatureState(creature, Enemies),
                new HealthState(10, 10),
                new GridPosition(0, 0, 0),
                new GridDistance(25),
                PreparedCreatureInputs.Empty,
                Array.Empty<SpellSlotState>(),
                Array.Empty<ActiveRuleBinding>(),
                activeEffects
            );

        private static CombatantRulesState CreateJoinStateWithPreparedInputs(
            CreatureId creature,
            PreparedCreatureInputs preparedInputs
        ) =>
            new CombatantRulesState(
                new CreatureState(creature, Enemies),
                new HealthState(10, 10),
                new GridPosition(0, 0, 0),
                new GridDistance(25),
                preparedInputs,
                Array.Empty<SpellSlotState>(),
                Array.Empty<ActiveRuleBinding>()
            );

        private static PreparedCreatureInputs CreateReplayPreparedInputs(int level) =>
            new PreparedCreatureInputs(
                level,
                new PreparedAbilityModifiers(1, 2, 3, 4, 5, 6),
                new[] { new KeyValuePair<string, int>("athletics", 2) },
                new[] { "steel-shield" },
                "medium",
                new[] { "humanoid" },
                new[] { new PreparedDefenseDescriptor("fire", 2) },
                new[] { new PreparedDefenseDescriptor("cold", 3) },
                new[] { new PreparedImmunityDescriptor("poison", PreparedImmunityKind.Damage) },
                new[] { "self:test-option" },
                new[]
                {
                    new PreparedBoundOption(
                        new RuleDefinitionId("prepared:replay"),
                        "item:owned:replay",
                        new PreparedAllPredicate(
                            new PreparedPredicate[]
                            {
                                PreparedPredicate.Always,
                                new PreparedOptionPredicate("self:test-option"),
                            }
                        )
                    ),
                },
                new[] { new KeyValuePair<string, int>("replay-value", 9) }
            );

        private static RuleDispatcher CreateDispatcher(
            IRollService rolls,
            RulesStateSeed seed = null,
            RuleRegistry registry = null,
            bool includeEffectWorkflow = false,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters = null
        )
        {
            RuleRegistry selected = registry ?? new RuleRegistryBuilder().AddOutcomeRule().Build();
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed ?? BaseSeed()),
                rolls
            )
                .UseRuleRegistry(selected)
                .UseHealthRules()
                .UseActiveEffectRules(selected)
                .UseMultipleAttackPenaltyRules()
                .UseEncounterRules(
                    turnStartAdapters ?? Array.Empty<IEncounterTurnStartAdapter>(),
                    selected
                );
            builder.RegisterHandler<
                CommitEncounterStartWorkflowOp,
                OpResult<EncounterStartOutcome>
            >(new CommitEncounterStartWorkflowHandler());
            builder.RegisterHandler<CommitEncounterJoinWorkflowOp, OpResult<EncounterJoinOutcome>>(
                new CommitEncounterJoinWorkflowHandler()
            );
            builder.RegisterHandler<
                CommitTurnStartAdapterProgressWorkflowOp,
                OpResult<TurnStartAdapterProgress>
            >(new CommitTurnStartAdapterProgressWorkflowHandler());
            if (includeEffectWorkflow)
                builder.RegisterHandler<CreateEffectWorkflowOp, ActiveEffectCreationOutcome>(
                    new CreateEffectWorkflowHandler()
                );
            return builder.Build();
        }

        private static ResolvedOpResult<TResult> Resolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class RescueListener : IRuleFactListener<CreatureReducedToZeroFact>
        {
            public int Calls { get; private set; }

            public async ValueTask OnFactCommitted(
                CreatureReducedToZeroFact fact,
                FactContext context
            )
            {
                Calls++;
                await context.Dispatch(
                    new ApplyHealingOp(
                        fact.Creature,
                        1,
                        new HealthChangeOriginId("rescue-heal"),
                        Source
                    )
                );
            }
        }

        private sealed class LethalCascadeListener : IRuleFactListener<CreatureReducedToZeroFact>
        {
            private readonly CreatureId trigger;
            private readonly CreatureId target;

            public LethalCascadeListener(CreatureId trigger, CreatureId target)
            {
                this.trigger = trigger;
                this.target = target;
            }

            public int Calls { get; private set; }

            public async ValueTask OnFactCommitted(
                CreatureReducedToZeroFact fact,
                FactContext context
            )
            {
                if (fact.Creature != trigger)
                    return;
                Calls++;
                HealthState health = context.Snapshot.Health[target];
                await context.Dispatch(
                    new ApplyDamageOp(
                        target,
                        health.Current + health.Temporary,
                        new HealthChangeOriginId("lethal-cascade"),
                        Source
                    )
                );
            }
        }

        private sealed class RecordingTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly string label;
            private readonly IList<string> order;
            private readonly int? actions;
            private readonly List<CreatureId> actors = new List<CreatureId>();

            public RecordingTurnStartAdapter(
                string label,
                IList<string> order = null,
                int? actions = null
            )
            {
                this.label = label;
                this.order = order;
                this.actions = actions;
            }

            public IReadOnlyList<CreatureId> Actors => actors;

            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                actors.Add(context.Actor);
                order?.Add(label);
                return new ValueTask<TurnStartContribution>(
                    actions.HasValue ? new TurnStartContribution(actions.Value) : current
                );
            }
        }

        private sealed class LethalTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly CreatureId target;

            public LethalTurnStartAdapter(CreatureId target) => this.target = target;

            public int Calls { get; private set; }

            public async ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                if (context.Actor != target)
                    return current;
                Calls++;
                HealthState health = context.Snapshot.Health[context.Actor];
                await context.ApplyFinalDamage(
                    context.Actor,
                    health.Current + health.Temporary,
                    new HealthChangeOriginId("lethal-turn-start"),
                    Source
                );
                return current;
            }
        }

        private sealed class DamageAndContributionTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly CreatureId actor;
            private readonly int damage;
            private readonly int actions;

            public DamageAndContributionTurnStartAdapter(CreatureId actor, int damage, int actions)
            {
                this.actor = actor;
                this.damage = damage;
                this.actions = actions;
            }

            public int Calls { get; private set; }

            public async ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                if (context.Actor != actor)
                    return current;
                Calls++;
                await context.ApplyFinalDamage(
                    actor,
                    damage,
                    new HealthChangeOriginId("turn-start-progress-damage"),
                    Source
                );
                return new TurnStartContribution(actions);
            }
        }

        private sealed class ThrowOnceTurnStartAdapter : IEncounterTurnStartAdapter
        {
            public int Calls { get; private set; }

            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                Calls++;
                if (Calls == 1)
                    throw new InvalidOperationException("Injected turn-start adapter failure.");
                return new ValueTask<TurnStartContribution>(current);
            }
        }

        private sealed class CountingFactObserver<TFact> : IFactObserver<TFact>
            where TFact : RuleFact
        {
            public int Calls { get; private set; }

            public ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
            {
                Calls++;
                return default;
            }
        }

        private sealed class ThrowOnceFactObserver<TFact> : IFactObserver<TFact>
            where TFact : RuleFact
        {
            private bool thrown;

            public ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
            {
                if (!thrown)
                {
                    thrown = true;
                    throw new InvalidOperationException(
                        "Injected post-commit Fact observer failure."
                    );
                }
                return default;
            }
        }

        private sealed class CollectingFactObserver<TFact> : IFactObserver<TFact>
            where TFact : RuleFact
        {
            private readonly List<TFact> facts = new List<TFact>();

            public IReadOnlyList<TFact> Facts => facts;

            public ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
            {
                facts.Add(fact);
                return default;
            }
        }

        private sealed class BlockingFactObserver<TFact> : IFactObserver<TFact>
            where TFact : RuleFact
        {
            private readonly TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            private readonly TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            public Task Started => started.Task;

            public ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
            {
                started.TrySetResult(true);
                return new ValueTask(release.Task);
            }

            public void Release() => release.TrySetResult(true);
        }

        private sealed class ThrowOnceExpirationObserver : IFactObserver<ActiveEffectExpiredFact>
        {
            public int Calls { get; private set; }

            public ValueTask OnFactCommitted(ActiveEffectExpiredFact fact, RulesSnapshot snapshot)
            {
                Calls++;
                if (Calls == 1)
                    throw new ApplicationException("first expiration callback failed");
                return default;
            }
        }

        private sealed class TurnBeganActorsObserver : IFactObserver<TurnBeganFact>
        {
            private readonly List<CreatureId> actors = new List<CreatureId>();

            public IReadOnlyList<CreatureId> Actors => actors;

            public ValueTask OnFactCommitted(TurnBeganFact fact, RulesSnapshot snapshot)
            {
                actors.Add(fact.Turn.Actor);
                return default;
            }
        }

        private sealed class TurnBeganSnapshotObserver : IFactObserver<TurnBeganFact>
        {
            private readonly IList<string> order;

            public TurnBeganSnapshotObserver(IList<string> order) => this.order = order;

            public int ActionsAtFact { get; private set; } = -1;

            public ValueTask OnFactCommitted(TurnBeganFact fact, RulesSnapshot snapshot)
            {
                ActionsAtFact = snapshot.ActionEconomy[fact.Turn.Actor].ActionsRemaining;
                order.Add("fact");
                return default;
            }
        }

        private sealed class EncounterEndFactOrderObserver
            : IFactObserver<ActiveEffectExpiredFact>,
                IFactObserver<EncounterOutcomeCommittedFact>
        {
            private readonly List<string> order = new List<string>();

            public IReadOnlyList<string> Order => order;

            public ValueTask OnFactCommitted(ActiveEffectExpiredFact fact, RulesSnapshot snapshot)
            {
                order.Add("expired");
                return default;
            }

            public ValueTask OnFactCommitted(
                EncounterOutcomeCommittedFact fact,
                RulesSnapshot snapshot
            )
            {
                order.Add("ended");
                return default;
            }
        }

        private sealed class InitiativeExpirationOrderObserver
            : IFactObserver<ActiveEffectExpiredFact>,
                IFactObserver<TurnBeganFact>
        {
            private readonly List<string> order = new List<string>();

            public IReadOnlyList<string> Order => order;

            public ValueTask OnFactCommitted(ActiveEffectExpiredFact fact, RulesSnapshot snapshot)
            {
                order.Add("expired");
                return default;
            }

            public ValueTask OnFactCommitted(TurnBeganFact fact, RulesSnapshot snapshot)
            {
                order.Add("turn");
                return default;
            }
        }

        private sealed class BatchOutcomeObserver
            : IFactObserver<CreatureReducedToZeroFact>,
                IFactObserver<EncounterOutcomeCommittedFact>
        {
            private readonly CreatureId hero;
            private readonly CreatureId enemy;
            private readonly List<string> order = new List<string>();

            public BatchOutcomeObserver(CreatureId hero, CreatureId enemy)
            {
                this.hero = hero;
                this.enemy = enemy;
            }

            public IReadOnlyList<string> Order => order;
            public int HeroAtOutcome { get; private set; } = -1;
            public int EnemyAtOutcome { get; private set; } = -1;
            public int EndCalls { get; private set; }

            public ValueTask OnFactCommitted(CreatureReducedToZeroFact fact, RulesSnapshot snapshot)
            {
                order.Add($"zero:{fact.Creature.Value}");
                return default;
            }

            public ValueTask OnFactCommitted(
                EncounterOutcomeCommittedFact fact,
                RulesSnapshot snapshot
            )
            {
                EndCalls++;
                HeroAtOutcome = snapshot.Health[hero].Current;
                EnemyAtOutcome = snapshot.Health[enemy].Current;
                order.Add("ended");
                return default;
            }
        }

        private sealed class TestEffectState : IEffectState { }

        private sealed class ClaimIncomingEffectOnJoinListener
            : IRuleFactListener<EncounterJoinedFact>
        {
            private readonly ActiveEffectRegistration claim;

            internal ClaimIncomingEffectOnJoinListener(ActiveEffectRegistration claim) =>
                this.claim = claim;

            internal int Calls { get; private set; }
            internal OpResult<ActiveEffectAdoptionOutcome> Result { get; private set; }

            public async ValueTask OnFactCommitted(EncounterJoinedFact fact, FactContext context)
            {
                Calls++;
                Result = await context.Dispatch(
                    new AdoptActiveEffectRegistrationsOp(
                        new[] { claim },
                        RuleSource.FromSlug("join-listener-claim")
                    )
                );
            }
        }

        private sealed class CommitEncounterStartWorkflowOp
            : IRuleOp<OpResult<EncounterStartOutcome>>
        {
            public CommitEncounterStartOp Commit { get; }

            public CommitEncounterStartWorkflowOp(CommitEncounterStartOp commit) =>
                Commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        private sealed class CommitEncounterStartWorkflowHandler
            : IOpHandler<CommitEncounterStartWorkflowOp, OpResult<EncounterStartOutcome>>
        {
            public async ValueTask<OpResult<EncounterStartOutcome>> Handle(
                OpFrame<CommitEncounterStartWorkflowOp> frame,
                OpHandlerContext context
            ) => await context.Dispatch(frame.Op.Commit);
        }

        private sealed class CommitEncounterJoinWorkflowOp : IRuleOp<OpResult<EncounterJoinOutcome>>
        {
            public CommitEncounterJoinWorkflowOp(CommitEncounterJoinOp commit) =>
                Commit = commit ?? throw new ArgumentNullException(nameof(commit));

            public CommitEncounterJoinOp Commit { get; }
        }

        private sealed class CommitEncounterJoinWorkflowHandler
            : IOpHandler<CommitEncounterJoinWorkflowOp, OpResult<EncounterJoinOutcome>>
        {
            public async ValueTask<OpResult<EncounterJoinOutcome>> Handle(
                OpFrame<CommitEncounterJoinWorkflowOp> frame,
                OpHandlerContext context
            ) => await context.Dispatch(frame.Op.Commit);
        }

        private sealed class CommitTurnStartAdapterProgressWorkflowOp
            : IRuleOp<OpResult<TurnStartAdapterProgress>>
        {
            public CommitTurnStartAdapterProgressWorkflowOp(
                CommitTurnStartAdapterProgressOp commit
            ) => Commit = commit;

            public CommitTurnStartAdapterProgressOp Commit { get; }
        }

        private sealed class CommitTurnStartAdapterProgressWorkflowHandler
            : IOpHandler<
                CommitTurnStartAdapterProgressWorkflowOp,
                OpResult<TurnStartAdapterProgress>
            >
        {
            public ValueTask<OpResult<TurnStartAdapterProgress>> Handle(
                OpFrame<CommitTurnStartAdapterProgressWorkflowOp> frame,
                OpHandlerContext context
            ) => context.Dispatch(frame.Op.Commit);
        }

        private sealed class CreateEffectWorkflowOp : IRuleOp<ActiveEffectCreationOutcome>
        {
            public ActiveEffectInstance Effect { get; }
            public ActiveRuleBinding Binding { get; }

            public CreateEffectWorkflowOp(ActiveEffectInstance effect, ActiveRuleBinding binding)
            {
                Effect = effect;
                Binding = binding;
            }
        }

        private sealed class CreateEffectWorkflowHandler
            : IOpHandler<CreateEffectWorkflowOp, ActiveEffectCreationOutcome>
        {
            public async ValueTask<ActiveEffectCreationOutcome> Handle(
                OpFrame<CreateEffectWorkflowOp> frame,
                OpHandlerContext context
            ) =>
                Resolved(
                    await context.Dispatch(
                        new CreateActiveEffectOp(frame.Op.Effect, frame.Op.Binding)
                    )
                ).Value;
        }
    }
}
