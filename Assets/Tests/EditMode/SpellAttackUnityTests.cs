using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Spells;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;

public sealed class SpellAttackUnityTests
{
    private readonly List<GameObject> created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject gameObject in created)
            if (gameObject != null)
                Object.DestroyImmediate(gameObject);
        created.Clear();
    }

    [Test]
    public void ContextRevalidatesNumericRangeAndLineOfEffect()
    {
        CreatureId actorId = new("spell-context-actor");
        CreatureId targetId = new("spell-context-target");
        CreatureComponent actor = CreateCreature("Spell Context Actor", 0);
        CreatureComponent target = CreateCreature("Spell Context Target", 12);
        Tile[,] tiles = CreateTiles(14);
        Dictionary<CreatureId, CreatureComponent> creatures = new()
        {
            [actorId] = actor,
            [targetId] = target,
        };
        UnitySpellAttackContext context = new(creatures, tiles);
        SpellAttackDefinition attack = new(
            new OneCreatureSpellAttackTarget(60),
            new[] { new TypedDamageDice(new DiceExpression(2, 4), "spirit", "test-spell") }
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedStatistics(
                new CreatureStatisticsState(
                    targetId,
                    0,
                    18,
                    0,
                    0,
                    0,
                    new Dictionary<Skill, int>(),
                    System.Array.Empty<Modifier>()
                )
            )
        ).Snapshot;

        Assert.That(
            context.Validate(snapshot, actorId, attack, targetId),
            Is.TypeOf<ActionValidationResult.ValidActionValidationResult>()
        );
        Assert.That(
            context.ValidateSelection(
                snapshot,
                actorId,
                new SpellSelectionProfile(
                    SpellSelectionKind.SingleCreature,
                    rangeFeet: 60,
                    exactCreatureCount: 1
                ),
                new SpellCastSelection(new[] { targetId })
            ),
            Is.TypeOf<ActionValidationResult.ValidActionValidationResult>()
        );
        Assert.That(
            context.Capture(snapshot, actorId, attack, targetId).BaseArmorClass,
            Is.EqualTo(18)
        );

        target.transform.position = new Vector3(13, 0, 0);
        Assert.That(
            context.Validate(snapshot, actorId, attack, targetId),
            Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
        );

        target.transform.position = new Vector3(1, 0, 1);
        Tile[,] corner = new Tile[2, 2]
        {
            { new Tile(), new Tile() },
            { new Tile(), new Tile() },
        };
        bool[,] blockers = new bool[2, 2]
        {
            { false, true },
            { true, false },
        };
        GridLineOfSightData.Register(corner, blockers);
        try
        {
            context.ReplaceTiles(corner);
            Assert.That(
                context.Validate(snapshot, actorId, attack, targetId),
                Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
            );
            Assert.That(
                context.ValidateSelection(
                    snapshot,
                    actorId,
                    new SpellSelectionProfile(
                        SpellSelectionKind.SingleCreature,
                        rangeFeet: 60,
                        exactCreatureCount: 1
                    ),
                    new SpellCastSelection(new[] { targetId })
                ),
                Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
            );
        }
        finally
        {
            GridLineOfSightData.Unregister(corner);
        }
    }

    [Test]
    public void ContextRecomputesExactDirectedAreaMembership()
    {
        CreatureId actorId = new("spell-area-actor");
        CreatureId eastId = new("spell-area-east");
        CreatureId northId = new("spell-area-north");
        CreatureComponent actor = CreateCreature("Spell Area Actor", 1);
        actor.transform.position = new Vector3(1, 0, 1);
        CreatureComponent east = CreateCreature("Spell Area East", 2);
        east.transform.position = new Vector3(2, 0, 1);
        CreatureComponent north = CreateCreature("Spell Area North", 1);
        north.transform.position = new Vector3(1, 0, 2);
        Tile[,] tiles = CreateTiles(4, 4);
        tiles[1, 1].Occupants.Add(actor.gameObject);
        tiles[2, 1].Occupants.Add(east.gameObject);
        tiles[1, 2].Occupants.Add(north.gameObject);
        UnitySpellAttackContext context = new(
            new Dictionary<CreatureId, CreatureComponent>
            {
                [actorId] = actor,
                [eastId] = east,
                [northId] = north,
            },
            tiles
        );
        PlayerId team = new("spell-area-team");
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed()
                .SeedCreature(new CreatureState(actorId, team))
                .SeedCreature(new CreatureState(eastId, team))
                .SeedCreature(new CreatureState(northId, team))
                .SeedHealth(actorId, new HealthState(10, 10))
                .SeedHealth(eastId, new HealthState(10, 10))
                .SeedHealth(northId, new HealthState(10, 10))
        ).Snapshot;
        SpellSelectionProfile cone = new(SpellSelectionKind.Cone, areaFeet: 15);

        Assert.That(
            context.ValidateSelection(
                snapshot,
                actorId,
                cone,
                new SpellCastSelection(new[] { eastId }, SpellAreaDirection.East)
            ),
            Is.TypeOf<ActionValidationResult.ValidActionValidationResult>()
        );
        Assert.That(
            context.ValidateSelection(
                snapshot,
                actorId,
                cone,
                new SpellCastSelection(new[] { northId }, SpellAreaDirection.East)
            ),
            Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
        );
        Assert.That(
            context.ValidateSelection(
                snapshot,
                actorId,
                cone,
                new SpellCastSelection(new[] { eastId })
            ),
            Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>()
        );
    }

    private CreatureComponent CreateCreature(string name, int x)
    {
        GameObject gameObject = new(name);
        created.Add(gameObject);
        gameObject.transform.position = new Vector3(x, 0, 0);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.ac = 10;
        creature.InitializeHealthBeforeEncounter(10, 10);
        return creature;
    }

    private static Tile[,] CreateTiles(int width)
    {
        return CreateTiles(width, 1);
    }

    private static Tile[,] CreateTiles(int width, int depth)
    {
        Tile[,] tiles = new Tile[width, depth];
        for (int x = 0; x < width; x++)
        for (int z = 0; z < depth; z++)
            tiles[x, z] = new Tile();
        return tiles;
    }
}
