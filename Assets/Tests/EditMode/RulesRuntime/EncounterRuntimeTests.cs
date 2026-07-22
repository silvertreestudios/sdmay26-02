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
        private static readonly PlayerId Players = new PlayerId("players");
        private static readonly PlayerId Enemies = new PlayerId("enemies");
        private static readonly EncounterId Encounter = new EncounterId("test-encounter");
        private static readonly RuleSource Source = RuleSource.FromSlug("encounter-test");

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
        public async Task CommitStartReducerRejectsRosterWithoutDesignatedProtagonist()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService());
            InitiativeEntry[] roster = { Entry(Enemy, Enemies, 10, 0) };

            OpResult<EncounterStartOutcome> result = await dispatcher.Dispatch(
                new CommitEncounterStartOp(Encounter, Players, Array.AsReadOnly(roster))
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<EncounterStartOutcome>>());
            Assert.That(
                ((InvalidOpResult<EncounterStartOutcome>)result).Reason,
                Does.Contain("designated protagonist team")
            );
            Assert.That(dispatcher.Snapshot.Encounters.Contains(Encounter), Is.False);
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
                        typeof(InitiativeBoundaryReachedFact),
                        typeof(TurnBeganFact),
                    }
                )
            );
            Assert.That(dispatcher.Trace.GetRolls(new OpId(1)), Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ExactEndTurnResetsActionsMapAndMovementThenWrapsRoundOnce()
        {
            MovementBudgetState budget = new MovementBudgetState(
                new MovementBudgetId(new OpId(99)),
                Hero,
                new GridDistance(15),
                DiagonalMovementPhase.NextCostsTenFeet
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                BaseSeed().SeedMovementBudget(Hero, budget)
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
            await dispatcher.Dispatch(new IncrementLegacyMapOp(Hero));
            await dispatcher.Dispatch(new SpendLegacyActionsOp(Hero, 1));

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
        public async Task ZeroCostActionAuthorizationRequiresExactTurnWithoutSpendMutation()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10));
            CountingFactObserver<LegacyActionsSpentFact> spends =
                new CountingFactObserver<LegacyActionsSpentFact>();
            dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
            EncounterState started = Resolved(
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                )
            ).Value.State;

            LegacyActionSpendOutcome authorized = Resolved(
                await dispatcher.Dispatch(new SpendLegacyActionsOp(Hero, 0))
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
                    await dispatcher.Dispatch(new SpendLegacyActionsOp(Hero, 0))
            );

            Assert.That(rejected.Message, Does.Contain("does not own an active current turn"));
            Assert.That(advanced.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
            Assert.That(spends.Calls, Is.Zero);
        }

        [Test]
        public async Task TargetAwareActionSpendRejectsAnyCommittedDefeatAtomically()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10, 5));
            CountingFactObserver<LegacyActionsSpentFact> spends =
                new CountingFactObserver<LegacyActionsSpentFact>();
            dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
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
            await dispatcher.Dispatch(new FinalizeCreatureDefeatOp(Enemy));

            InvalidOperationException rejected = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(
                        new SpendLegacyActionsOp(
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

            LegacyActionSpendOutcome livingTargetSpend = Resolved(
                await dispatcher.Dispatch(
                    new SpendLegacyActionsOp(Hero, 1, new[] { Reinforcement })
                )
            ).Value;
            Assert.That(livingTargetSpend.Remaining, Is.EqualTo(2));
            Assert.That(spends.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task QueuedMapAfterExactTurnLossRejectsWithoutRecreatingPenalty()
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
            BlockingFactObserver<LegacyActionsSpentFact> blocker =
                new BlockingFactObserver<LegacyActionsSpentFact>();
            CountingFactObserver<LegacyMapIncrementedFact> maps =
                new CountingFactObserver<LegacyMapIncrementedFact>();
            dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(blocker);
            dispatcher.RegisterFactObserver<LegacyMapIncrementedFact>(maps);

            try
            {
                Task<OpResult<LegacyActionSpendOutcome>> spending = dispatcher
                    .Dispatch(new SpendLegacyActionsOp(Hero, 1))
                    .AsTask();
                await blocker.Started;
                Task<OpResult<EncounterAdvanceOutcome>> ending = dispatcher
                    .Dispatch(new EndTurnOp(started.CurrentTurn.Value))
                    .AsTask();
                Task<OpResult<LegacyMapOutcome>> mapping = dispatcher
                    .Dispatch(new IncrementLegacyMapOp(Hero))
                    .AsTask();

                blocker.Release();
                Resolved(await spending);
                EncounterState advanced = Resolved(await ending).Value.State;
                InvalidOperationException rejected = Assert.ThrowsAsync<InvalidOperationException>(
                    async () =>
                        await mapping
                );

                Assert.That(rejected.Message, Does.Contain("does not own an active current turn"));
                Assert.That(advanced.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
                Assert.That(dispatcher.Snapshot.MultipleAttackPenalty[Hero].AttackCount, Is.Zero);
                Assert.That(maps.Calls, Is.Zero);
            }
            finally
            {
                blocker.Release();
            }
        }

        [Test]
        public async Task QueuedMapAfterEncounterEndRejectsWithoutRecreatingPenalty()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                BaseSeed().SeedHealth(Enemy, new HealthState(1, 10))
            );
            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );
            BlockingFactObserver<HealthFact> blocker = new BlockingFactObserver<HealthFact>();
            CountingFactObserver<LegacyMapIncrementedFact> maps =
                new CountingFactObserver<LegacyMapIncrementedFact>();
            dispatcher.RegisterFactObserver<HealthFact>(blocker);
            dispatcher.RegisterFactObserver<LegacyMapIncrementedFact>(maps);

            try
            {
                Task<OpResult<DamageOutcome>> lethal = dispatcher
                    .Dispatch(
                        new ApplyDamageOp(
                            Enemy,
                            1,
                            new HealthChangeOriginId("queued-map-lethal"),
                            Source
                        )
                    )
                    .AsTask();
                await blocker.Started;
                Task<OpResult<LegacyMapOutcome>> mapping = dispatcher
                    .Dispatch(new IncrementLegacyMapOp(Hero))
                    .AsTask();

                blocker.Release();
                Resolved(await lethal);
                InvalidOperationException rejected = Assert.ThrowsAsync<InvalidOperationException>(
                    async () =>
                        await mapping
                );

                Assert.That(rejected.Message, Does.Contain("does not own an active current turn"));
                Assert.That(
                    dispatcher.Snapshot.Encounters[Encounter].Phase,
                    Is.EqualTo(EncounterPhase.Ended)
                );
                Assert.That(
                    dispatcher.Snapshot.Encounters[Encounter].Outcome,
                    Is.EqualTo(EncounterOutcome.PlayerVictory)
                );
                Assert.That(dispatcher.Snapshot.MultipleAttackPenalty[Hero].AttackCount, Is.Zero);
                Assert.That(maps.Calls, Is.Zero);
            }
            finally
            {
                blocker.Release();
            }
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
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 15, 10),
                seed,
                turnStartAdapters: new[] { adapter }
            );

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
                advanced
                    .Facts.OfType<InitiativeBoundaryReachedFact>()
                    .Select(fact => fact.Creature),
                Is.EqualTo(new[] { Enemy, Reinforcement })
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
            CountingFactObserver<EncounterEndedFact> ended =
                new CountingFactObserver<EncounterEndedFact>();
            CountingFactObserver<TurnBeganFact> began = new CountingFactObserver<TurnBeganFact>();
            dispatcher.RegisterFactObserver<EncounterEndedFact>(ended);
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
            dispatcher.RegisterFactObserver<TurnBeganFact>(began);

            await dispatcher.Dispatch(
                Start(
                    new EncounterParticipant(Hero, Players, 0),
                    new EncounterParticipant(Enemy, Enemies, 0)
                )
            );

            EncounterState state = dispatcher.Snapshot.Encounters[Encounter];
            Assert.That(lethal.Calls, Is.EqualTo(1));
            Assert.That(rescue.Calls, Is.EqualTo(1));
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
            MovementBudgetState budget = new MovementBudgetState(
                new MovementBudgetId(new OpId(99)),
                Hero,
                new GridDistance(10),
                DiagonalMovementPhase.NextCostsFiveFeet
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new ScriptedRollService(20, 10),
                BaseSeed().SeedMovementBudget(Hero, budget)
            );
            CountingFactObserver<MovementBudgetResetFact> movementResets =
                new CountingFactObserver<MovementBudgetResetFact>();
            CountingFactObserver<TurnEndedFact> turnsEnded =
                new CountingFactObserver<TurnEndedFact>();
            dispatcher.RegisterFactObserver<MovementBudgetResetFact>(movementResets);
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
            Assert.That(movementResets.Calls, Is.EqualTo(1));
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
        public async Task HigherInitiativeReinforcementWaitsUntilNextRound()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(15, 10, 20));
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
                .SeedEncounter(active);
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(), seed);

            EncounterState ended = Resolved(
                await dispatcher.Dispatch(new EvaluateEncounterOutcomeOp(Encounter))
            ).Value.State;

            Assert.That(ended.Outcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
            Assert.That(Enum.GetNames(typeof(EncounterOutcome)), Does.Not.Contain("Draw"));
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
                );
                await dispatcher.Dispatch(
                    Start(
                        new EncounterParticipant(Hero, Players, 0),
                        new EncounterParticipant(Enemy, Enemies, 0)
                    )
                );
                BatchOutcomeObserver observer = new BatchOutcomeObserver(Hero, Enemy);
                dispatcher.RegisterFactObserver<CreatureReducedToZeroFact>(observer);
                dispatcher.RegisterFactObserver<EncounterEndedFact>(observer);
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
            Assert.That(state.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(state.Outcome, Is.Null);
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
            dispatcher.RegisterFactObserver<EncounterEndedFact>(endFacts);

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

            EncounterState enemyTurn = Resolved(
                await dispatcher.Dispatch(new EndTurnOp(heroTurn.CurrentTurn.Value))
            ).Value.State;
            OpResult<EncounterAdvanceOutcome> nextHero = await dispatcher.Dispatch(
                new EndTurnOp(enemyTurn.CurrentTurn.Value)
            );

            Assert.That(
                dispatcher.Snapshot.ActiveEffects[effectId].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(effectId), Is.False);
            Type[] facts = nextHero.Facts.Select(fact => fact.GetType()).ToArray();
            Assert.That(
                Array.IndexOf(facts, typeof(ActiveEffectExpiredFact)),
                Is.LessThan(Array.IndexOf(facts, typeof(TurnBeganFact)))
            );
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
                .UseMovementBudgetResetRules()
                .UseEncounterRules(turnStartAdapters ?? Array.Empty<IEncounterTurnStartAdapter>());
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
                IFactObserver<EncounterEndedFact>
        {
            private readonly List<string> order = new List<string>();

            public IReadOnlyList<string> Order => order;

            public ValueTask OnFactCommitted(ActiveEffectExpiredFact fact, RulesSnapshot snapshot)
            {
                order.Add("expired");
                return default;
            }

            public ValueTask OnFactCommitted(EncounterEndedFact fact, RulesSnapshot snapshot)
            {
                order.Add("ended");
                return default;
            }
        }

        private sealed class BatchOutcomeObserver
            : IFactObserver<CreatureReducedToZeroFact>,
                IFactObserver<EncounterEndedFact>
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

            public ValueTask OnFactCommitted(EncounterEndedFact fact, RulesSnapshot snapshot)
            {
                EndCalls++;
                HeroAtOutcome = snapshot.Health[hero].Current;
                EnemyAtOutcome = snapshot.Health[enemy].Current;
                order.Add("ended");
                return default;
            }
        }

        private sealed class TestEffectState : IEffectState { }

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
