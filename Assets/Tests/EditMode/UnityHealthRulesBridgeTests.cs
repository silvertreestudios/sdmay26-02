using System;
using System.Reflection;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class UnityHealthRulesBridgeTests
{
    [Test]
    public void BridgeRejectsEmptyEncounterWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityHealthRulesBridge.Create(Array.Empty<CreatureComponent>())
        );

        StringAssert.Contains("requires at least one creature", error.Message);
    }

    [Test]
    public void BridgeRejectsNullEncounterCreatureWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityHealthRulesBridge.Create(new CreatureComponent[] { null })
        );

        StringAssert.Contains("cannot contain a null creature", error.Message);
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
                creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-damage"))
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
    public void BridgeOwnsHealthAndProjectsCommittedFactsBackToComponents()
    {
        GameObject firstObject = new GameObject("first");
        GameObject secondObject = new GameObject("second");
        try
        {
            CreatureComponent first = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent second = secondObject.AddComponent<CreatureComponent>();
            first.InitializeHealthBeforeEncounter(10, 12);
            second.InitializeHealthBeforeEncounter(7, 7);
            UnityHealthRulesBridge bridge = UnityHealthRulesBridge.Create(new[] { first, second });

            CreatureId firstId = bridge.GetCreatureId(first);
            CreatureId secondId = bridge.GetCreatureId(second);
            DamageOutcome damage = bridge.ApplyFinalDamage(
                firstId,
                4,
                RuleSource.FromSlug("test-strike")
            );
            HealingOutcome healing = bridge.ApplyHealing(
                firstId,
                2,
                RuleSource.FromSlug("test-heal")
            );

            Assert.That(firstId.Value, Is.EqualTo("health-creature-1"));
            Assert.That(secondId.Value, Is.EqualTo("health-creature-2"));
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
    public void BridgeProjectsSourceTemporaryHitPointStateAndImmunity()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityHealthRulesBridge bridge = UnityHealthRulesBridge.Create(new[] { creature });
            CreatureId id = bridge.GetCreatureId(creature);
            RuleSource rage = RuleSource.FromSlug("rage");

            creature.GrantSourceTemporaryHitPoints(rage, 4);
            creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-damage"));
            creature.RemoveSourceTemporaryHitPoints(rage);
            creature.AddTemporaryHitPointImmunity(rage);
            TemporaryHitPointsGrantOutcome blocked = creature.GrantSourceTemporaryHitPoints(
                rage,
                5
            );

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
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityHealthRulesBridge bridge = UnityHealthRulesBridge.Create(new[] { creature });
            InvalidOperationException expected = new InvalidOperationException(
                "completed observer failure"
            );
            GetDispatcher(bridge)
                .RegisterFactObserver<HealthFact>(new CompletedFailureObserver(expected));

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyFinalDamage(
                    bridge.GetCreatureId(creature),
                    1,
                    RuleSource.FromSlug("test-damage")
                )
            );

            Assert.That(actual, Is.SameAs(expected));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void BridgeRejectsIncompleteDispatcherWork()
    {
        GameObject creatureObject = new GameObject("creature");
        IncompleteObserver observer = new IncompleteObserver();
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityHealthRulesBridge bridge = UnityHealthRulesBridge.Create(new[] { creature });
            GetDispatcher(bridge).RegisterFactObserver<HealthFact>(observer);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyFinalDamage(
                    bridge.GetCreatureId(creature),
                    1,
                    RuleSource.FromSlug("test-damage")
                )
            );

            StringAssert.Contains("must complete synchronously", error.Message);
        }
        finally
        {
            observer.Complete();
            Object.DestroyImmediate(creatureObject);
        }
    }

    private static RuleDispatcher GetDispatcher(UnityHealthRulesBridge bridge)
    {
        FieldInfo field = typeof(UnityHealthRulesBridge).GetField(
            "dispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (RuleDispatcher)field.GetValue(bridge);
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
}
