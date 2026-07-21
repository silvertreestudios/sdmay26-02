using System;
using System.Reflection;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class UnityEncounterRulesBridgeTests
{
    [Test]
    public void BridgeRejectsEmptyEncounterWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityEncounterRulesBridge.Create(Array.Empty<ActionController>(), "Players")
        );

        StringAssert.Contains("requires unique non-null controllers", error.Message);
    }

    [Test]
    public void BridgeRejectsNullEncounterCreatureWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityEncounterRulesBridge.Create(new ActionController[] { null }, "Players")
        );

        StringAssert.Contains("requires unique non-null controllers", error.Message);
    }

    [Test]
    public void CreatureHealthCommandsRejectMissingEncounterBridge()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                creature.ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-damage"))
            );

            StringAssert.Contains("require an encounter health bridge", error.Message);
            Assert.That(creature.Health, Is.EqualTo(new HealthState(10, 10)));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public async Task BridgeOwnsHealthAndProjectsCommittedFactsBackToComponents()
    {
        GameObject firstObject = new GameObject("first");
        GameObject secondObject = new GameObject("second");
        try
        {
            CreatureComponent first = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent second = secondObject.AddComponent<CreatureComponent>();
            TestActionController firstController = PrepareController(firstObject);
            TestActionController secondController = PrepareController(secondObject);
            first.InitializeHealthBeforeEncounter(10, 12);
            second.InitializeHealthBeforeEncounter(7, 7);
            UnityEncounterRulesBridge bridge = UnityEncounterRulesBridge.Create(
                new ActionController[] { firstController, secondController },
                "Players"
            );

            CreatureId firstId = bridge.GetCreatureId(first);
            CreatureId secondId = bridge.GetCreatureId(second);
            DamageOutcome damage = await bridge.ApplyFinalDamageAsync(
                firstId,
                4,
                RuleSource.FromSlug("test-strike")
            );
            HealingOutcome healing = await bridge.ApplyHealingAsync(
                firstId,
                2,
                RuleSource.FromSlug("test-heal")
            );

            Assert.That(firstId.Value, Is.EqualTo("encounter-creature-1"));
            Assert.That(secondId.Value, Is.EqualTo("encounter-creature-2"));
            Assert.That(damage.Applied, Is.EqualTo(4));
            Assert.That(healing.Applied, Is.EqualTo(2));
            Assert.That(first.hp, Is.EqualTo(8));
            Assert.That(first.maxHp, Is.EqualTo(12));
            Assert.That(bridge.Snapshot.Health[firstId].Current, Is.EqualTo(first.hp));
            Assert.That(
                bridge.TryGetOriginSource(
                    new HealthChangeOriginId("health-origin-1"),
                    out RuleSource originSource
                ),
                Is.True
            );
            Assert.That(originSource, Is.EqualTo(RuleSource.FromSlug("test-strike")));
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(secondObject);
        }
    }

    [Test]
    public async Task BridgeProjectsSourceTemporaryHitPointStateAndImmunity()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            TestActionController controller = PrepareController(creatureObject);
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityEncounterRulesBridge bridge = UnityEncounterRulesBridge.Create(
                new ActionController[] { controller },
                "Players"
            );
            CreatureId id = bridge.GetCreatureId(creature);
            RuleSource rage = RuleSource.FromSlug("rage");

            await creature.GrantSourceTemporaryHitPointsAsync(rage, 4);
            await creature.ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-damage"));
            await creature.RemoveSourceTemporaryHitPointsAsync(rage);
            await creature.AddTemporaryHitPointImmunityAsync(rage);
            TemporaryHitPointsGrantOutcome blocked =
                await creature.GrantSourceTemporaryHitPointsAsync(rage, 5);

            Assert.That(blocked.Immune, Is.True);
            Assert.That(creature.tempHp, Is.Zero);
            Assert.That(bridge.Snapshot.Health[id].Temporary, Is.Zero);
            Assert.That(creature.HasTempHpImmunity("rage"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void BridgePropagatesCompletedDispatcherFailure()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            TestActionController controller = PrepareController(creatureObject);
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityEncounterRulesBridge bridge = UnityEncounterRulesBridge.Create(
                new ActionController[] { controller },
                "Players"
            );
            InvalidOperationException expected = new InvalidOperationException(
                "completed observer failure"
            );
            GetDispatcher(bridge)
                .RegisterFactObserver<HealthFact>(new CompletedFailureObserver(expected));

            InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await bridge.ApplyFinalDamageAsync(
                        bridge.GetCreatureId(creature),
                        1,
                        RuleSource.FromSlug("test-damage")
                    )
            );

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(
                GetProjectedCurrentHealth(creature),
                Is.EqualTo(9),
                "Committed presentation must drain for its exact failed root before ownership releases."
            );
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public async Task AwaitedPortsSerializeIncompleteObserversAndDrainPresentation()
    {
        GameObject creatureObject = new GameObject("creature");
        IncompleteObserver observer = new IncompleteObserver();
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            TestActionController controller = PrepareController(creatureObject);
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityEncounterRulesBridge bridge = UnityEncounterRulesBridge.Create(
                new ActionController[] { controller },
                "Players"
            );
            GetDispatcher(bridge).RegisterFactObserver<HealthFact>(observer);

            CreatureId creatureId = bridge.GetCreatureId(creature);
            ValueTask<DamageOutcome> pending = bridge.ApplyFinalDamageAsync(
                creatureId,
                2,
                RuleSource.FromSlug("test-damage")
            );
            long versionAfterDamageCommit = bridge.Snapshot.Version;
            ValueTask<HealingOutcome> queued = bridge.ApplyHealingAsync(
                creatureId,
                1,
                RuleSource.FromSlug("test-healing")
            );

            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(queued.IsCompleted, Is.False);
            Assert.That(bridge.Snapshot.Version, Is.EqualTo(versionAfterDamageCommit));
            Assert.That(bridge.Snapshot.Health[creatureId].Current, Is.EqualTo(8));
            Assert.That(GetProjectedCurrentHealth(creature), Is.EqualTo(10));
            observer.Complete();
            await pending;
            await queued;
            Assert.That(bridge.Snapshot.Health[creatureId].Current, Is.EqualTo(9));
            Assert.That(GetProjectedCurrentHealth(creature), Is.EqualTo(9));
        }
        finally
        {
            observer.Complete();
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public async Task OverlappingRootsDrainOnlyTheirOwnPresentationAfterListenersSettle()
    {
        GameObject creatureObject = new GameObject("root-scoped presentation creature");
        SecondHealthFactObserver observer = new SecondHealthFactObserver();
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            TestActionController controller = PrepareController(creatureObject);
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityEncounterRulesBridge bridge = UnityEncounterRulesBridge.Create(
                new ActionController[] { controller },
                "Players"
            );
            GetDispatcher(bridge).RegisterFactObserver<HealthFact>(observer);
            CreatureId id = bridge.GetCreatureId(creature);

            Task<DamageOutcome> first = bridge
                .ApplyFinalDamageAsync(id, 1, RuleSource.FromSlug("first-root"))
                .AsTask();
            Task<DamageOutcome> second = bridge
                .ApplyFinalDamageAsync(id, 1, RuleSource.FromSlug("second-root"))
                .AsTask();
            await observer.SecondStarted;

            Assert.That(bridge.Snapshot.Health[id].Current, Is.EqualTo(8));
            Assert.That(
                GetProjectedCurrentHealth(creature),
                Is.EqualTo(9),
                "The first caller must not drain presentation owned by the paused second root."
            );
            Assert.That(second.IsCompleted, Is.False);

            observer.ReleaseSecond();
            await first;
            await second;

            Assert.That(GetProjectedCurrentHealth(creature), Is.EqualTo(8));
            Assert.That(observer.Calls, Is.EqualTo(2));
        }
        finally
        {
            observer.ReleaseSecond();
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public async Task ReactionHealedCreatureRemainsActiveAndSkipsDefeatPresentation()
    {
        GameObject heroObject = new GameObject("reaction-healed hero");
        GameObject enemyObject = new GameObject("living enemy");
        try
        {
            CreatureComponent hero = heroObject.AddComponent<CreatureComponent>();
            CreatureComponent enemy = enemyObject.AddComponent<CreatureComponent>();
            hero.InitializeHealthBeforeEncounter(1, 10);
            enemy.InitializeHealthBeforeEncounter(10, 10);
            TestActionController heroController = PrepareController(heroObject, "Players");
            TestActionController enemyController = PrepareController(enemyObject, "Enemies");
            RuleSource source = RuleSource.FromSlug("reaction-heal-test");
            RuleDefinitionId rescueDefinition = new RuleDefinitionId("reaction-heal");
            RescueListener rescue = new RescueListener(source);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder().AddOutcomeRule();
            registryBuilder
                .Define(rescueDefinition)
                .FactListener(RuleLifecyclePhase.Reaction, rescue);
            RuleRegistry registry = registryBuilder.Build();
            ActiveRuleBinding rescueBinding = new ActiveRuleBinding(
                new BindingId("reaction-heal-binding"),
                rescueDefinition,
                new CreatureId("encounter-creature-1"),
                null,
                source,
                1
            );
            UnityEncounterRulesBridge bridge = UnityEncounterRulesBridge.CreateWithRuleComposition(
                new ActionController[] { heroController, enemyController },
                "Players",
                new ScriptedRollService(20, 10),
                registry,
                new[] { rescueBinding }
            );
            CreatureId heroId = bridge.GetCreatureId(hero);
            ProjectionSettlementObserver projection = new ProjectionSettlementObserver(
                hero,
                heroId
            );
            GetDispatcher(bridge).RegisterRootSettlementObserver(projection);
            await bridge.StartEncounter(new ActionController[] { heroController, enemyController });
            projection.Clear();

            await hero.ApplyFinalDamageAsync(1, source);

            EncounterState encounter = bridge.Snapshot.Encounters[bridge.EncounterId];
            Assert.That(rescue.Calls, Is.EqualTo(1));
            Assert.That(
                projection.Calls,
                Is.GreaterThanOrEqualTo(2),
                "Causal healing and its owning damage root must settle independently."
            );
            Assert.That(projection.SawMultipleRoots, Is.True);
            Assert.That(
                projection.AllProjectionsMatched,
                Is.True,
                "Every root settlement must leave Unity health equal to the latest shared snapshot."
            );
            Assert.That(bridge.Snapshot.Health[heroId].Current, Is.EqualTo(1));
            Assert.That(hero.Health, Is.EqualTo(bridge.Snapshot.Health[heroId]));
            Assert.That(GetProjectedCurrentHealth(hero), Is.EqualTo(1));
            Assert.That(hero.IsDefeated, Is.False);
            Assert.That(heroObject.activeSelf, Is.True);
            Assert.That(heroController.enabled, Is.True);
            Assert.That(encounter.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(encounter.Outcome, Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(heroObject);
            Object.DestroyImmediate(enemyObject);
        }
    }

    [Test]
    public async Task RejectedJoinLeavesRulesStateIdentityMapsAndAttachmentsUnchanged()
    {
        GameObject heroObject = new GameObject("hero");
        GameObject enemyObject = new GameObject("enemy");
        GameObject reinforcementObject = new GameObject("reinforcement");
        try
        {
            CreatureComponent hero = heroObject.AddComponent<CreatureComponent>();
            CreatureComponent enemy = enemyObject.AddComponent<CreatureComponent>();
            CreatureComponent reinforcement = reinforcementObject.AddComponent<CreatureComponent>();
            hero.InitializeHealthBeforeEncounter(10, 10);
            enemy.InitializeHealthBeforeEncounter(10, 10);
            reinforcement.InitializeHealthBeforeEncounter(8, 8);
            TestActionController heroController = PrepareController(heroObject, "Players");
            TestActionController enemyController = PrepareController(enemyObject, "Enemies");
            TestActionController reinforcementController = PrepareController(
                reinforcementObject,
                "Enemies"
            );
            UnityEncounterRulesBridge bridge = UnityEncounterRulesBridge.Create(
                new ActionController[] { heroController, enemyController },
                "Players",
                new ScriptedRollService(20, 10, 15, 5)
            );
            await bridge.StartEncounter(new ActionController[] { heroController, enemyController });
            EncounterState before = bridge.Snapshot.Encounters[bridge.EncounterId];
            long version = bridge.Snapshot.Version;
            int hostPublications = 0;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await bridge.JoinEncounter(
                    new ActionController[] { reinforcementController, heroController },
                    () => hostPublications++
                )
            );

            Assert.That(hostPublications, Is.Zero);
            Assert.That(bridge.Snapshot.Version, Is.EqualTo(version));
            Assert.That(
                bridge.Snapshot.Encounters[bridge.EncounterId].Roster,
                Is.EqualTo(before.Roster)
            );
            Assert.Throws<InvalidOperationException>(() =>
                bridge.GetCreatureId(reinforcementController)
            );
            Assert.Throws<InvalidOperationException>(() =>
                reinforcement.ApplyFinalDamageAsync(1, RuleSource.FromSlug("rejected-join"))
            );
        }
        finally
        {
            Object.DestroyImmediate(heroObject);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    private static RuleDispatcher GetDispatcher(UnityEncounterRulesBridge bridge)
    {
        FieldInfo field = typeof(UnityEncounterRulesBridge).GetField(
            "dispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (RuleDispatcher)field.GetValue(bridge);
    }

    private static int GetProjectedCurrentHealth(CreatureComponent creature)
    {
        FieldInfo field = typeof(CreatureComponent).GetField(
            "_hp",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (int)field.GetValue(creature);
    }

    private static TestActionController PrepareController(GameObject obj) =>
        PrepareController(obj, "Players");

    private static TestActionController PrepareController(GameObject obj, string teamName)
    {
        Team team = obj.AddComponent<Team>();
        team.Name = teamName;
        return obj.AddComponent<TestActionController>();
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class CompletedFailureObserver : IFactObserver<HealthFact>
    {
        private readonly Exception failure;

        public CompletedFailureObserver(Exception failure) => this.failure = failure;

        public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot) =>
            new ValueTask(Task.FromException(failure));
    }

    private sealed class IncompleteObserver : IFactObserver<HealthFact>
    {
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot) =>
            new ValueTask(completion.Task);

        public void Complete() => completion.TrySetResult(true);
    }

    private sealed class SecondHealthFactObserver : IFactObserver<HealthFact>
    {
        private readonly TaskCompletionSource<bool> secondStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> releaseSecond = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int Calls { get; private set; }
        public Task SecondStarted => secondStarted.Task;

        public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot)
        {
            Calls++;
            if (Calls != 2)
                return default;
            secondStarted.TrySetResult(true);
            return new ValueTask(releaseSecond.Task);
        }

        public void ReleaseSecond() => releaseSecond.TrySetResult(true);
    }

    private sealed class ProjectionSettlementObserver : IRootSettlementObserver
    {
        private readonly CreatureComponent creature;
        private readonly CreatureId creatureId;
        private OpId firstRoot;
        private OpId lastRoot;

        public ProjectionSettlementObserver(CreatureComponent creature, CreatureId creatureId)
        {
            this.creature = creature;
            this.creatureId = creatureId;
            Clear();
        }

        public int Calls { get; private set; }
        public bool AllProjectionsMatched { get; private set; }
        public bool SawMultipleRoots => Calls > 1 && firstRoot != lastRoot;

        /// <inheritdoc/>
        public ValueTask OnRootSettled(OpId rootId, RulesSnapshot snapshot)
        {
            if (!snapshot.Health.TryGet(creatureId, out HealthState health))
                return default;
            if (Calls == 0)
                firstRoot = rootId;
            lastRoot = rootId;
            Calls++;
            AllProjectionsMatched &= GetProjectedCurrentHealth(creature) == health.Current;
            return default;
        }

        public void Clear()
        {
            Calls = 0;
            AllProjectionsMatched = true;
            firstRoot = default;
            lastRoot = default;
        }
    }

    private sealed class RescueListener : IRuleFactListener<CreatureReducedToZeroFact>
    {
        private readonly RuleSource source;

        public RescueListener(RuleSource source) => this.source = source;

        public int Calls { get; private set; }

        public async ValueTask OnFactCommitted(CreatureReducedToZeroFact fact, FactContext context)
        {
            Calls++;
            await context.Dispatch(
                new ApplyHealingOp(
                    fact.Creature,
                    1,
                    new HealthChangeOriginId("reaction-heal"),
                    source
                )
            );
        }
    }
}
