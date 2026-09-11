using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class RottingAuraRulesTests
    {
        private static readonly CreatureId Source = new("aura-source");
        private static readonly CreatureId SecondSource = new("second-aura-source");
        private static readonly CreatureId Target = new("aura-target");
        private static readonly EncounterId Encounter = new("aura-encounter");
        private static readonly PlayerId Players = new("players");
        private static readonly PlayerId Enemies = new("enemies");
        private static readonly TurnIdentity Turn = new(
            Encounter,
            new TurnId(1),
            Target,
            RoundNumber.First,
            0
        );
        private static readonly RuleSource TestSource = RuleSource.FromSlug("aura-test");

        [Test]
        public void CompletionFactCopiesCollectionsAndRejectsObserverMutation()
        {
            TypedDamagePart originalDamage = new("void", 4, new[] { "Rotting Aura" });
            TypedDefenseAdjustment weakness = new("void", 2);
            TypedDefenseAdjustment resistance = new("void", 1);
            TypedDamagePart[] damage = { originalDamage };
            TypedDefenseAdjustment[] weaknesses = { weakness };
            List<TypedDefenseAdjustment> resistances = new() { resistance };
            RottingAuraResolvedFact fact = new(
                Source,
                Target,
                new RollResult(new DiceExpression(1, 6), new[] { 3 }),
                damage,
                weaknesses,
                resistances,
                new DamageOutcome(4, 0, 4)
            );

            damage[0] = new TypedDamagePart("fire", 99, Array.Empty<string>());
            weaknesses[0] = new TypedDefenseAdjustment("fire", 99);
            resistances.Clear();

            Assert.That(fact.Damage, Is.EqualTo(new[] { originalDamage }));
            Assert.That(fact.Weaknesses, Is.EqualTo(new[] { weakness }));
            Assert.That(fact.Resistances, Is.EqualTo(new[] { resistance }));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<TypedDamagePart>)fact.Damage)[0] = damage[0]
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<TypedDefenseAdjustment>)fact.Weaknesses).Clear()
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<TypedDefenseAdjustment>)fact.Resistances).Clear()
            );
        }

        [Test]
        public async Task TickUsesSourceLevelTypedDefensesAndAuthoritativeHealth()
        {
            ScriptedRollService rolls = new(3, 4);
            RuleDispatcher dispatcher = CreateTickDispatcher(rolls, 20);

            ResolvedOpResult<DamageOutcome> result = RequireResolved(
                await dispatcher.Dispatch(
                    new ApplyRottingAuraTickOp(
                        Source,
                        Target,
                        6,
                        new[] { new TypedDefenseAdjustment("void", 2) },
                        new[] { new TypedDefenseAdjustment("void", 1) }
                    )
                )
            );

            Assert.That(result.Value.Requested, Is.EqualTo(8));
            Assert.That(result.Value.Applied, Is.EqualTo(8));
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(12));
            Assert.That(result.Facts.OfType<DamageAppliedFact>().Single().Applied, Is.EqualTo(8));
            RottingAuraResolvedFact aura = result.Facts.OfType<RottingAuraResolvedFact>().Single();
            Assert.That(aura.Source, Is.EqualTo(Source));
            Assert.That(aura.Target, Is.EqualTo(Target));
            Assert.That(aura.Roll.Dice, Is.EqualTo(new DiceExpression(2, 6)));
            Assert.That(aura.Roll.Values, Is.EqualTo(new[] { 3, 4 }));
            Assert.That(aura.Damage.Single().Amount, Is.EqualTo(8));
            Assert.That(((IList<TypedDamagePart>)aura.Damage).IsReadOnly, Is.True);
            Assert.That(aura.Outcome, Is.EqualTo(result.Value));
            Assert.That(rolls.Remaining, Is.Zero);
            Assert.That(dispatcher.Diagnostics.Compact, Does.Contain("ApplyRottingAuraTickOp"));
        }

        [Test]
        public async Task TickClampsNegativeLevelAndFullyResistedDamageToZero()
        {
            ScriptedRollService rolls = new(4);
            RuleDispatcher dispatcher = CreateTickDispatcher(rolls, 20);

            ResolvedOpResult<DamageOutcome> result = RequireResolved(
                await dispatcher.Dispatch(
                    new ApplyRottingAuraTickOp(
                        Source,
                        Target,
                        -10,
                        Array.Empty<TypedDefenseAdjustment>(),
                        new[] { new TypedDefenseAdjustment("void", 5) }
                    )
                )
            );

            Assert.That(result.Value.Requested, Is.Zero);
            Assert.That(result.Value.Applied, Is.Zero);
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(result.Facts.OfType<DamageAppliedFact>(), Is.Empty);
            RottingAuraResolvedFact aura = result.Facts.OfType<RottingAuraResolvedFact>().Single();
            Assert.That(aura.Roll.Total, Is.EqualTo(4));
            Assert.That(aura.Damage.Single().Amount, Is.Zero);
            Assert.That(aura.Outcome.Applied, Is.Zero);
            Assert.That(rolls.Remaining, Is.Zero);
        }

        [TestCase("undead")]
        [TestCase("construct")]
        public async Task TurnListenerRejectsExcludedTargetTraits(string trait)
        {
            TestDataProvider data = new(
                new RottingAuraTurnData(
                    new[] { Trait.FromSlug(trait) },
                    Array.Empty<TypedDefenseAdjustment>(),
                    Array.Empty<TypedDefenseAdjustment>(),
                    new[] { new RottingAuraSource(Source, 1) }
                )
            );
            ScriptedRollService rolls = new(6);
            RuleDispatcher dispatcher = CreateTurnDispatcher(data, rolls, 10);

            await dispatcher.Dispatch(new EmitTurnBeganOp(Turn));

            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(10));
            Assert.That(rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task TurnListenerSkipsFullHealthBeforeReadingUnityData()
        {
            TestDataProvider data = DataFor(Source);
            RuleDispatcher dispatcher = CreateTurnDispatcher(data, new ScriptedRollService(6), 20);

            await dispatcher.Dispatch(new EmitTurnBeganOp(Turn));

            Assert.That(data.CaptureCalls, Is.Zero);
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(20));
        }

        [Test]
        public async Task StaleTurnFactDoesNotCaptureDataOrApplyTicks()
        {
            TestDataProvider data = DataFor(Source);
            ScriptedRollService rolls = new(6);
            RuleDispatcher dispatcher = CreateTurnDispatcher(data, rolls, 10);
            TurnIdentity stale = new(Encounter, new TurnId(2), Target, RoundNumber.First, 0);

            await dispatcher.Dispatch(new EmitTurnBeganOp(stale));

            Assert.That(data.CaptureCalls, Is.Zero);
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(10));
            Assert.That(rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task AuraTickSettlesBeforeTurnResourcesRegainedFact()
        {
            TestDataProvider data = DataFor(Source);
            RuleDispatcher dispatcher = CreateTurnDispatcher(data, new ScriptedRollService(2), 10);
            FactOrderObserver order = new();
            using IDisposable registration = dispatcher.RegisterFactObserver<RuleFact>(order);

            await dispatcher.Dispatch(new EmitTurnBeganOp(Turn));
            await dispatcher.Dispatch(new EmitResourcesRegainedOp(Turn));

            Assert.That(
                order.Types.IndexOf(typeof(RottingAuraResolvedFact)),
                Is.LessThan(order.Types.IndexOf(typeof(TurnResourcesRegainedFact)))
            );
        }

        [Test]
        public async Task SourcesResolveInProviderOrderAndStopAfterTargetReachesZero()
        {
            TestDataProvider data = DataFor(Source, SecondSource);
            ScriptedRollService rolls = new(5, 6);
            RuleDispatcher dispatcher = CreateTurnDispatcher(data, rolls, 5);
            RecordingAuraObserver observer = new();
            using IDisposable registration = dispatcher.RegisterFactObserver(observer);

            await dispatcher.Dispatch(new EmitTurnBeganOp(Turn));

            Assert.That(observer.Sources, Is.EqualTo(new[] { Source }));
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.Zero);
            Assert.That(rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task DefeatedSourceIsSkippedWithoutChangingRemainingSourceOrder()
        {
            TestDataProvider data = DataFor(Source, SecondSource);
            RuleDispatcher dispatcher = CreateTurnDispatcher(
                data,
                new ScriptedRollService(4),
                10,
                firstSourceHitPoints: 0
            );
            RecordingAuraObserver observer = new();
            using IDisposable registration = dispatcher.RegisterFactObserver(observer);

            await dispatcher.Dispatch(new EmitTurnBeganOp(Turn));

            Assert.That(observer.Sources, Is.EqualTo(new[] { SecondSource }));
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(6));
        }

        [Test]
        public async Task ZeroHpListenerMayRecoverTargetBeforeTheNextOrderedSource()
        {
            TestDataProvider data = DataFor(Source, SecondSource);
            RecoverOnceListener recovery = new();
            RuleDispatcher dispatcher = CreateTurnDispatcher(
                data,
                new ScriptedRollService(5, 1),
                5,
                recovery
            );
            RecordingAuraObserver observer = new();
            using IDisposable registration = dispatcher.RegisterFactObserver(observer);

            await dispatcher.Dispatch(new EmitTurnBeganOp(Turn));

            Assert.That(recovery.Calls, Is.EqualTo(2));
            Assert.That(observer.Sources, Is.EqualTo(new[] { Source, SecondSource }));
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.Zero);
        }

        [Test]
        public void ThrowingPresentationObserverCannotStopCommittedHealthOrLaterSources()
        {
            TestDataProvider data = DataFor(Source, SecondSource);
            RuleDispatcher dispatcher = CreateTurnDispatcher(
                data,
                new ScriptedRollService(2, 3),
                10
            );
            RecordingAuraObserver recording = new();
            using IDisposable failed = dispatcher.RegisterFactObserver(new ThrowingAuraObserver());
            using IDisposable retained = dispatcher.RegisterFactObserver(recording);

            Assert.DoesNotThrowAsync(async () =>
                await dispatcher.Dispatch(new EmitTurnBeganOp(Turn))
            );

            Assert.That(recording.Sources, Is.EqualTo(new[] { Source, SecondSource }));
            Assert.That(dispatcher.Snapshot.Health[Target].Current, Is.EqualTo(5));
        }

        private static RuleDispatcher CreateTickDispatcher(IRollService rolls, int targetHitPoints)
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Source, Enemies))
                .SeedCreature(new CreatureState(Target, Players))
                .SeedHealth(Source, new HealthState(20, 20))
                .SeedHealth(Target, new HealthState(targetHitPoints, 20));
            return new RuleDispatcherBuilder(new InMemoryRulesStore(seed), rolls)
                .UseHealthRules()
                .UseRottingAuraRules()
                .Build();
        }

        private static TestDataProvider DataFor(params CreatureId[] sources) =>
            new(
                new RottingAuraTurnData(
                    Array.Empty<Trait>(),
                    Array.Empty<TypedDefenseAdjustment>(),
                    Array.Empty<TypedDefenseAdjustment>(),
                    sources.Select(source => new RottingAuraSource(source, 1))
                )
            );

        private static RuleDispatcher CreateTurnDispatcher(
            TestDataProvider data,
            IRollService rolls,
            int targetHitPoints,
            RecoverOnceListener recovery = null,
            int firstSourceHitPoints = 20
        )
        {
            RuleRegistryBuilder registryBuilder = new();
            RottingAuraRules.DefineRuleBinding(registryBuilder, data);
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Target, Players))
                .SeedCreature(new CreatureState(Source, Enemies))
                .SeedCreature(new CreatureState(SecondSource, Enemies))
                .SeedHealth(Target, new HealthState(targetHitPoints, 20))
                .SeedHealth(Source, new HealthState(firstSourceHitPoints, 20))
                .SeedHealth(SecondSource, new HealthState(20, 20))
                .SeedRuleBinding(RottingAuraRules.CreateBinding(Target))
                .SeedEncounter(
                    new EncounterState(
                        Encounter,
                        EncounterPhase.Active,
                        Players,
                        RoundNumber.First,
                        new[]
                        {
                            Entry(Target, Players, 20, 0),
                            Entry(Source, Enemies, 10, 1),
                            Entry(SecondSource, Enemies, 9, 2),
                        },
                        0,
                        Turn,
                        2,
                        null
                    )
                );
            if (recovery != null)
            {
                RuleDefinitionId recoveryDefinition = new("aura-recovery-test");
                registryBuilder
                    .Define(recoveryDefinition)
                    .FactListener(RuleLifecyclePhase.Reaction, recovery);
                seed.SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId("aura-recovery-test-binding"),
                        recoveryDefinition,
                        Target,
                        default,
                        TestSource,
                        0
                    )
                );
            }
            RuleRegistry registry = registryBuilder.Build();
            return new RuleDispatcherBuilder(new InMemoryRulesStore(seed), rolls)
                .UseRuleRegistry(registry)
                .UseHealthRules()
                .UseRottingAuraRules()
                .RegisterHandler<EmitTurnBeganOp, bool>(new EmitTurnBeganHandler())
                .RegisterHandler<EmitResourcesRegainedOp, bool>(new EmitResourcesRegainedHandler())
                .RegisterReducer<CommitTurnBeganOp, bool>(new CommitTurnBeganReducer(), TestSource)
                .RegisterReducer<CommitResourcesRegainedOp, bool>(
                    new CommitResourcesRegainedReducer(),
                    TestSource
                )
                .Build();
        }

        private static InitiativeEntry Entry(
            CreatureId creature,
            PlayerId team,
            int roll,
            long order
        ) => new(creature, team, roll, 0, order, RoundNumber.First);

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private sealed class TestDataProvider : IRottingAuraDataProvider
        {
            private readonly RottingAuraTurnData data;

            public TestDataProvider(RottingAuraTurnData data) => this.data = data;

            public int CaptureCalls { get; private set; }

            public RottingAuraTurnData Capture(
                RulesSnapshot snapshot,
                EncounterId encounter,
                CreatureId target
            )
            {
                CaptureCalls++;
                return data;
            }
        }

        private sealed class RecordingAuraObserver : IFactObserver<RottingAuraResolvedFact>
        {
            public List<CreatureId> Sources { get; } = new();

            public void OnFactCommitted(
                RottingAuraResolvedFact fact,
                OpId observationRootId,
                RulesSnapshot currentSnapshot
            ) => Sources.Add(fact.Source);
        }

        private sealed class ThrowingAuraObserver : IFactObserver<RottingAuraResolvedFact>
        {
            public void OnFactCommitted(
                RottingAuraResolvedFact fact,
                OpId observationRootId,
                RulesSnapshot currentSnapshot
            ) => throw new InvalidOperationException("Synthetic aura presentation failure.");
        }

        private sealed class FactOrderObserver : IFactObserver<RuleFact>
        {
            public List<Type> Types { get; } = new();

            public void OnFactCommitted(
                RuleFact fact,
                OpId observationRootId,
                RulesSnapshot currentSnapshot
            ) => Types.Add(fact.GetType());
        }

        private sealed class RecoverOnceListener : IRuleFactListener<CreatureReducedToZeroFact>
        {
            private bool recovered;

            public int Calls { get; private set; }

            public async ValueTask OnFactCommitted(
                CreatureReducedToZeroFact fact,
                FactContext context
            )
            {
                Calls++;
                if (recovered)
                    return;
                recovered = true;
                await context.Dispatch(
                    new ApplyHealingOp(
                        fact.Creature,
                        1,
                        new HealthChangeOriginId("aura-recovery"),
                        TestSource
                    )
                );
            }
        }

        private sealed class EmitTurnBeganOp : IRuleOp<bool>
        {
            public EmitTurnBeganOp(TurnIdentity turn) => Turn = turn;

            public TurnIdentity Turn { get; }
        }

        private sealed class CommitTurnBeganOp : IRuleOp<bool>, IRuleSourcedOp
        {
            public CommitTurnBeganOp(TurnIdentity turn) => Turn = turn;

            public TurnIdentity Turn { get; }
            public RuleSource Source => TestSource;
        }

        private sealed class CommitResourcesRegainedOp : IRuleOp<bool>, IRuleSourcedOp
        {
            public CommitResourcesRegainedOp(TurnIdentity turn) => Turn = turn;

            public TurnIdentity Turn { get; }
            public RuleSource Source => TestSource;
        }

        private sealed class EmitResourcesRegainedOp : IRuleOp<bool>
        {
            public EmitResourcesRegainedOp(TurnIdentity turn) => Turn = turn;

            public TurnIdentity Turn { get; }
        }

        private sealed class EmitTurnBeganHandler : IOpHandler<EmitTurnBeganOp, bool>
        {
            public async ValueTask<bool> Handle(
                OpFrame<EmitTurnBeganOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(await context.Dispatch(new CommitTurnBeganOp(frame.Op.Turn))).Value;
        }

        private sealed class EmitResourcesRegainedHandler
            : IOpHandler<EmitResourcesRegainedOp, bool>
        {
            public async ValueTask<bool> Handle(
                OpFrame<EmitResourcesRegainedOp> frame,
                OpHandlerContext context
            ) =>
                RequireResolved(
                    await context.Dispatch(new CommitResourcesRegainedOp(frame.Op.Turn))
                ).Value;
        }

        private sealed class CommitTurnBeganReducer : IOpReducer<CommitTurnBeganOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<CommitTurnBeganOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                facts.Stage(new TurnBeganFact(context.Op.Turn));
                return ReductionResult<bool>.Accept(true);
            }
        }

        private sealed class CommitResourcesRegainedReducer
            : IOpReducer<CommitResourcesRegainedOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<CommitResourcesRegainedOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                facts.Stage(new TurnResourcesRegainedFact(context.Op.Turn));
                return ReductionResult<bool>.Accept(true);
            }
        }
    }
}
