using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

public sealed class Pf2eRottingAuraTests
{
    [Test]
    public async Task ResolverStopsAfterProjectedLethalDamageBeforeLaterAuraResolution()
    {
        GameObject firstSourceObject = new("First Aura Source");
        GameObject targetObject = new("Aura Target");
        GameObject secondSourceObject = new("Second Aura Source");
        try
        {
            CreatureComponent firstSourceCreature =
                firstSourceObject.AddComponent<CreatureComponent>();
            CreatureComponent targetCreature = targetObject.AddComponent<CreatureComponent>();
            CreatureComponent secondSourceCreature =
                secondSourceObject.AddComponent<CreatureComponent>();
            TestActionController firstSource =
                firstSourceObject.AddComponent<TestActionController>();
            TestActionController targetController =
                targetObject.AddComponent<TestActionController>();
            TestActionController secondSource =
                secondSourceObject.AddComponent<TestActionController>();
            firstSourceCreature.auras = new List<CreatureAura>
            {
                new() { slug = RottingAuraRule.RuleSlug, radiusFeet = 10 },
            };
            secondSourceCreature.auras = new List<CreatureAura>
            {
                new() { slug = RottingAuraRule.RuleSlug, radiusFeet = 10 },
            };
            targetCreature.InitializeHealthBeforeEncounter(1, 2, 1);
            targetCreature.traits = new List<string>();
            targetCreature.weaknesses = new List<DamageValue>();
            targetCreature.resistances = new List<DamageValue>();
            firstSourceObject.transform.position = Vector3Int.zero;
            targetObject.transform.position = Vector3Int.right;
            secondSourceObject.transform.position = new Vector3Int(2, 0, 0);
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
                { new Tile() },
            };
            tiles[0, 0].Occupants.Add(firstSourceObject);
            tiles[1, 0].Occupants.Add(targetObject);
            tiles[2, 0].Occupants.Add(secondSourceObject);
            CountingDiceRoller dice = new(2);
            List<CreatureAuraEffectResult> committed = new();
            List<CreatureAuraEffectResult> presented = new();
            int commitCalls = 0;

            List<CreatureAuraEffectResult> results =
                await CreatureAuraResolver.ApplyTurnStartAurasAwaited(
                    targetController,
                    new ActionController[] { firstSource, targetController, secondSource },
                    tiles,
                    (committedTarget, batch) =>
                    {
                        commitCalls++;
                        Assert.That(committedTarget, Is.SameAs(targetCreature));
                        committed.AddRange(batch);
                        IReadOnlyList<DamageOutcome> outcomes = new[]
                        {
                            new DamageOutcome(2, 1, 1),
                        };
                        return new ValueTask<IReadOnlyList<DamageOutcome>>(outcomes);
                    },
                    _ => new HealthState(1, 2, 1),
                    result =>
                    {
                        presented.Add(result);
                        return default;
                    },
                    dice
                );

            Assert.That(dice.RollCalls, Is.EqualTo(1));
            Assert.That(commitCalls, Is.EqualTo(1));
            Assert.That(committed, Has.Count.EqualTo(1));
            Assert.That(committed[0].Source, Is.SameAs(firstSourceObject));
            Assert.That(presented, Is.EqualTo(committed));
            Assert.That(results, Is.EqualTo(committed));
            Assert.That(presented.Exists(result => result.Source == secondSourceObject), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(firstSourceObject);
            Object.DestroyImmediate(targetObject);
            Object.DestroyImmediate(secondSourceObject);
        }
    }

    [Test]
    public void ResolveCalculatesAdjustedDamageWithoutMutatingTargetHealth()
    {
        GameObject sourceObject = new("Aura Source");
        GameObject targetObject = new("Aura Target");
        try
        {
            CreatureComponent source = sourceObject.AddComponent<CreatureComponent>();
            CreatureComponent target = targetObject.AddComponent<CreatureComponent>();
            TestActionController sourceController =
                sourceObject.AddComponent<TestActionController>();
            TestActionController targetController =
                targetObject.AddComponent<TestActionController>();
            source.level = 6;
            target.InitializeHealthBeforeEncounter(5, 10);
            target.weaknesses = new List<DamageValue> { new("void", 2) };
            target.resistances = new List<DamageValue>();
            CreatureAura aura = new() { slug = RottingAuraRule.RuleSlug, radiusFeet = 10 };
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
            };
            AreaTargetResult area = new()
            {
                Creatures = new List<AreaAffectedCreature>
                {
                    new() { Creature = targetObject, Cell = new Vector3Int(1, 0, 0) },
                },
            };
            CreatureAuraContext context = new(
                sourceController,
                targetController,
                source,
                target,
                aura,
                tiles,
                area,
                new FixedDiceRoller(4)
            );
            RottingAuraRule rule = new();

            CreatureAuraEffectResult result = rule.Resolve(context);

            Assert.That(result.RolledDamage, Is.EqualTo(4));
            Assert.That(result.AppliedDamage, Is.EqualTo(6));
            Assert.That(target.hp, Is.EqualTo(5));
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void CanAffectRejectsFullHealthUndeadAndConstructTargets()
    {
        GameObject sourceObject = new("Aura Source");
        GameObject targetObject = new("Aura Target");
        try
        {
            CreatureComponent source = sourceObject.AddComponent<CreatureComponent>();
            CreatureComponent target = targetObject.AddComponent<CreatureComponent>();
            TestActionController sourceController =
                sourceObject.AddComponent<TestActionController>();
            TestActionController targetController =
                targetObject.AddComponent<TestActionController>();
            target.InitializeHealthBeforeEncounter(10, 10);
            CreatureAuraContext context = new(
                sourceController,
                targetController,
                source,
                target,
                new CreatureAura { slug = RottingAuraRule.RuleSlug, radiusFeet = 10 },
                new[,]
                {
                    { new Tile() },
                },
                new AreaTargetResult(),
                new FixedDiceRoller(1)
            );
            RottingAuraRule rule = new();

            Assert.That(rule.CanAffect(context), Is.False);
            target.InitializeHealthBeforeEncounter(5, 10);
            target.traits = new List<string> { "undead" };
            Assert.That(rule.CanAffect(context), Is.False);
            target.traits = new List<string> { "construct" };
            Assert.That(rule.CanAffect(context), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }
    }

    private sealed class FixedDiceRoller : IPf2eDiceRoller
    {
        private readonly int result;

        public FixedDiceRoller(int result) => this.result = result;

        public int Roll(int numberOfDice, int sidesPerDie) => result;
    }

    private sealed class CountingDiceRoller : IPf2eDiceRoller
    {
        private readonly int result;

        public CountingDiceRoller(int result) => this.result = result;

        public int RollCalls { get; private set; }

        public int Roll(int numberOfDice, int sidesPerDie)
        {
            RollCalls++;
            return result;
        }
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
