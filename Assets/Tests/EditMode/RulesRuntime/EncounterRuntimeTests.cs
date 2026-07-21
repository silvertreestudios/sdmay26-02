using System;
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
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10));
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
        }

        [Test]
        public void StaleAndDuplicateTurnEndRejectWithoutCommit()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 10));
            EncounterState started = Resolved(
                dispatcher
                    .Dispatch(
                        Start(
                            new EncounterParticipant(Hero, Players, 0),
                            new EncounterParticipant(Enemy, Enemies, 0)
                        )
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
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
            Resolved(
                dispatcher
                    .Dispatch(new EndTurnOp(started.CurrentTurn.Value))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
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
            RuleDispatcher dispatcher = CreateDispatcher(new ScriptedRollService(20, 15, 10), seed);

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
            bool includeEffectWorkflow = false
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
                .UseEncounterRules();
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
