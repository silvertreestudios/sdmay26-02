using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;

public sealed class UnityHealthRulesBridgeTests
{
    [Test]
    public void BridgeOwnsHealthAndProjectsCommittedFactsBackToComponents()
    {
        GameObject firstObject = new GameObject("first");
        GameObject secondObject = new GameObject("second");
        try
        {
            CreatureComponent first = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent second = secondObject.AddComponent<CreatureComponent>();
            first.InitializeHealth(10, 12);
            second.InitializeHealth(7, 7);
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
            creature.InitializeHealth(10, 10);
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
}
