using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Verifies catalog validation and transactional encounter creature materialization.</summary>
public sealed class DungeonEncounterMaterializerTests
{
    private const string GoblinJson = "DataFiles/pathfinder-monster-core/goblin-warrior";
    private readonly List<Object> cleanup = new();

    /// <summary>Destroys all synthetic Unity objects after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
                Object.DestroyImmediate(cleanup[index]);
        }
        cleanup.Clear();
    }

    /// <summary>Verifies encounter members accept stable identity only once.</summary>
    [Test]
    public void EncounterMemberConfiguresExactlyOnce()
    {
        GameObject owner = Track(new GameObject("Encounter member"));
        DungeonEncounterMember member = owner.AddComponent<DungeonEncounterMember>();
        int defeatReports = 0;
        member.Defeated += _ => defeatReports++;

        member.Configure(
            "encounter-1",
            "encounter-1/creature-0000",
            "goblin-warrior",
            "restored-child-state"
        );
        member.ReportDefeated();
        member.ReportDefeated();

        Assert.That(member.IsConfigured, Is.True);
        Assert.That(member.EncounterId, Is.EqualTo("encounter-1"));
        Assert.That(member.InstanceId, Is.EqualTo("encounter-1/creature-0000"));
        Assert.That(member.CreatureContentId, Is.EqualTo("goblin-warrior"));
        Assert.That(member.PersistentState, Is.EqualTo("restored-child-state"));
        Assert.That(member.DefeatWasReported, Is.True);
        Assert.That(defeatReports, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() =>
            member.Configure(
                "encounter-1",
                "encounter-1/creature-0000",
                "goblin-warrior",
                string.Empty
            )
        );
    }

    /// <summary>Verifies catalog validation rejects ambiguous or incomplete definitions.</summary>
    [Test]
    public void CatalogRejectsDuplicateMissingJsonAndMissingPrefabReferences()
    {
        GameObject prefab = CreaturePrefab("Catalog prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog();
        catalog.ReplaceEntries(
            new[] { Entry("goblin-warrior", prefab), Entry("goblin-warrior", prefab) }
        );
        Assert.That(
            Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow).Message,
            Does.Contain("duplicated")
        );

        catalog.ReplaceEntries(
            new[]
            {
                new DungeonEncounterCreatureCatalogEntry(
                    "missing-json",
                    "DataFiles/does-not-exist",
                    prefab
                ),
            }
        );
        Assert.That(
            Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow).Message,
            Does.Contain("cannot load creature JSON")
        );

        catalog.ReplaceEntries(
            new[] { new DungeonEncounterCreatureCatalogEntry("missing-prefab", GoblinJson, null) }
        );
        Assert.That(
            Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow).Message,
            Does.Contain("requires a creature prefab")
        );

        GameObject prefabWithoutTeam = Track(new GameObject("Prefab without team"));
        prefabWithoutTeam.SetActive(false);
        prefabWithoutTeam.AddComponent<TestActionController>();
        prefabWithoutTeam.AddComponent<Token>();
        catalog.ReplaceEntries(new[] { Entry("goblin-warrior", prefabWithoutTeam) });
        Assert.That(
            Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow).Message,
            Does.Contain("requires a Team")
        );
    }

    /// <summary>Verifies the generated project catalog resolves every authored enemy.</summary>
    [Test]
    public void DefaultCatalogLoadsEveryAuthoredEncounterCreature()
    {
        DungeonEncounterCreatureCatalog catalog =
            DungeonEncounterCreatureCatalog.LoadDefaultOrThrow();

        Assert.That(
            catalog.Entries.Select(entry => entry.ContentId),
            Is.EqualTo(
                new[] { "goblin-warrior", "kobold-warrior", "skeleton-guard", "zombie-shambler" }
            )
        );
    }

    /// <summary>Verifies plan order, stable IDs, hierarchy, and cell positions are preserved.</summary>
    [Test]
    public void MaterializeCreatesOrderedStableMembersAtSpawnCells()
    {
        GameObject prefab = CreaturePrefab("Materializer prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog(
            Entry("goblin-warrior", prefab),
            Entry("kobold-warrior", prefab)
        );
        RecordingFactory factory = new();
        DungeonEncounterMaterializer materializer = new(catalog, factory);
        GameObject root = Track(new GameObject("Encounter root"));
        DungeonEncounterPlan plan = Plan(
            new[] { new DungeonCell(4, 7), new DungeonCell(8, 2) },
            "goblin-warrior",
            "kobold-warrior"
        );

        DungeonEncounterMaterialization result = materializer.Materialize(plan, root.transform);

        Assert.That(result.Members, Has.Count.EqualTo(2));
        Assert.That(result.Controllers, Has.Count.EqualTo(2));
        Assert.That(
            result.Members.Select(member => member.InstanceId),
            Is.EqualTo(new[] { "encounter-1/creature-0000", "encounter-1/creature-0001" })
        );
        Assert.That(
            result.Members.Select(member => member.CreatureContentId),
            Is.EqualTo(new[] { "goblin-warrior", "kobold-warrior" })
        );
        Assert.That(
            result.Members.Select(member => member.transform.position),
            Is.EqualTo(new[] { new Vector3(4f, 0f, 7f), new Vector3(8f, 0f, 2f) })
        );
        Assert.That(
            result.Members.All(member => member.transform.parent == root.transform),
            Is.True
        );
        Assert.That(
            result.Controllers.Select(controller => controller.gameObject),
            Is.EqualTo(result.Members.Select(member => member.gameObject))
        );
    }

    /// <summary>Verifies restored casualties are skipped without renumbering surviving members.</summary>
    [Test]
    public void MaterializeLifecycleViewPreservesOriginalIndexesForSurvivors()
    {
        GameObject prefab = CreaturePrefab("Restored prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog(Entry("goblin-warrior", prefab));
        RecordingFactory factory = new();
        DungeonEncounterMaterializer materializer = new(catalog, factory);
        GameObject root = Track(new GameObject("Restored root"));
        DungeonEncounterPlan plan = Plan(
            new[] { new DungeonCell(1, 2), new DungeonCell(6, 7) },
            "goblin-warrior",
            "goblin-warrior"
        );
        DungeonEncounterStateMachine lifecycle = new(new[] { plan });
        DungeonEncounterGroupView active = lifecycle.EnterRoom(1).Encounter;
        lifecycle.MarkCreatureDefeated(active.Creatures[0].InstanceId);
        lifecycle.SuspendIfPartyOutsideActiveRegions(1, Array.Empty<int>());

        DungeonEncounterMaterialization result = materializer.Materialize(
            lifecycle.GetEncounter("encounter-1"),
            root.transform
        );

        Assert.That(result.Members, Has.Count.EqualTo(1));
        Assert.That(result.Members[0].InstanceId, Is.EqualTo("encounter-1/creature-0001"));
        Assert.That(result.Members[0].transform.position, Is.EqualTo(new Vector3(6f, 0f, 7f)));
    }

    /// <summary>Verifies a failed creation rolls back every earlier instance.</summary>
    [Test]
    public void MaterializeRollsBackEarlierCreaturesWhenFactoryFails()
    {
        GameObject prefab = CreaturePrefab("Rollback prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog(
            Entry("goblin-warrior", prefab),
            Entry("kobold-warrior", prefab)
        );
        RecordingFactory factory = new(failOnCall: 2);
        RecordingRuntimeRegistration registration = new();
        DungeonEncounterMaterializer materializer = new(catalog, factory, registration);
        GameObject root = Track(new GameObject("Rollback root"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(
                Plan(
                    new[] { new DungeonCell(1, 1), new DungeonCell(2, 2) },
                    "goblin-warrior",
                    "kobold-warrior"
                ),
                root.transform
            )
        );

        Assert.That(exception.Message, Does.Contain("Injected creation failure"));
        Assert.That(factory.DestroyCount, Is.EqualTo(1));
        Assert.That(registration.RollbackCount, Is.EqualTo(1));
        Assert.That(root.transform.childCount, Is.Zero);
    }

    /// <summary>Verifies occupied cells reject a plan before any creature is created.</summary>
    [Test]
    public void MaterializeRejectsOccupiedSpawnCellBeforeCreation()
    {
        GameObject prefab = CreaturePrefab("Occupied-cell prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog(Entry("goblin-warrior", prefab));
        RecordingFactory factory = new();
        DungeonCell occupied = new(4, 7);
        RecordingRuntimeRegistration registration = new(occupied);
        DungeonEncounterMaterializer materializer = new(catalog, factory, registration);
        GameObject root = Track(new GameObject("Occupied-cell root"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(Plan(new[] { occupied }, "goblin-warrior"), root.transform)
        );

        Assert.That(exception.Message, Does.Contain("already occupied"));
        Assert.That(factory.CreateCount, Is.Zero);
        Assert.That(registration.ValidateCreatedCount, Is.Zero);
        Assert.That(registration.RollbackCount, Is.Zero);
    }

    /// <summary>Verifies room-aware materialization relocates an occupied planned spawn.</summary>
    [Test]
    public void MaterializeLifecycleViewUsesNearestAvailableRoomCell()
    {
        GameObject prefab = CreaturePrefab("Fallback prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog(Entry("goblin-warrior", prefab));
        RecordingFactory factory = new();
        DungeonCell occupied = new(4, 7);
        RecordingRuntimeRegistration registration = new(occupied);
        DungeonEncounterMaterializer materializer = new(catalog, factory, registration);
        GameObject root = Track(new GameObject("Fallback root"));
        DungeonEncounterPlan plan = Plan(new[] { occupied }, "goblin-warrior");
        DungeonEncounterStateMachine lifecycle = new(new[] { plan });

        DungeonEncounterMaterialization result = materializer.Materialize(
            lifecycle.GetEncounter("encounter-1"),
            root.transform,
            new DungeonRoom(1, 3, 6, 5, 8)
        );

        Assert.That(result.Members, Has.Count.EqualTo(1));
        Assert.That(result.Members[0].InstanceId, Is.EqualTo("encounter-1/creature-0000"));
        Assert.That(result.Members[0].transform.position, Is.EqualTo(new Vector3(4f, 0f, 6f)));
    }

    /// <summary>Verifies persisted survivors restore their cell, HP, and opaque child state.</summary>
    [Test]
    public void MaterializeRestoredSurvivorAppliesMutableRuntimeState()
    {
        GameObject prefab = CreaturePrefab("Restored survivor prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog(Entry("goblin-warrior", prefab));
        RecordingFactory factory = new();
        DungeonEncounterPlan plan = Plan(new[] { new DungeonCell(4, 7) }, "goblin-warrior");
        DungeonCreatureRuntimeState restoredCreature = new(
            "encounter-1/creature-0000",
            "goblin-warrior",
            "encounter-1",
            new DungeonCell(9, 8),
            3,
            "restored-child-state"
        );
        DungeonRuntimeState runtimeState = new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { restoredCreature }
        );
        DungeonEncounterStateMachine lifecycle = DungeonEncounterStateMachine.Restore(
            new[] { plan },
            DungeonEncounterLifecycleSnapshot.FromRuntimeState(new[] { plan }, runtimeState)
        );
        DungeonEncounterMaterializer materializer = new(
            catalog,
            factory,
            new RecordingRuntimeRegistration(),
            runtimeState.Creatures
        );
        GameObject root = Track(new GameObject("Restored survivor root"));

        DungeonEncounterMaterialization result = materializer.Materialize(
            lifecycle.GetEncounter("encounter-1"),
            root.transform,
            new DungeonRoom(1, 3, 6, 6, 9)
        );

        Assert.That(result.Members, Has.Count.EqualTo(1));
        Assert.That(
            result.Members[0].transform.position,
            Is.EqualTo(new Vector3(9f, 0f, 8f)),
            "An available persisted corridor or neighboring-room cell must not be snapped back into the source room."
        );
        Assert.That(result.Members[0].GetComponent<CreatureComponent>().hp, Is.EqualTo(3));
        Assert.That(result.Members[0].PersistentState, Is.EqualTo("restored-child-state"));
    }

    /// <summary>Verifies every catalog ID resolves before the first creature is created.</summary>
    [Test]
    public void MaterializeResolvesWholePlanBeforeCreatingAnything()
    {
        GameObject prefab = CreaturePrefab("Preflight prefab");
        DungeonEncounterCreatureCatalog catalog = Catalog(Entry("goblin-warrior", prefab));
        RecordingFactory factory = new();
        DungeonEncounterMaterializer materializer = new(catalog, factory);
        GameObject root = Track(new GameObject("Preflight root"));

        Assert.Throws<KeyNotFoundException>(() =>
            materializer.Materialize(
                Plan(
                    new[] { new DungeonCell(1, 1), new DungeonCell(2, 2) },
                    "goblin-warrior",
                    "missing-creature"
                ),
                root.transform
            )
        );

        Assert.That(factory.CreateCount, Is.Zero);
        Assert.That(root.transform.childCount, Is.Zero);
    }

    /// <summary>Verifies the production JSON factory applies data at the final transform.</summary>
    [Test]
    public void JsonFactoryCreatesAtFinalTransformAndAppliesCreatureJson()
    {
        GameObject prefab = CreaturePrefab("JSON factory prefab");
        GameObject root = Track(new GameObject("JSON factory root"));
        JsonDungeonEncounterCreatureFactory factory = new();

        GameObject instance = factory.Create(
            Entry("goblin-warrior", prefab),
            new Vector3(3f, 0f, 9f),
            Quaternion.Euler(0f, 90f, 0f),
            root.transform
        );

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.transform.parent, Is.SameAs(root.transform));
        Assert.That(instance.transform.position, Is.EqualTo(new Vector3(3f, 0f, 9f)));
        Assert.That(instance.GetComponent<TestActionController>(), Is.Not.Null);
        Assert.That(instance.GetComponent<CreatureComponent>().name, Is.EqualTo("Goblin Warrior"));
        Assert.That(instance.GetComponent<TestActionController>().GetActions(), Is.Not.Empty);
        Assert.That(
            instance.GetComponent<Team>().Name,
            Is.EqualTo(DungeonEncounterCreatureCatalog.HostileTeamName)
        );
    }

    /// <summary>Verifies invalid team metadata is rejected before a runtime root is created.</summary>
    [Test]
    public void JsonFactoryRejectsMissingTeamWithoutLeakingAnInstance()
    {
        GameObject prefab = Track(new GameObject("Missing team prefab"));
        prefab.SetActive(false);
        prefab.AddComponent<TestActionController>();
        prefab.AddComponent<Token>();
        GameObject root = Track(new GameObject("Missing team root"));
        JsonDungeonEncounterCreatureFactory factory = new();

        Assert.Throws<InvalidOperationException>(() =>
            factory.Create(
                Entry("goblin-warrior", prefab),
                Vector3.zero,
                Quaternion.identity,
                root.transform
            )
        );

        Assert.That(root.transform.childCount, Is.Zero);
    }

    private DungeonEncounterCreatureCatalog Catalog(
        params DungeonEncounterCreatureCatalogEntry[] entries
    )
    {
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        catalog.ReplaceEntries(entries);
        return catalog;
    }

    private DungeonEncounterCreatureCatalogEntry Entry(string id, GameObject prefab) =>
        new(id, GoblinJson, prefab);

    private GameObject CreaturePrefab(string name)
    {
        GameObject prefab = Track(new GameObject(name));
        prefab.SetActive(false);
        prefab.AddComponent<TestActionController>();
        prefab.AddComponent<Token>();
        Team team = prefab.AddComponent<Team>();
        team.Name = "NoTeam";
        return prefab;
    }

    private static DungeonEncounterPlan Plan(
        IReadOnlyList<DungeonCell> cells,
        params string[] creatureIds
    ) => new("encounter-1", 1, DungeonEncounterThreat.Trivial, 40, cells, creatureIds);

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class RecordingFactory : IDungeonEncounterCreatureFactory
    {
        private readonly int failOnCall;

        public RecordingFactory(int failOnCall = -1)
        {
            this.failOnCall = failOnCall;
        }

        public int CreateCount { get; private set; }
        public int DestroyCount { get; private set; }

        /// <inheritdoc/>
        public GameObject Create(
            DungeonEncounterCreatureCatalogEntry definition,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Transform parent
        )
        {
            CreateCount++;
            if (CreateCount == failOnCall)
                throw new InvalidOperationException("Injected creation failure.");

            GameObject instance = new(definition.ContentId);
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(worldPosition, worldRotation);
            CreatureComponent creature = instance.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            instance.AddComponent<TestActionController>();
            return instance;
        }

        /// <inheritdoc/>
        public void Destroy(GameObject instance)
        {
            DestroyCount++;
            Object.DestroyImmediate(instance);
        }
    }

    private sealed class RecordingRuntimeRegistration : IDungeonEncounterRuntimeRegistration
    {
        private readonly HashSet<DungeonCell> occupied;

        public RecordingRuntimeRegistration(params DungeonCell[] occupied)
        {
            this.occupied = new HashSet<DungeonCell>(occupied);
        }

        public int ValidateCreatedCount { get; private set; }
        public int RollbackCount { get; private set; }

        /// <inheritdoc/>
        public DungeonCell ResolveAvailable(
            DungeonCell preferred,
            DungeonRoom room,
            IReadOnlyCollection<DungeonCell> reserved
        )
        {
            if (!occupied.Contains(preferred) && !reserved.Contains(preferred))
                return preferred;
            return Enumerable
                .Range(room.MinimumX, room.MaximumX - room.MinimumX + 1)
                .SelectMany(x =>
                    Enumerable
                        .Range(room.MinimumZ, room.MaximumZ - room.MinimumZ + 1)
                        .Select(z => new DungeonCell(x, z))
                )
                .Where(cell => !occupied.Contains(cell) && !reserved.Contains(cell))
                .OrderBy(cell => Math.Abs(cell.X - preferred.X) + Math.Abs(cell.Z - preferred.Z))
                .ThenBy(cell => cell.Z)
                .ThenBy(cell => cell.X)
                .First();
        }

        /// <inheritdoc/>
        public void RequireAvailable(DungeonCell cell)
        {
            if (occupied.Contains(cell))
                throw new InvalidOperationException(
                    $"Encounter spawn cell ({cell.X}, {cell.Z}) is already occupied."
                );
        }

        /// <inheritdoc/>
        public void ValidateCreated(GameObject instance, DungeonCell cell)
        {
            ValidateCreatedCount++;
        }

        /// <inheritdoc/>
        public void Rollback(GameObject instance)
        {
            RollbackCount++;
        }
    }
}
