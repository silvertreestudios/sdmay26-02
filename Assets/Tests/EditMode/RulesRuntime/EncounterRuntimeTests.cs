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

        /// <summary>Identifies one state collection that can collide during a join preflight.</summary>
        public enum JoinRegistrationCollision
        {
            /// <summary>The creature identity slice.</summary>
            Creature,

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
            Assert.That(dispatcher.Snapshot.MovementBudgets.Contains(Hero), Is.False);
            Assert.That(movementResets.Calls, Is.EqualTo(1));

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

        /// <summary>Verifies every join-owned state slice is preflighted before any write.</summary>
        [TestCase(JoinRegistrationCollision.Creature)]
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
                new[] { new SpellSlotState(slotId, Reinforcement, 1, 1) },
                new[] { binding }
            );
            RulesStateSeed seed = JoinSeed();
            switch (collision)
            {
                case JoinRegistrationCollision.Creature:
                    seed.SeedCreature(new CreatureState(Reinforcement, Enemies));
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
            BindingId binding
        ) =>
            new CombatantRulesState(
                new CreatureState(creature, Enemies),
                new HealthState(10, 10),
                new GridPosition(0, 0, 0),
                new GridDistance(25),
                new[] { new SpellSlotState(slot, creature, 1, 1) },
                new[]
                {
                    new ActiveRuleBinding(
                        binding,
                        new RuleDefinitionId("reinforcement-rule"),
                        creature,
                        null,
                        Source,
                        0,
                        false
                    ),
                }
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
                .UseEncounterRules(turnStartAdapters ?? Array.Empty<IEncounterTurnStartAdapter>());
            builder.RegisterHandler<
                CommitEncounterStartWorkflowOp,
                OpResult<EncounterStartOutcome>
            >(new CommitEncounterStartWorkflowHandler());
            builder.RegisterHandler<CommitEncounterJoinWorkflowOp, OpResult<EncounterJoinOutcome>>(
                new CommitEncounterJoinWorkflowHandler()
            );
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
