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
            new[] { new SpellAttackDamageComponent(2, 4, "spirit") }
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(new RulesStateSeed()).Snapshot;

        Assert.That(
            context.Validate(snapshot, actorId, attack, targetId),
            Is.TypeOf<ActionValidationResult.ValidActionValidationResult>()
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
        }
        finally
        {
            GridLineOfSightData.Unregister(corner);
        }
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
        Tile[,] tiles = new Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new Tile();
        return tiles;
    }
}
