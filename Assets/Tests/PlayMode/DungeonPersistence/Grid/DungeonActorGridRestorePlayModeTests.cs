using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class DungeonActorGridRestorePlayModeTests
{
    private readonly List<GameObject> createdObjects = new();

    [SetUp]
    public void SetUp()
    {
        ResetGridSingleton();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects.AsEnumerable().Reverse())
            UnityEngine.Object.DestroyImmediate(createdObject);
        createdObjects.Clear();
        ResetGridSingleton();
    }

    [UnityTest]
    public IEnumerator Restore_AllowsRegisteredTokenSwapAndPreservesHeight()
    {
        GridBase grid = CreateGrid(GroundGrid(3, 2));
        TestActionController first = CreateRegisteredActor("First", new Vector3(0, 1.25f, 0));
        TestActionController second = CreateRegisteredActor("Second", new Vector3(1, 2.5f, 0));
        yield return null;
        Assert.That(first.GetComponent<Token>().IsRegistered, Is.True);
        Assert.That(second.GetComponent<Token>().IsRegistered, Is.True);

        DungeonActorRestorePlan plan = DungeonActorStateAdapter.PreflightRestore(
            new[]
            {
                new DungeonActorRestoreTarget(first, LivingState("first", 1, 0)),
                new DungeonActorRestoreTarget(second, LivingState("second", 0, 0)),
            }
        );
        plan.Apply();

        Assert.That(first.transform.position, Is.EqualTo(new Vector3(1, 1.25f, 0)));
        Assert.That(second.transform.position, Is.EqualTo(new Vector3(0, 2.5f, 0)));
        Assert.That(grid.GetTiles()[0, 0].Occupants, Is.EqualTo(new[] { second.gameObject }));
        Assert.That(grid.GetTiles()[1, 0].Occupants, Is.EqualTo(new[] { first.gameObject }));
        Assert.That(first.GetComponent<Token>().IsRegistered, Is.True);
        Assert.That(second.GetComponent<Token>().IsRegistered, Is.True);
    }

    [UnityTest]
    public IEnumerator Restore_DetachesDefeatedTokenBeforeLivingActorClaimsItsCell()
    {
        GridBase grid = CreateGrid(GroundGrid(3, 2));
        TestActionController living = CreateRegisteredActor("Living", new Vector3(0, 1, 0));
        TestActionController defeated = CreateRegisteredActor("Defeated", new Vector3(1, 3, 0));
        yield return null;

        DungeonActorRestorePlan plan = DungeonActorStateAdapter.PreflightRestore(
            new[]
            {
                new DungeonActorRestoreTarget(living, LivingState("living", 1, 0)),
                new DungeonActorRestoreTarget(defeated, DefeatedState("defeated", 2, 0)),
            }
        );
        plan.Apply();

        Assert.That(grid.GetTiles()[0, 0].Occupants, Is.Empty);
        Assert.That(grid.GetTiles()[1, 0].Occupants, Is.EqualTo(new[] { living.gameObject }));
        Assert.That(grid.GetTiles()[2, 0].Occupants, Is.Empty);
        Assert.That(living.transform.position, Is.EqualTo(new Vector3(1, 1, 0)));
        Assert.That(defeated.transform.position, Is.EqualTo(new Vector3(2, 3, 0)));
        Assert.That(defeated.GetComponent<Token>().IsRegistered, Is.False);
        Assert.That(defeated.gameObject.activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator Preflight_BlockedDestinationDoesNotMutateActorsOrOccupancy()
    {
        TileType[,] gridData = GroundGrid(3, 2);
        gridData[2, 1] = TileType.Obstacle;
        GridBase grid = CreateGrid(gridData);
        TestActionController actor = CreateRegisteredActor("Actor", new Vector3(0, 4, 0));
        yield return null;

        Assert.Throws<InvalidOperationException>(() =>
            DungeonActorStateAdapter.PreflightRestore(
                actor,
                LivingState("actor", 2, 1, currentHitPoints: 2)
            )
        );

        Assert.That(actor.transform.position, Is.EqualTo(new Vector3(0, 4, 0)));
        Assert.That(actor.GetComponent<CreatureComponent>().Health.Current, Is.EqualTo(5));
        Assert.That(grid.GetTiles()[0, 0].Occupants, Is.EqualTo(new[] { actor.gameObject }));
        Assert.That(actor.GetComponent<Token>().IsRegistered, Is.True);
    }

    [UnityTest]
    public IEnumerator Preflight_OutOfBoundsDestinationDoesNotMutateActorsOrOccupancy()
    {
        GridBase grid = CreateGrid(GroundGrid(2, 2));
        TestActionController actor = CreateRegisteredActor("Actor", new Vector3(0, 4, 0));
        yield return null;

        Assert.Throws<InvalidOperationException>(() =>
            DungeonActorStateAdapter.PreflightRestore(
                actor,
                LivingState("actor", 99, 0, currentHitPoints: 2)
            )
        );

        Assert.That(actor.transform.position, Is.EqualTo(new Vector3(0, 4, 0)));
        Assert.That(actor.GetComponent<CreatureComponent>().Health.Current, Is.EqualTo(5));
        Assert.That(grid.GetTiles()[0, 0].Occupants, Is.EqualTo(new[] { actor.gameObject }));
    }

    [UnityTest]
    public IEnumerator Preflight_ConflictingBatchDestinationsDoNotMutateEitherActor()
    {
        GridBase grid = CreateGrid(GroundGrid(3, 2));
        TestActionController first = CreateRegisteredActor("First", new Vector3(0, 1, 0));
        TestActionController second = CreateRegisteredActor("Second", new Vector3(1, 2, 0));
        yield return null;

        Assert.Throws<InvalidOperationException>(() =>
            DungeonActorStateAdapter.PreflightRestore(
                new[]
                {
                    new DungeonActorRestoreTarget(first, LivingState("first", 2, 0)),
                    new DungeonActorRestoreTarget(second, LivingState("second", 2, 0)),
                }
            )
        );

        Assert.That(first.transform.position, Is.EqualTo(new Vector3(0, 1, 0)));
        Assert.That(second.transform.position, Is.EqualTo(new Vector3(1, 2, 0)));
        Assert.That(grid.GetTiles()[0, 0].Occupants, Is.EqualTo(new[] { first.gameObject }));
        Assert.That(grid.GetTiles()[1, 0].Occupants, Is.EqualTo(new[] { second.gameObject }));
        Assert.That(grid.GetTiles()[2, 0].Occupants, Is.Empty);
    }

    private GridBase CreateGrid(TileType[,] gridData)
    {
        GameObject owner = Track(new GameObject("Synthetic Actor Restore Grid"));
        owner.SetActive(false);
        SyntheticMap map = owner.AddComponent<SyntheticMap>();
        map.ConfigureSynthetic(gridData, new bool[gridData.GetLength(0), gridData.GetLength(1)]);
        GridBase grid = owner.AddComponent<GridBase>();
        owner.SetActive(true);
        Assert.That(grid.IsInitialized, Is.True);
        return grid;
    }

    private TestActionController CreateRegisteredActor(string name, Vector3 position)
    {
        GameObject owner = Track(new GameObject(name));
        owner.SetActive(false);
        owner.transform.position = position;
        TestActionController controller = owner.AddComponent<TestActionController>();
        CreatureComponent creature = owner.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(5, 5);
        owner.AddComponent<Token>();
        owner.SetActive(true);
        return controller;
    }

    private GameObject Track(GameObject gameObject)
    {
        createdObjects.Add(gameObject);
        return gameObject;
    }

    private static DungeonCreatureSaveState LivingState(
        string instanceId,
        int x,
        int z,
        int currentHitPoints = 5
    ) => ActorState(instanceId, x, z, currentHitPoints, isDefeated: false);

    private static DungeonCreatureSaveState DefeatedState(string instanceId, int x, int z) =>
        ActorState(instanceId, x, z, currentHitPoints: 0, isDefeated: true);

    private static DungeonCreatureSaveState ActorState(
        string instanceId,
        int x,
        int z,
        int currentHitPoints,
        bool isDefeated
    ) =>
        new(
            instanceId,
            "test-creature",
            new DungeonSaveCell(x, z),
            new DungeonHealthSaveState(currentHitPoints, 5, 0, string.Empty, Array.Empty<string>()),
            isDefeated,
            Array.Empty<DungeonConditionSaveState>(),
            Array.Empty<DungeonTimedEffectSaveState>(),
            new DungeonPreparedRuleSaveState(
                Array.Empty<string>(),
                Array.Empty<DungeonPreparedEffectSaveState>(),
                Array.Empty<DungeonSpellPoolSaveState>()
            ),
            new DungeonEquipmentSaveState(
                Array.Empty<DungeonInventoryItemSaveState>(),
                Array.Empty<DungeonAmmunitionSaveState>()
            )
        );

    private static TileType[,] GroundGrid(int width, int height)
    {
        TileType[,] result = new TileType[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
                result[x, z] = TileType.Ground;
        }
        return result;
    }

    private static void ResetGridSingleton()
    {
        FieldInfo singleton = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(singleton, Is.Not.Null);
        singleton.SetValue(null, null);
    }

    private sealed class SyntheticMap : Map
    {
        internal void ConfigureSynthetic(TileType[,] gridData, bool[,] lineOfSightBlocks)
        {
            GridData = gridData;
            LineOfSightBlocks = lineOfSightBlocks;
        }
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
