using System.Collections.Generic;
using Game.Creature;
using Game.Creature.Rules;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

public sealed class Pf2eRottingAuraTests
{
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

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
