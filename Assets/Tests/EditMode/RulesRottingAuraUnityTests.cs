using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class RulesRottingAuraUnityTests
{
    private static readonly CreatureId Source = new("unity-aura-source");
    private static readonly CreatureId Target = new("unity-aura-target");
    private static readonly EncounterId Encounter = new("unity-aura-encounter");
    private static readonly PlayerId Players = new("players");
    private static readonly PlayerId Enemies = new("enemies");

    [TestCase(10, true)]
    [TestCase(0, false)]
    [TestCase(-5, false)]
    public void VisualizationDefinitionWorksWithoutAnEncounter(int radius, bool expected)
    {
        ICreatureAuraRule visualization = DefinedAuras.TryGet(RottingAuraRules.Slug);
        Assert.That(visualization, Is.TypeOf<RottingAuraVisualization>());
        Assert.That(visualization.Slug, Is.EqualTo(RottingAuraRules.Slug));
        Assert.That(
            visualization.HasVisual(
                new CreatureAura { slug = RottingAuraRules.Slug, radiusFeet = radius }
            ),
            Is.EqualTo(expected)
        );
    }

    [Test]
    public void AdapterCapturesOnlyExistingSpatialDataAndTypedCreatureValues()
    {
        GameObject sourceObject = new("source");
        GameObject targetObject = new("target");
        try
        {
            CreatureComponent source = sourceObject.AddComponent<CreatureComponent>();
            CreatureComponent target = targetObject.AddComponent<CreatureComponent>();
            source.level = 6;
            source.auras = new List<CreatureAura>
            {
                new() { slug = RottingAuraRules.Slug, radiusFeet = 10 },
            };
            target.traits = new List<string> { "humanoid" };
            target.weaknesses = new List<DamageValue> { new("void", 2) };
            target.resistances = new List<DamageValue> { new("fire", 3) };
            Tile[,] tiles = CreateTiles(4);
            Place(tiles, sourceObject, 0);
            Place(tiles, targetObject, 2);
            UnityRottingAuraModule context = new(
                new Dictionary<CreatureId, CreatureComponent>
                {
                    [Source] = source,
                    [Target] = target,
                },
                tiles
            );

            RottingAuraTurnData data = context.Capture(Snapshot(), Encounter, Target);

            Assert.That(data.Sources, Has.Count.EqualTo(1));
            Assert.That(data.Sources[0].Creature, Is.EqualTo(Source));
            Assert.That(data.Sources[0].Level, Is.EqualTo(6));
            Assert.That(data.TargetTraits, Is.EqualTo(new[] { Trait.FromSlug("humanoid") }));
            Assert.That(data.Weaknesses, Has.Count.EqualTo(1));
            Assert.That(data.Weaknesses[0].Amount, Is.EqualTo(2));
            Assert.That(data.Resistances, Has.Count.EqualTo(1));
            Assert.That(data.Resistances[0].DamageType, Is.EqualTo("fire"));
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void AdapterUsesReplacementTopologyForRangeAndBlockedLineOfEffect()
    {
        GameObject sourceObject = new("source");
        GameObject targetObject = new("target");
        try
        {
            CreatureComponent source = sourceObject.AddComponent<CreatureComponent>();
            CreatureComponent target = targetObject.AddComponent<CreatureComponent>();
            source.auras = new List<CreatureAura>
            {
                new() { slug = RottingAuraRules.Slug, radiusFeet = 10 },
            };
            target.traits = new List<string>();
            target.weaknesses = new List<DamageValue>();
            target.resistances = new List<DamageValue>();
            Dictionary<CreatureId, CreatureComponent> creatures = new()
            {
                [Source] = source,
                [Target] = target,
            };
            Tile[,] outOfRange = CreateTiles(4);
            Place(outOfRange, sourceObject, 0);
            Place(outOfRange, targetObject, 3);
            UnityRottingAuraModule context = new(creatures, outOfRange);

            Assert.That(context.Capture(Snapshot(), Encounter, Target).Sources, Is.Empty);

            Tile[,] blocked = CreateTiles(3);
            blocked[1, 0] = null;
            Place(blocked, sourceObject, 0);
            Place(blocked, targetObject, 2);
            context.RefreshTopology(blocked);

            Assert.That(context.Capture(Snapshot(), Encounter, Target).Sources, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void InitialAndReinforcementEnrollmentBothReceiveFeatureBinding()
    {
        GameObject initialObject = new("initial");
        GameObject anchorObject = new("anchor");
        GameObject reinforcementObject = new("reinforcement");
        UnityCombatRulesBridge bridge = null;
        try
        {
            TestActionController initial = ConfigureCombatant(initialObject, "Players", 0);
            TestActionController anchor = ConfigureCombatant(anchorObject, "Enemies", 1);
            TestActionController reinforcement = ConfigureCombatant(
                reinforcementObject,
                "Players",
                2
            );
            Tile[,] tiles = CreateTiles(3);
            Place(tiles, initialObject, 0);
            Place(tiles, anchorObject, 1);
            Place(tiles, reinforcementObject, 2);
            bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { initial, anchor },
                tiles,
                "Players"
            );

            bridge.AddCombatants(new ActionController[] { reinforcement });

            CreatureId initialId = bridge.GetCreatureId(initial);
            CreatureId reinforcementId = bridge.GetCreatureId(reinforcement);
            Assert.That(HasAuraBinding(bridge.Snapshot, initialId), Is.True);
            Assert.That(HasAuraBinding(bridge.Snapshot, reinforcementId), Is.True);
        }
        finally
        {
            bridge?.ReleaseOwnership();
            Object.DestroyImmediate(initialObject);
            Object.DestroyImmediate(anchorObject);
            Object.DestroyImmediate(reinforcementObject);
        }
    }

    private static RulesSnapshot Snapshot()
    {
        TurnIdentity turn = new(Encounter, new TurnId(1), Target, RoundNumber.First, 1);
        RulesStateSeed seed = new RulesStateSeed()
            .SeedCreature(new CreatureState(Source, Enemies))
            .SeedCreature(new CreatureState(Target, Players))
            .SeedHealth(Source, new HealthState(10, 10))
            .SeedHealth(Target, new HealthState(5, 10))
            .SeedEncounter(
                new EncounterState(
                    Encounter,
                    EncounterPhase.Active,
                    Players,
                    RoundNumber.First,
                    new[]
                    {
                        new InitiativeEntry(Source, Enemies, 20, 0, 0, RoundNumber.First),
                        new InitiativeEntry(Target, Players, 10, 0, 1, RoundNumber.First),
                    },
                    1,
                    turn,
                    2,
                    null
                )
            );
        return new InMemoryRulesStore(seed).Snapshot;
    }

    private static Tile[,] CreateTiles(int width)
    {
        Tile[,] tiles = new Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new Tile();
        return tiles;
    }

    private static void Place(Tile[,] tiles, GameObject creature, int x)
    {
        creature.transform.position = new Vector3(x, 0, 0);
        tiles[x, 0].Occupants.Add(creature);
    }

    private static TestActionController ConfigureCombatant(
        GameObject gameObject,
        string teamName,
        int x
    )
    {
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        creature.traits = new List<string>();
        creature.weaknesses = new List<DamageValue>();
        creature.resistances = new List<DamageValue>();
        gameObject.transform.position = new Vector3(x, 0, 0);
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        return gameObject.AddComponent<TestActionController>();
    }

    private static bool HasAuraBinding(RulesSnapshot snapshot, CreatureId owner) =>
        snapshot.RuleBindings.Any(pair =>
            pair.Value.Owner == owner && pair.Value.DefinitionId == RottingAuraRules.DefinitionId
        );

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
