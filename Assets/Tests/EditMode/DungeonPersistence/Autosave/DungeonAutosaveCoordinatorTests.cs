using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Autosave;
using Game.DungeonPersistence.Repository;
using Game.KayKit;
using GridPublic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class DungeonAutosaveCoordinatorTests
{
    private readonly List<UnityEngine.Object> cleanup = new();
    private string directory;

    [SetUp]
    public void SetUp()
    {
        directory = Path.GetFullPath(
            Path.Combine(".agent-temp", "coordinator-" + Guid.NewGuid().ToString("N"))
        );
    }

    [TearDown]
    public void TearDown()
    {
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
                UnityEngine.Object.DestroyImmediate(cleanup[index]);
        }
        cleanup.Clear();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Test]
    public void ActionBoundaryDefersWhileBusyThenCommitsLatestStateOnceStable()
    {
        CombatManager manager = Track(new GameObject("Combat Manager"))
            .AddComponent<CombatManager>();
        GameObject partyObject = Track(new GameObject("Party"));
        DungeonPersistenceTestActionController party =
            partyObject.AddComponent<DungeonPersistenceTestActionController>();
        CreatureComponent creature = partyObject.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(12, 12);
        partyObject.AddComponent<Conditions>();
        partyObject.transform.position = new Vector3(1f, 0f, 1f);
        DungeonPartyMemberIdentity identity =
            partyObject.AddComponent<DungeonPartyMemberIdentity>();
        identity.Configure("party-slot", "party-content");
        GameObject runtimeRoot = Track(new GameObject("Runtime"));
        DungeonEncounterRuntimeController runtime =
            runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
        DungeonLevelDocument document = Document();
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        runtime.InitializePristine(
            document,
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation()
        );
        FileSystemDungeonSaveRepository repository = new(directory);
        DungeonAutosaveCoordinator coordinator =
            runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
        coordinator.Initialize(
            document,
            repository,
            runtime,
            new[] { party },
            saveImmediately: true
        );
        Assert.That(
            repository.Load().Value.Manifest.Party.Members.Single().CurrentHitPoints,
            Is.EqualTo(12)
        );

        creature.InitializeHealthBeforeEncounter(5, 12);
        party.IsTakingAction = true;
        OnActorActionCompleted.Invoke(partyObject);

        Assert.That(
            repository.Load().Value.Manifest.Party.Members.Single().CurrentHitPoints,
            Is.EqualTo(12)
        );

        party.IsTakingAction = false;
        OnActorActionCompleted.Invoke(partyObject);

        DungeonSaveResult<DungeonRunSave> saved = repository.Load();
        Assert.That(saved.IsSuccess, Is.True);
        Assert.That(saved.Value.Manifest.Party.Members.Single().CurrentHitPoints, Is.EqualTo(5));
        Assert.That(coordinator.LastDiagnostics, Is.Empty);
    }

    [Test]
    public void MissingSaveBootstrapCreatesRuntimeAndCommitsInitialFloor()
    {
        CombatManager manager = Track(new GameObject("Bootstrap Combat Manager"))
            .AddComponent<CombatManager>();
        GameObject partyObject = Track(new GameObject("Bootstrap Party"));
        DungeonPersistenceTestActionController party =
            partyObject.AddComponent<DungeonPersistenceTestActionController>();
        CreatureComponent creature = partyObject.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(12, 12);
        partyObject.AddComponent<Conditions>();
        partyObject.transform.position = new Vector3(1f, 0f, 1f);
        partyObject.AddComponent<Token>();
        DungeonPartyMemberIdentity identity =
            partyObject.AddComponent<DungeonPartyMemberIdentity>();
        identity.Configure("party-slot", "party-content");
        GameObject mapObject = Track(new GameObject("Map"));
        Map map = mapObject.AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        FileSystemDungeonSaveRepository repository = new(directory);

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.Initialize(
            map,
            Document(),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.RestoredExistingRun, Is.False);
        Assert.That(result.Runtime.IsInitialized, Is.True);
        Assert.That(repository.Load().IsSuccess, Is.True);
    }

    [Test]
    public void ExistingSaveRepopulatesCurrentFloorAndRestoresPartyState()
    {
        CombatManager manager = Track(new GameObject("Restore Combat Manager"))
            .AddComponent<CombatManager>();
        GameObject partyObject = Track(new GameObject("Restore Party"));
        DungeonPersistenceTestActionController party =
            partyObject.AddComponent<DungeonPersistenceTestActionController>();
        CreatureComponent creature = partyObject.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(12, 12);
        partyObject.AddComponent<Conditions>();
        partyObject.transform.position = new Vector3(1f, 0f, 1f);
        partyObject.AddComponent<Token>();
        partyObject
            .AddComponent<DungeonPartyMemberIdentity>()
            .Configure("party-slot", "party-content");
        GameObject mapObject = Track(new GameObject("Restore Map"));
        mapObject.SetActive(false);
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog mapCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset"
        );
        Assert.That(mapCatalog, Is.Not.Null);
        DungeonLevelDocument document = Document();
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(document))),
            mapCatalog
        );
        DungeonEncounterCreatureCatalog encounterCatalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        FileSystemDungeonSaveRepository repository = new(directory);
        GameObject initialRoot = Track(new GameObject("Initial Runtime"));
        initialRoot.transform.SetParent(map.transform, false);
        DungeonRunPersistenceBootstrapResult created = DungeonRunPersistenceBootstrap.Initialize(
            map,
            document,
            encounterCatalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            initialRoot,
            repository
        );
        Assert.That(created.IsSuccess, Is.True);
        UnityEngine.Object.DestroyImmediate(initialRoot);
        partyObject.transform.position = new Vector3(2f, 0f, 2f);
        creature.InitializeHealthBeforeEncounter(4, 12);
        GameObject restoredRoot = Track(new GameObject("Restored Runtime"));
        restoredRoot.transform.SetParent(map.transform, false);

        DungeonRunPersistenceBootstrapResult restored = DungeonRunPersistenceBootstrap.Initialize(
            map,
            document,
            encounterCatalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            restoredRoot,
            repository
        );

        Assert.That(
            restored.IsSuccess,
            Is.True,
            string.Join(" ", restored.Diagnostics.Select(diagnostic => diagnostic.Message))
        );
        Assert.That(restored.RestoredExistingRun, Is.True);
        Assert.That(partyObject.transform.position, Is.EqualTo(new Vector3(1f, 0f, 1f)));
        Assert.That(creature.hp, Is.EqualTo(12));
        Assert.That(map.UsesRuntimeJsonSource, Is.True);
    }

    private T Track<T>(T value)
        where T : UnityEngine.Object
    {
        cleanup.Add(value);
        return value;
    }

    private static DungeonLevelDocument Document()
    {
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata("test-generator", 123, 0, 0),
            new[] { "...", "...", "..." },
            new[] { new DungeonRoom(1, 0, 0, 2, 2) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>()
        );
    }

    private sealed class RecordingExplorationPresentation : IDungeonExplorationPresentation
    {
        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) { }

        public void HideExploration() { }
    }
}
