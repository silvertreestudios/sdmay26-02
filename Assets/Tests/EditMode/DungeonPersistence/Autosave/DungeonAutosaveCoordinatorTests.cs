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
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class DungeonAutosaveCoordinatorTests
{
    private readonly List<UnityEngine.Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
                UnityEngine.Object.DestroyImmediate(cleanup[index]);
        }
        cleanup.Clear();
    }

    [Test]
    public void ActionBoundaryDefersWhileBusyThenCommitsLatestStateOnceStable()
    {
        CombatManager manager = Track(new GameObject("Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Party", 12);
        GameObject runtimeRoot = Track(new GameObject("Runtime"));
        DungeonEncounterRuntimeController runtime =
            runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
        DungeonLevelDocument document = Document(0, persisted: false);
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
        RecordingRepository repository = new(
            new DungeonSaveDiagnostic(DungeonSaveDiagnosticCode.MissingSave, "autosave", "missing")
        );
        DungeonAutosaveCoordinator coordinator =
            runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
        coordinator.InitializeNewRun(document, repository, runtime, new[] { party });
        Assert.That(repository.Current.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(12));

        party.GetComponent<CreatureComponent>().InitializeHealthBeforeEncounter(5, 12);
        party.IsTakingAction = true;
        OnGameplayStateCommitted.Invoke();
        DungeonSaveResult<DungeonRunSave> unstable = coordinator.CheckpointCurrentFloor();

        Assert.That(unstable.IsSuccess, Is.False);
        Assert.That(repository.Current.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(12));
        Assert.That(repository.SaveCount, Is.EqualTo(1));

        party.IsTakingAction = false;
        OnGameplayStateCommitted.Invoke();

        Assert.That(repository.Current.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(5));
        Assert.That(repository.SaveCount, Is.EqualTo(2));
        Assert.That(coordinator.LastDiagnostics, Is.Empty);
    }

    [Test]
    public void OrdinaryLoadedRunAutosaveRetainsInactiveDepths()
    {
        CombatManager manager = Track(new GameObject("Loaded Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Loaded Party", 12);
        DungeonRunSave baseline = CreateRun(0, 2).WithSelectedFloor(2, PartyState(12));
        string inactivePayload = baseline
            .FloorPayloads.Single(payload => payload.Path == DungeonSaveSchema.FloorPath(0))
            .FloorJson;
        RecordingRepository repository = new(baseline);
        GameObject runtimeRoot = Track(new GameObject("Loaded Runtime"));
        DungeonEncounterRuntimeController runtime =
            runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        runtime.InitializePersisted(
            baseline.GetFloor(2),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation()
        );
        DungeonAutosaveCoordinator coordinator =
            runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
        coordinator.InitializeLoadedRun(baseline, repository, runtime, new[] { party });

        party.GetComponent<CreatureComponent>().InitializeHealthBeforeEncounter(6, 12);
        OnGameplayStateCommitted.Invoke();

        Assert.That(repository.Current.Manifest.Floors.Length, Is.EqualTo(2));
        Assert.That(repository.Current.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(6));
        Assert.That(
            repository
                .Current.FloorPayloads.Single(payload =>
                    payload.Path == DungeonSaveSchema.FloorPath(0)
                )
                .FloorJson,
            Is.EqualTo(inactivePayload)
        );
    }

    [Test]
    public void ImmediateCheckpointReturnsCommittedRunAndKeepsBaselineAfterFailure()
    {
        CombatManager manager = Track(new GameObject("Checkpoint Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Checkpoint Party", 12);
        DungeonRunSave baseline = CreateRun(0, 2).WithSelectedFloor(2, PartyState(12));
        RecordingRepository repository = new(baseline) { FailSave = true };
        GameObject runtimeRoot = Track(new GameObject("Checkpoint Runtime"));
        DungeonEncounterRuntimeController runtime =
            runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        runtime.InitializePersisted(
            baseline.GetFloor(2),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation()
        );
        DungeonAutosaveCoordinator coordinator =
            runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
        coordinator.InitializeLoadedRun(baseline, repository, runtime, new[] { party });
        party.GetComponent<CreatureComponent>().InitializeHealthBeforeEncounter(4, 12);

        DungeonSaveResult<DungeonRunSave> failed = coordinator.CheckpointCurrentFloor();

        Assert.That(failed.IsSuccess, Is.False);
        Assert.That(coordinator.LastCommittedSnapshot, Is.SameAs(baseline));
        Assert.That(repository.Current, Is.SameAs(baseline));

        repository.FailSave = false;
        DungeonSaveResult<DungeonRunSave> committed = coordinator.CheckpointCurrentFloor();

        Assert.That(committed.IsSuccess, Is.True);
        Assert.That(committed.Value, Is.SameAs(coordinator.LastCommittedSnapshot));
        Assert.That(committed.Value.Manifest.Floors.Length, Is.EqualTo(2));
        Assert.That(committed.Value.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(4));
        Assert.That(repository.Current, Is.SameAs(committed.Value));
    }

    [Test]
    public void StartNewRunNeverLoadsAndCommitsExactlyOneIndexedFloor()
    {
        CombatManager manager = Track(new GameObject("New Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("New Party", 12);
        Map map = Track(new GameObject("New Map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("New Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        RecordingRepository repository = new(
            new DungeonSaveDiagnostic(
                DungeonSaveDiagnosticCode.CorruptSave,
                "existing",
                "An existing file must be ignored."
            )
        );

        DungeonRunPersistenceBootstrapResult result =
            DungeonRunPersistenceBootstrap.StartPreparedRunForTests(
                map,
                Document(0, persisted: false),
                catalog,
                manager,
                new[] { party },
                new RecordingExplorationPresentation(),
                runtimeRoot,
                repository
            );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.RestoredExistingRun, Is.False);
        Assert.That(repository.LoadCount, Is.Zero);
        Assert.That(repository.SaveCount, Is.EqualTo(1));
        Assert.That(repository.Current.Manifest.CurrentDepth, Is.EqualTo(0));
        Assert.That(
            repository.Current.Manifest.Floors.Select(item => item.Depth),
            Is.EqualTo(new[] { 0 })
        );
    }

    [Test]
    public void NewRunRejectsIncompatiblePresenterBeforeAnyMutation()
    {
        CombatManager manager = Track(new GameObject("Invalid New Presenter Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty(
            "Invalid New Presenter Party",
            12
        );
        Vector3 priorPosition = new(2f, 0f, 1f);
        party.transform.position = priorPosition;
        Map map = Track(new GameObject("Invalid New Presenter Map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Invalid New Presenter Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        RecordingRepository repository = new((DungeonRunSave)null);

        Assert.Throws<ArgumentException>(() =>
            DungeonRunPersistenceBootstrap.StartPreparedRunForTests(
                map,
                Document(0, persisted: false),
                catalog,
                manager,
                new[] { party },
                new ExplorationOnlyPresentation(),
                runtimeRoot,
                repository
            )
        );

        Assert.That(repository.LoadCount, Is.Zero);
        Assert.That(repository.SaveCount, Is.Zero);
        Assert.That(repository.Current, Is.Null);
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void NewRunRejectsPartyWithoutCreatureStateBeforeAnyMutation()
    {
        CombatManager manager = Track(new GameObject("Invalid New Party Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreatePartyWithoutCreatureState(
            "Invalid New Party"
        );
        Vector3 priorPosition = new(2f, 0f, 1f);
        party.transform.position = priorPosition;
        Map map = Track(new GameObject("Invalid New Party Map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Invalid New Party Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        RecordingRepository repository = new((DungeonRunSave)null);

        DungeonRunPersistenceBootstrapResult result =
            DungeonRunPersistenceBootstrap.StartGeneratedRun(
                map,
                Document(0, persisted: false),
                catalog,
                manager,
                new[] { party },
                new RecordingExplorationPresentation(),
                runtimeRoot,
                repository,
                new SequenceDungeonGenerator(),
                new DungeonEncounterPlanner(),
                Array.Empty<DungeonEncounterCandidate>()
            );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.InvalidSnapshot)
        );
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo("party"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain(nameof(CreatureComponent)));
        Assert.That(repository.LoadCount, Is.Zero);
        Assert.That(repository.SaveCount, Is.Zero);
        Assert.That(repository.Current, Is.Null);
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void GeneratedNewRunPreflightsGridDependenciesBeforePublication()
    {
        CombatManager manager = Track(new GameObject("Preflight New Run Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Preflight New Run Party", 12);
        Vector3 priorPosition = new(2f, 0f, 2f);
        party.transform.position = priorPosition;
        GameObject mapObject = Track(new GameObject("Preflight New Run Map"));
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog mapCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset"
        );
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(Document(0, false)))),
            mapCatalog
        );
        GameObject runtimeRoot = Track(new GameObject("Preflight New Run Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        DungeonRunSave priorSave = CreateRun(0);
        RecordingRepository repository = new(priorSave);

        DungeonRunPersistenceBootstrapResult result =
            DungeonRunPersistenceBootstrap.StartGeneratedRun(
                map,
                Document(0, persisted: false),
                catalog,
                manager,
                new[] { party },
                new RecordingExplorationPresentation(),
                runtimeRoot,
                repository,
                new SequenceDungeonGenerator(),
                new DungeonEncounterPlanner(),
                Array.Empty<DungeonEncounterCandidate>()
            );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo("runtime.preflight"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain(nameof(GridBase)));
        Assert.That(repository.CheckpointCount, Is.Zero);
        Assert.That(repository.SaveCount, Is.Zero);
        Assert.That(repository.Current, Is.SameAs(priorSave));
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void GeneratedNewRunRollsBackLatePresenterFailureAtomically()
    {
        CombatManager manager = Track(new GameObject("Rollback New Run Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Rollback New Run Party", 12);
        Vector3 priorPosition = new(2f, 0f, 2f);
        party.transform.position = priorPosition;
        Map map = CreateRuntimeReadyMap(
            "Rollback New Run Map",
            Document(0, persisted: false),
            out GridBase grid
        );
        Token partyToken = party.GetComponent<Token>();
        Assert.That(partyToken.IsRegistered, Is.True);
        string priorMapJson = DungeonLevelJsonSerializer.Serialize(
            map.ValidateSource().JsonMap.LevelDocument
        );
        GameObject runtimeRoot = Track(new GameObject("Rollback New Run Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        DungeonRunSave priorSave = CreateRun(0);
        RecordingRepository repository = new(priorSave);

        DungeonRunPersistenceBootstrapResult result =
            DungeonRunPersistenceBootstrap.StartGeneratedRun(
                map,
                Document(0, persisted: false),
                catalog,
                manager,
                new[] { party },
                new ThrowingExplorationPresentation(),
                runtimeRoot,
                repository,
                new SequenceDungeonGenerator(),
                new DungeonEncounterPlanner(),
                Array.Empty<DungeonEncounterCandidate>()
            );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.InvalidSnapshot)
        );
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo("runtime"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("presenter failure"));
        Assert.That(repository.CheckpointCount, Is.EqualTo(1));
        Assert.That(repository.SaveCount, Is.EqualTo(1));
        Assert.That(repository.RestoreCount, Is.EqualTo(1));
        Assert.That(repository.Current, Is.SameAs(priorSave));
        Assert.That(
            DungeonLevelJsonSerializer.Serialize(map.ValidateSource().JsonMap.LevelDocument),
            Is.EqualTo(priorMapJson)
        );
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
        Assert.That(map.GetMapData(), Is.SameAs(grid.GridData));
        Assert.That(map.GetLineOfSightBlocks(), Is.SameAs(grid.LineOfSightBlocks));
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(partyToken.IsRegistered, Is.True);
        Assert.That(grid.GetTiles()[2, 2].Occupants, Does.Contain(party.gameObject));
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void GeneratedNewRunPopulationFailureRollsBackPublishedSave()
    {
        CombatManager manager = Track(new GameObject("Population New Run Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Population New Run Party", 12);
        party.transform.position = new Vector3(2f, 0f, 2f);
        GameObject blocker = Track(new GameObject("Population New Run Blocker"));
        blocker.transform.position = new Vector3(1f, 0f, 1f);
        Token blockerToken = blocker.AddComponent<Token>();
        Map map = CreateRuntimeReadyMap(
            "Population New Run Map",
            Document(0, persisted: false),
            out GridBase grid
        );
        Token partyToken = party.GetComponent<Token>();
        Assert.That(partyToken.IsRegistered, Is.True);
        Assert.That(blockerToken.IsRegistered, Is.True);
        string priorMapJson = DungeonLevelJsonSerializer.Serialize(
            map.ValidateSource().JsonMap.LevelDocument
        );
        GameObject runtimeRoot = Track(new GameObject("Population New Run Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        DungeonRunSave priorSave = CreateRun(0);
        RecordingRepository repository = new(priorSave);

        DungeonRunPersistenceBootstrapResult result =
            DungeonRunPersistenceBootstrap.StartGeneratedRun(
                map,
                Document(0, persisted: false),
                catalog,
                manager,
                new[] { party },
                new RecordingExplorationPresentation(),
                runtimeRoot,
                repository,
                new SequenceDungeonGenerator(),
                new DungeonEncounterPlanner(),
                Array.Empty<DungeonEncounterCandidate>()
            );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo("population"));
        Assert.That(repository.SaveCount, Is.EqualTo(1));
        Assert.That(repository.RestoreCount, Is.EqualTo(1));
        Assert.That(repository.Current, Is.SameAs(priorSave));
        Assert.That(
            DungeonLevelJsonSerializer.Serialize(map.ValidateSource().JsonMap.LevelDocument),
            Is.EqualTo(priorMapJson)
        );
        Assert.That(party.transform.position, Is.EqualTo(new Vector3(2f, 0f, 2f)));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(partyToken.IsRegistered, Is.True);
        Assert.That(blockerToken.IsRegistered, Is.True);
        Assert.That(grid.GetTiles()[2, 2].Occupants, Does.Contain(party.gameObject));
        Assert.That(grid.GetTiles()[1, 1].Occupants, Does.Contain(blocker));
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void ContinueRejectsIncompatiblePresenterBeforeLoadOrSceneMutation()
    {
        CombatManager manager = Track(new GameObject("Invalid Continue Presenter Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty(
            "Invalid Continue Presenter Party",
            12
        );
        Vector3 priorPosition = new(2f, 0f, 1f);
        party.transform.position = priorPosition;
        Map map = Track(new GameObject("Invalid Continue Presenter Map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Invalid Continue Presenter Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        DungeonRunSave saved = CreateRun(0);
        RecordingRepository repository = new(saved);

        Assert.Throws<ArgumentException>(() =>
            DungeonRunPersistenceBootstrap.ContinueRun(
                map,
                Document(0, persisted: false),
                catalog,
                manager,
                new[] { party },
                new ExplorationOnlyPresentation(),
                runtimeRoot,
                repository
            )
        );

        Assert.That(repository.LoadCount, Is.Zero);
        Assert.That(repository.SaveCount, Is.Zero);
        Assert.That(repository.Current, Is.SameAs(saved));
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void ContinueRejectsPartyWithoutCreatureStateBeforeSceneMutation()
    {
        CombatManager manager = Track(new GameObject("Invalid Continue Party Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreatePartyWithoutCreatureState(
            "Invalid Continue Party"
        );
        Vector3 priorPosition = new(2f, 0f, 1f);
        party.transform.position = priorPosition;
        Map map = Track(new GameObject("Invalid Continue Party Map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Invalid Continue Party Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        DungeonRunSave saved = CreateRun(0);
        RecordingRepository repository = new(saved);

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.ContinueRun(
            map,
            Document(0, persisted: false),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.InvalidSnapshot)
        );
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo("party"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain(nameof(CreatureComponent)));
        Assert.That(repository.LoadCount, Is.EqualTo(1));
        Assert.That(repository.SaveCount, Is.Zero);
        Assert.That(repository.Current, Is.SameAs(saved));
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [TestCase(DungeonSaveDiagnosticCode.MissingSave)]
    [TestCase(DungeonSaveDiagnosticCode.CorruptSave)]
    [TestCase(DungeonSaveDiagnosticCode.IncompatibleVersion)]
    public void ContinueReturnsLoadDiagnosticBeforeScenePopulation(DungeonSaveDiagnosticCode code)
    {
        CombatManager manager = Track(new GameObject("Failed Continue Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Failed Continue Party", 12);
        Map map = Track(new GameObject("Failed Continue Map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Failed Continue Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        DungeonSaveDiagnostic diagnostic = new(code, "load-path", "load-message");
        RecordingRepository repository = new(diagnostic);

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.ContinueRun(
            map,
            Document(0, persisted: false),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(diagnostic.Code));
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo(diagnostic.Path));
        Assert.That(result.Diagnostics.Single().Message, Is.EqualTo(diagnostic.Message));
        Assert.That(repository.LoadCount, Is.EqualTo(1));
        Assert.That(repository.SaveCount, Is.Zero);
        Assert.That(runtimeRoot.GetComponent<DungeonEncounterRuntimeController>(), Is.Null);
        Assert.That(runtimeRoot.GetComponent<DungeonAutosaveCoordinator>(), Is.Null);
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
    }

    [Test]
    public void ContinuePrepositionsPartyBeforeRebindingDestinationGrid()
    {
        CombatManager manager = Track(new GameObject("Continue Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Continue Party", 12);
        party.transform.position = new Vector3(2f, 0f, 2f);
        GameObject mapObject = Track(new GameObject("Continue Map"));
        mapObject.SetActive(false);
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog mapCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset"
        );
        Assert.That(mapCatalog, Is.Not.Null);
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(Document(0, false)))),
            mapCatalog
        );
        GridBase grid = mapObject.AddComponent<GridBase>();
        Assert.That(
            grid.TryRebindMapData(
                map.GetMapData(),
                map.GetLineOfSightBlocks(),
                out string initializationFailure
            ),
            Is.True,
            initializationFailure
        );
        Token partyToken = party.GetComponent<Token>();
        Assert.That(grid.IsInitialized, Is.True);
        Assert.That(partyToken.IsRegistered, Is.True);

        DungeonRunSave saved = DungeonRunSave
            .CreateNew(PartyState(12), Document(0, persisted: true))
            .WithAddedAndSelectedFloor(PartyState(8), BlockedDestinationDocument(2));
        RecordingRepository repository = new(saved);
        GameObject runtimeRoot = Track(new GameObject("Continue Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.ContinueRun(
            map,
            Document(0, persisted: false),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );

        Assert.That(
            result.IsSuccess,
            Is.True,
            string.Join(" ", result.Diagnostics.Select(item => item.Message))
        );
        Assert.That(result.RestoredExistingRun, Is.True);
        Assert.That(map.ValidateSource().JsonMap.LevelDocument.Generation.Depth, Is.EqualTo(2));
        Assert.That(party.transform.position, Is.EqualTo(new Vector3(1f, 0f, 1f)));
        Assert.That(party.gameObject.activeSelf, Is.True);
        DungeonAutosaveCoordinator coordinator =
            runtimeRoot.GetComponent<DungeonAutosaveCoordinator>();
        Assert.That(coordinator.LastCommittedSnapshot.Manifest.Floors.Length, Is.EqualTo(2));
        Assert.That(party.GetComponent<CreatureComponent>().hp, Is.EqualTo(8));
    }

    [Test]
    public void ContinuePopulationFailureRestoresPartyPositionAndActivity()
    {
        CombatManager manager = Track(new GameObject("Failed Population Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Failed Population Party", 12);
        Vector3 priorPosition = new(0f, 1.25f, 0f);
        party.transform.position = priorPosition;

        GameObject blocker = Track(new GameObject("Destination Rebind Blocker"));
        blocker.transform.position = new Vector3(2f, 0f, 2f);
        Token blockerToken = blocker.AddComponent<Token>();
        GameObject mapObject = Track(new GameObject("Failed Population Map"));
        mapObject.SetActive(false);
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog mapCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset"
        );
        Assert.That(mapCatalog, Is.Not.Null);
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(Document(0, false)))),
            mapCatalog
        );
        GridBase grid = mapObject.AddComponent<GridBase>();
        Assert.That(
            grid.TryRebindMapData(
                map.GetMapData(),
                map.GetLineOfSightBlocks(),
                out string initializationFailure
            ),
            Is.True,
            initializationFailure
        );
        Token partyToken = party.GetComponent<Token>();
        Assert.That(grid.IsInitialized, Is.True);
        Assert.That(partyToken.IsRegistered, Is.True);
        Assert.That(blockerToken.IsRegistered, Is.True);
        party.gameObject.SetActive(false);

        DungeonRunSave saved = DungeonRunSave
            .CreateNew(PartyState(12), Document(0, persisted: true))
            .WithAddedAndSelectedFloor(PartyState(8), BlockedDestinationDocument(2));
        RecordingRepository repository = new(saved);
        GameObject runtimeRoot = Track(new GameObject("Failed Population Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.ContinueRun(
            map,
            Document(0, persisted: false),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo("floor"));
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.False);
        Assert.That(partyToken.IsRegistered, Is.True);
        Assert.That(grid.GetTiles()[0, 0].Occupants, Does.Contain(party.gameObject));
        Assert.That(map.ValidateSource().JsonMap.LevelDocument.Generation.Depth, Is.Zero);
        Assert.That(runtimeRoot.GetComponent<DungeonEncounterRuntimeController>(), Is.Null);
    }

    [Test]
    public void ContinuePopulationFailureRestoresInactiveRegisteredPartyReservation()
    {
        CombatManager manager = Track(new GameObject("Inactive Rollback Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Inactive Rollback Party", 12);
        Vector3 priorPosition = Vector3.zero;
        party.transform.position = priorPosition;
        GameObject blocker = Track(new GameObject("Inactive Rollback Blocker"));
        blocker.transform.position = new Vector3(2f, 0f, 2f);
        Token blockerToken = blocker.AddComponent<Token>();
        Map map = CreateRuntimeReadyMap(
            "Inactive Rollback Map",
            Document(0, persisted: false),
            out GridBase grid
        );
        Token partyToken = party.GetComponent<Token>();
        ActivationProbe activation = party.gameObject.AddComponent<ActivationProbe>();
        Assert.That(partyToken.IsRegistered, Is.True);
        Assert.That(blockerToken.IsRegistered, Is.True);
        party.gameObject.SetActive(false);
        Assert.That(partyToken.IsRegistered, Is.True);
        int priorEnableCount = activation.EnableCount;

        DungeonRunSave saved = DungeonRunSave
            .CreateNew(PartyState(12), Document(0, persisted: true))
            .WithAddedAndSelectedFloor(DefeatedPartyState(), BlockedDestinationDocument(2));
        RecordingRepository repository = new(saved);
        GameObject runtimeRoot = Track(new GameObject("Inactive Rollback Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.ContinueRun(
            map,
            Document(0, persisted: false),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics.Single().Path, Is.EqualTo("floor"));
        Assert.That(party.transform.position, Is.EqualTo(priorPosition));
        Assert.That(party.gameObject.activeSelf, Is.False);
        Assert.That(activation.EnableCount, Is.EqualTo(priorEnableCount));
        Assert.That(partyToken.IsRegistered, Is.True);
        Assert.That(grid.GetTiles()[0, 0].Occupants, Does.Contain(party.gameObject));
        Assert.That(grid.GetTiles()[0, 0].Occupants, Has.Count.EqualTo(1));
        Assert.That(blockerToken.IsRegistered, Is.True);
        Assert.That(map.ValidateSource().JsonMap.LevelDocument.Generation.Depth, Is.Zero);
        Assert.That(runtimeRoot.GetComponents<Component>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void ContinueKeepsDefeatedPartyDetachedAcrossDescentAndBacktracking()
    {
        CombatManager manager = Track(new GameObject("Casualty Continue Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController living = CreateParty(
            "Living Continue Party",
            12,
            "party-slot-living",
            "party-content-living"
        );
        DungeonPersistenceTestActionController defeated = CreateParty(
            "Defeated Continue Party",
            12,
            "party-slot-defeated",
            "party-content-defeated"
        );
        living.transform.position = new Vector3(4f, 0f, 1f);
        defeated.transform.position = new Vector3(1f, 0f, 1f);

        GameObject mapObject = Track(new GameObject("Casualty Continue Map"));
        mapObject.SetActive(false);
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog mapCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset"
        );
        Assert.That(mapCatalog, Is.Not.Null);
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(StairDocument(0)))),
            mapCatalog
        );
        GridBase grid = mapObject.AddComponent<GridBase>();
        Assert.That(
            grid.TryRebindMapData(
                map.GetMapData(),
                map.GetLineOfSightBlocks(),
                out string initializationFailure
            ),
            Is.True,
            initializationFailure
        );
        mapObject.SetActive(true);
        Assert.That(living.GetComponent<Token>().IsRegistered, Is.True);
        Assert.That(defeated.GetComponent<Token>().IsRegistered, Is.True);

        DungeonRunSave saved = DungeonRunSave.CreateNew(
            CasualtyPartyState(),
            PersistedStairDocument(0)
        );
        RecordingRepository repository = new(saved);
        GameObject runtimeRoot = Track(new GameObject("Casualty Continue Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );

        DungeonRunPersistenceBootstrapResult continued = DungeonRunPersistenceBootstrap.ContinueRun(
            map,
            StairDocument(0),
            catalog,
            manager,
            new[] { living, defeated },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );

        Assert.That(
            continued.IsSuccess,
            Is.True,
            string.Join(" ", continued.Diagnostics.Select(item => item.Message))
        );
        Assert.That(defeated.gameObject.activeSelf, Is.False);
        Assert.That(defeated.GetComponent<Token>().IsRegistered, Is.False);
        continued.Controller.ReplaceGenerationForTests(
            new SequenceDungeonGenerator(),
            new DungeonEncounterPlanner(),
            Array.Empty<DungeonEncounterCandidate>()
        );

        DungeonStairMarker down = map.GetComponentsInChildren<DungeonStairMarker>(
                includeInactive: false
            )
            .Single(stair => stair.Kind == DungeonStairKind.Down);
        DungeonTravelResult descent = continued.Controller.TryUseStair(down, confirmed: true);

        Assert.That(
            descent.IsSuccess,
            Is.True,
            string.Join(" ", descent.Diagnostics.Select(item => item.Message))
        );
        Assert.That(descent.Depth, Is.EqualTo(1));
        Assert.That(defeated.gameObject.activeSelf, Is.False);
        Assert.That(defeated.GetComponent<Token>().IsRegistered, Is.False);

        DungeonStairMarker up = map.GetComponentsInChildren<DungeonStairMarker>(
                includeInactive: false
            )
            .Single(stair => stair.Kind == DungeonStairKind.Up);
        Assert.That(
            Vector3Int.RoundToInt(living.transform.position),
            Is.EqualTo(new Vector3Int(up.ArrivalCell.X, 0, up.ArrivalCell.Z))
        );
        Assert.That(
            Vector3Int.RoundToInt(living.transform.position),
            Is.Not.EqualTo(new Vector3Int(up.Cell.X, 0, up.Cell.Z))
        );
        DungeonTravelResult backtrack = continued.Controller.TryUseStair(up, confirmed: true);

        Assert.That(
            backtrack.IsSuccess,
            Is.True,
            string.Join(" ", backtrack.Diagnostics.Select(item => item.Message))
        );
        Assert.That(backtrack.Depth, Is.Zero);
        Assert.That(living.gameObject.activeSelf, Is.True);
        Assert.That(defeated.gameObject.activeSelf, Is.False);
        Assert.That(defeated.GetComponent<Token>().IsRegistered, Is.False);
        DungeonPartyMemberSaveState persistedCasualty = repository.Current.Manifest.Party.Single(
            member => member.RosterSlotId == "party-slot-defeated"
        );
        Assert.That(persistedCasualty.IsDefeated, Is.True);
        Assert.That(persistedCasualty.CurrentHitPoints, Is.Zero);
        Assert.That(
            defeated.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
            Is.EqualTo(persistedCasualty.RosterSlotId)
        );
    }

    [Test]
    public void StairTravelRequiresConfirmationAndEveryLivingPartyMember()
    {
        DungeonTraversalFixture fixture = CreateTraversalFixture();
        DungeonStairMarker stair = CreateStairMarker(fixture.Map, DungeonStairKind.Down);

        DungeonTravelResult unconfirmed = fixture.Controller.TryUseStair(stair, confirmed: false);

        Assert.That(unconfirmed.IsSuccess, Is.False);
        Assert.That(
            unconfirmed.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.ConfirmationRequired)
        );
        fixture.Party.transform.position = Vector3.zero;

        DungeonTravelResult missing = fixture.Controller.TryUseStair(stair, confirmed: true);

        Assert.That(missing.IsSuccess, Is.False);
        Assert.That(
            missing.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.PartyMissing)
        );
        Assert.That(missing.MissingPartyMembers, Is.EqualTo(new[] { "party-slot" }));
        Assert.That(fixture.Repository.Current.Manifest.CurrentDepth, Is.Zero);
    }

    [Test]
    public void UpTravelRejectsSparseMissingShallowerHistoryWithoutGeneration()
    {
        CombatManager manager = Track(new GameObject("Sparse History Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Sparse History Party", 12);
        party.transform.position = new Vector3(1f, 0f, 1f);
        Map map = CreateRuntimeReadyMap("Sparse History Map", StairDocument(2), out GridBase _);
        DungeonRunSave sparse = DungeonRunSave
            .CreateNew(PartyState(12), PersistedStairDocument(0))
            .WithAddedAndSelectedFloor(PartyState(12), PersistedStairDocument(2));
        RecordingRepository repository = new(sparse);
        GameObject runtimeRoot = Track(new GameObject("Sparse History Runtime"));
        runtimeRoot.transform.SetParent(map.transform, false);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        DungeonRunPersistenceBootstrapResult continued = DungeonRunPersistenceBootstrap.ContinueRun(
            map,
            StairDocument(0),
            catalog,
            manager,
            new[] { party },
            new RecordingExplorationPresentation(),
            runtimeRoot,
            repository
        );
        Assert.That(
            continued.IsSuccess,
            Is.True,
            string.Join(" ", continued.Diagnostics.Select(item => item.Message))
        );
        CountingDungeonGenerator generator = new();
        continued.Controller.ReplaceGenerationForTests(
            generator,
            new DungeonEncounterPlanner(),
            Array.Empty<DungeonEncounterCandidate>()
        );
        DungeonStairMarker up = map.GetComponentsInChildren<DungeonStairMarker>(
                includeInactive: false
            )
            .Single(stair => stair.Kind == DungeonStairKind.Up);

        DungeonTravelResult result = continued.Controller.TryUseStair(up, confirmed: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.ValidationFailed)
        );
        Assert.That(result.Diagnostics.Single().Stage, Is.EqualTo("history"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("depth 1"));
        Assert.That(generator.CallCount, Is.Zero);
        Assert.That(repository.SaveCount, Is.Zero);
        Assert.That(repository.Current, Is.SameAs(sparse));
        Assert.That(continued.Controller.CurrentDepth, Is.EqualTo(2));
        Assert.That(continued.Runtime.IsInitialized, Is.True);
        Assert.That(map.ValidateSource().JsonMap.LevelDocument.Generation.Depth, Is.EqualTo(2));
        Assert.That(party.gameObject.activeSelf, Is.True);
        Assert.That(party.GetComponent<Token>().IsRegistered, Is.True);
    }

    [Test]
    public void StairActivationPresentsMissingPartyAndRequiresExplicitConfirmation()
    {
        DungeonTraversalFixture fixture = CreateTraversalFixture();
        DungeonStairMarker stair = CreateStairMarker(fixture.Map, DungeonStairKind.Down);
        fixture.Party.transform.position = Vector3.zero;

        fixture.Controller.RequestUseStair(stair);

        Assert.That(fixture.Presentation.LastPrompt, Is.Not.Null);
        Assert.That(fixture.Presentation.LastPrompt.CanConfirm, Is.False);
        Assert.That(
            fixture.Presentation.LastPrompt.MissingPartyMembers,
            Is.EqualTo(new[] { "party-slot" })
        );
        Assert.That(
            fixture.Controller.LastDiagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.PartyMissing)
        );

        fixture.Party.transform.position = new Vector3(
            stair.ArrivalCell.X,
            0f,
            stair.ArrivalCell.Z
        );
        fixture.Presentation.ConfirmNext = false;
        fixture.Controller.RequestUseStair(stair);

        Assert.That(fixture.Presentation.LastPrompt.CanConfirm, Is.True);
        Assert.That(
            fixture.Controller.LastDiagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.ConfirmationRequired)
        );

        fixture.Controller.ReplaceGenerationForTests(
            new SequenceDungeonGenerator(),
            new DungeonEncounterPlanner(),
            Array.Empty<DungeonEncounterCandidate>()
        );
        fixture.Presentation.ConfirmNext = true;
        fixture.Controller.RequestUseStair(stair);

        Assert.That(
            fixture.Controller.LastDiagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.PopulationFailed),
            "A confirmed production activation must invoke the traversal pipeline."
        );
        Assert.That(fixture.Controller.CurrentDepth, Is.Zero);
    }

    [Test]
    public void IdenticalStartingSeedsProduceIdenticalValidatedFloorSequences()
    {
        DungeonPersistenceTestActionController party = CreateParty("Sequence Party", 12);
        string[] first = AcquireSequence(157, party);
        string[] second = AcquireSequence(157, party);

        Assert.That(second, Is.EqualTo(first));
        DungeonLevelParseResult depthZero = DungeonLevelJsonParser.Parse(first[0]);
        Assert.That(depthZero.IsSuccess, Is.True);
        Assert.That(
            depthZero.Document.Stairs.Select(stair => stair.Kind),
            Is.EqualTo(new[] { DungeonStairKind.Down })
        );
    }

    [Test]
    public void GenerationFailureKeepsCurrentFloorSelectedAndReportsStage()
    {
        DungeonTraversalFixture fixture = CreateTraversalFixture();
        fixture.Controller.ReplaceGenerationForTests(
            new FailingDungeonGenerator(),
            new DungeonEncounterPlanner(),
            Array.Empty<DungeonEncounterCandidate>()
        );
        DungeonStairMarker stair = CreateStairMarker(fixture.Map, DungeonStairKind.Down);

        DungeonTravelResult result = fixture.Controller.TryUseStair(stair, confirmed: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.GenerationFailed)
        );
        Assert.That(result.Depth, Is.Zero);
        Assert.That(fixture.Controller.CurrentDepth, Is.Zero);
        Assert.That(fixture.Runtime.IsInitialized, Is.True);
        Assert.That(fixture.Repository.Current.Manifest.CurrentDepth, Is.Zero);
    }

    [Test]
    public void ReparseValidationFailureKeepsCurrentFloorPlayable()
    {
        DungeonTraversalFixture fixture = CreateTraversalFixture();
        fixture.Controller.ReplaceGenerationForTests(
            new InvalidDocumentGenerator(),
            new DungeonEncounterPlanner(),
            Array.Empty<DungeonEncounterCandidate>()
        );
        DungeonStairMarker stair = CreateStairMarker(fixture.Map, DungeonStairKind.Down);

        DungeonTravelResult result = fixture.Controller.TryUseStair(stair, confirmed: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.ValidationFailed)
        );
        Assert.That(fixture.Controller.CurrentDepth, Is.Zero);
        Assert.That(fixture.Runtime.IsInitialized, Is.True);
        Assert.That(fixture.Repository.Current.Manifest.CurrentDepth, Is.Zero);
    }

    [Test]
    public void SaveFailureKeepsCurrentFloorPlayableAndSelected()
    {
        DungeonTraversalFixture fixture = CreateTraversalFixture();
        fixture.Repository.FailSave = true;
        DungeonStairMarker stair = CreateStairMarker(fixture.Map, DungeonStairKind.Down);

        DungeonTravelResult result = fixture.Controller.TryUseStair(stair, confirmed: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.SaveFailed)
        );
        Assert.That(fixture.Controller.CurrentDepth, Is.Zero);
        Assert.That(fixture.Runtime.IsInitialized, Is.True);
        Assert.That(fixture.Repository.Current.Manifest.CurrentDepth, Is.Zero);
    }

    [Test]
    public void PopulationFailureRollsPublishedSelectionBackToPlayableFloor()
    {
        DungeonTraversalFixture fixture = CreateTraversalFixture();
        fixture.Controller.ReplaceGenerationForTests(
            new SequenceDungeonGenerator(),
            new DungeonEncounterPlanner(),
            Array.Empty<DungeonEncounterCandidate>()
        );
        DungeonStairMarker stair = CreateStairMarker(fixture.Map, DungeonStairKind.Down);

        DungeonTravelResult result = fixture.Controller.TryUseStair(stair, confirmed: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.PopulationFailed)
        );
        Assert.That(fixture.Controller.CurrentDepth, Is.Zero);
        Assert.That(fixture.Runtime.IsInitialized, Is.True);
        Assert.That(fixture.Repository.Current.Manifest.CurrentDepth, Is.Zero);
        Assert.That(fixture.Repository.Current.Manifest.Floors, Has.Length.EqualTo(1));
    }

    private DungeonTraversalFixture CreateTraversalFixture()
    {
        CombatManager manager = Track(new GameObject("Traversal Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Traversal Party", 12);
        party.transform.position = new Vector3(4f, 0f, 1f);
        Map map = Track(new GameObject("Traversal Map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Traversal Runtime"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        RecordingRepository repository = new((DungeonRunSave)null);
        RecordingExplorationPresentation presentation = new();
        DungeonRunPersistenceBootstrapResult bootstrap =
            DungeonRunPersistenceBootstrap.StartPreparedRunForTests(
                map,
                StairDocument(0),
                catalog,
                manager,
                new[] { party },
                presentation,
                runtimeRoot,
                repository
            );
        Assert.That(
            bootstrap.IsSuccess,
            Is.True,
            string.Join(" ", bootstrap.Diagnostics.Select(item => item.Message))
        );
        return new DungeonTraversalFixture(
            map,
            party,
            bootstrap.Runtime,
            bootstrap.Controller,
            repository,
            presentation
        );
    }

    private static string[] AcquireSequence(int seed, DungeonPersistenceTestActionController party)
    {
        List<string> floors = new();
        for (int depth = 0; depth <= 3; depth++)
        {
            DungeonTravelDiagnostic diagnostic = DungeonRunController.AcquireFirstVisit(
                new DeterministicDungeonGenerator(),
                new DungeonEncounterPlanner(),
                Array.Empty<DungeonEncounterCandidate>(),
                new[] { party },
                seed,
                depth,
                width: 31,
                height: 31,
                out DungeonLevelDocument document
            );
            Assert.That(diagnostic, Is.Null, diagnostic?.Message);
            floors.Add(DungeonLevelJsonSerializer.Serialize(document));
        }
        return floors.ToArray();
    }

    private DungeonStairMarker CreateStairMarker(Map map, DungeonStairKind kind)
    {
        GameObject stairObject = Track(new GameObject("Traversal Stair"));
        stairObject.transform.SetParent(map.transform, false);
        DungeonStair stair = StairDocument(0).Stairs.Single(item => item.Kind == kind);
        DungeonStairMarker marker = stairObject.AddComponent<DungeonStairMarker>();
        marker.Configure(stair.Id, stair.Kind, stair.Cell, stair.ArrivalCell);
        return marker;
    }

    private DungeonPersistenceTestActionController CreateParty(
        string name,
        int hitPoints,
        string rosterSlotId = "party-slot",
        string creatureContentId = "party-content"
    )
    {
        GameObject partyObject = Track(new GameObject(name));
        DungeonPersistenceTestActionController party =
            partyObject.AddComponent<DungeonPersistenceTestActionController>();
        partyObject
            .AddComponent<CreatureComponent>()
            .InitializeHealthBeforeEncounter(hitPoints, 12);
        partyObject.AddComponent<Conditions>();
        partyObject.AddComponent<Token>();
        partyObject.transform.position = new Vector3(1f, 0f, 1f);
        partyObject
            .AddComponent<DungeonPartyMemberIdentity>()
            .Configure(rosterSlotId, creatureContentId);
        return party;
    }

    private Map CreateRuntimeReadyMap(string name, DungeonLevelDocument document, out GridBase grid)
    {
        GameObject mapObject = Track(new GameObject(name));
        mapObject.SetActive(false);
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog mapCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset"
        );
        Assert.That(mapCatalog, Is.Not.Null);
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(document))),
            mapCatalog
        );
        grid = mapObject.AddComponent<GridBase>();
        Assert.That(
            grid.TryRebindMapData(
                map.GetMapData(),
                map.GetLineOfSightBlocks(),
                out string initializationFailure
            ),
            Is.True,
            initializationFailure
        );
        mapObject.SetActive(true);
        return map;
    }

    private DungeonPersistenceTestActionController CreatePartyWithoutCreatureState(string name)
    {
        GameObject partyObject = Track(new GameObject(name));
        DungeonPersistenceTestActionController party =
            partyObject.AddComponent<DungeonPersistenceTestActionController>();
        partyObject.AddComponent<Conditions>();
        partyObject.AddComponent<Token>();
        partyObject
            .AddComponent<DungeonPartyMemberIdentity>()
            .Configure("party-slot", "party-content");
        return party;
    }

    private T Track<T>(T value)
        where T : UnityEngine.Object
    {
        cleanup.Add(value);
        return value;
    }

    private static DungeonRunSave CreateRun(params int[] depths)
    {
        DungeonRunSave save = DungeonRunSave.CreateNew(
            PartyState(12),
            Document(depths[0], persisted: true)
        );
        foreach (int depth in depths.Skip(1))
        {
            save = save.WithAddedAndSelectedFloor(PartyState(12), Document(depth, persisted: true));
        }
        return save;
    }

    private static DungeonPartyMemberSaveState[] PartyState(int hitPoints) =>
        new[]
        {
            new DungeonPartyMemberSaveState
            {
                RosterSlotId = "party-slot",
                CreatureContentId = "party-content",
                CellX = 1,
                CellZ = 1,
                CurrentHitPoints = hitPoints,
                IsDefeated = false,
                State = EmptyActorState(),
            },
        };

    private static DungeonPartyMemberSaveState[] DefeatedPartyState() =>
        new[]
        {
            new DungeonPartyMemberSaveState
            {
                RosterSlotId = "party-slot",
                CreatureContentId = "party-content",
                CellX = 1,
                CellZ = 1,
                CurrentHitPoints = 0,
                IsDefeated = true,
                State = EmptyActorState(),
            },
        };

    private static DungeonPartyMemberSaveState[] CasualtyPartyState() =>
        new[]
        {
            new DungeonPartyMemberSaveState
            {
                RosterSlotId = "party-slot-living",
                CreatureContentId = "party-content-living",
                CellX = 4,
                CellZ = 1,
                CurrentHitPoints = 12,
                IsDefeated = false,
                State = EmptyActorState(),
            },
            new DungeonPartyMemberSaveState
            {
                RosterSlotId = "party-slot-defeated",
                CreatureContentId = "party-content-defeated",
                CellX = 1,
                CellZ = 1,
                CurrentHitPoints = 0,
                IsDefeated = true,
                State = EmptyActorState(),
            },
        };

    private static DungeonActorSaveState EmptyActorState() =>
        new()
        {
            TemporaryHitPointSource = string.Empty,
            TemporaryHitPointImmunities = Array.Empty<string>(),
            Conditions = Array.Empty<DungeonConditionSaveState>(),
            TimedEffects = Array.Empty<DungeonTimedEffectSaveState>(),
            PreparedEffects = Array.Empty<DungeonPreparedEffectSaveState>(),
            Equipment = new DungeonEquipmentSaveState
            {
                LeftHandId = string.Empty,
                RightHandId = string.Empty,
                ArmorId = string.Empty,
                Ammunition = Array.Empty<AmmoCount>(),
                UnloadedWeaponIds = Array.Empty<string>(),
            },
        };

    private static DungeonLevelDocument Document(int depth, bool persisted)
    {
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata("test-generator", 123, depth, depth),
            new[] { "...", "...", "..." },
            new[] { new DungeonRoom(1, 0, 0, 2, 2) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>(),
            persisted
                ? new DungeonRuntimeState(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()
                )
                : null
        );
    }

    private static DungeonLevelDocument BlockedDestinationDocument(int depth) =>
        new(
            new DungeonGenerationMetadata("test-generator", 123, depth, depth),
            new[] { "..#", "...", "..." },
            new[] { new DungeonRoom(1, 0, 0, 1, 1) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>(),
            new DungeonRuntimeState(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            )
        );

    private static DungeonLevelDocument StairDocument(int depth)
    {
        DungeonStair[] stairs =
            depth == 0
                ? new[]
                {
                    new DungeonStair(
                        "stair-down",
                        DungeonStairKind.Down,
                        new DungeonCell(5, 1),
                        new DungeonCell(4, 1)
                    ),
                }
                : new[]
                {
                    new DungeonStair(
                        "stair-down",
                        DungeonStairKind.Down,
                        new DungeonCell(5, 1),
                        new DungeonCell(4, 1)
                    ),
                    new DungeonStair(
                        "stair-up",
                        DungeonStairKind.Up,
                        new DungeonCell(0, 1),
                        new DungeonCell(1, 1)
                    ),
                };
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata("test-generator", 123, depth, 0),
            new[] { ".......", ".......", "......." },
            new[] { new DungeonRoom(1, 0, 0, 2, 2), new DungeonRoom(2, 4, 0, 6, 2) },
            Array.Empty<DungeonDoor>(),
            stairs,
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>()
        );
    }

    private static DungeonLevelDocument PersistedStairDocument(int depth)
    {
        DungeonLevelDocument source = StairDocument(depth);
        return new DungeonLevelDocument(
            source.Generation,
            source.Rows,
            source.Rooms,
            source.Doors,
            source.Stairs,
            source.StartCell,
            source.SafeCells,
            source.Objects,
            source.EncounterPlans,
            new DungeonRuntimeState(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            )
        );
    }

    private sealed class DungeonTraversalFixture
    {
        internal DungeonTraversalFixture(
            Map map,
            DungeonPersistenceTestActionController party,
            DungeonEncounterRuntimeController runtime,
            DungeonRunController controller,
            RecordingRepository repository,
            RecordingExplorationPresentation presentation
        )
        {
            Map = map;
            Party = party;
            Runtime = runtime;
            Controller = controller;
            Repository = repository;
            Presentation = presentation;
        }

        internal Map Map { get; }
        internal DungeonPersistenceTestActionController Party { get; }
        internal DungeonEncounterRuntimeController Runtime { get; }
        internal DungeonRunController Controller { get; }
        internal RecordingRepository Repository { get; }
        internal RecordingExplorationPresentation Presentation { get; }
    }

    private sealed class ActivationProbe : MonoBehaviour
    {
        internal int EnableCount { get; private set; }

        private void OnEnable()
        {
            EnableCount++;
        }
    }

    private sealed class FailingDungeonGenerator : IDungeonGenerator
    {
        public DungeonGenerationResult Generate(DungeonGenerationRequest request) =>
            new(
                null,
                new[]
                {
                    new DungeonGenerationDiagnostic(
                        DungeonGenerationDiagnosticCode.RetryLimitExhausted,
                        "topology",
                        "Simulated generation failure."
                    ),
                }
            );
    }

    private sealed class SequenceDungeonGenerator : IDungeonGenerator
    {
        public DungeonGenerationResult Generate(DungeonGenerationRequest request) =>
            new(StairDocument(request.Depth), Array.Empty<DungeonGenerationDiagnostic>());
    }

    private sealed class CountingDungeonGenerator : IDungeonGenerator
    {
        internal int CallCount { get; private set; }

        public DungeonGenerationResult Generate(DungeonGenerationRequest request)
        {
            CallCount++;
            return new DungeonGenerationResult(
                StairDocument(request.Depth),
                Array.Empty<DungeonGenerationDiagnostic>()
            );
        }
    }

    private sealed class InvalidDocumentGenerator : IDungeonGenerator
    {
        public DungeonGenerationResult Generate(DungeonGenerationRequest request) =>
            new(
                new DungeonLevelDocument(
                    new DungeonGenerationMetadata("invalid-generator", 123, request.Depth, 0),
                    Array.Empty<string>(),
                    Array.Empty<DungeonRoom>(),
                    Array.Empty<DungeonDoor>(),
                    Array.Empty<DungeonStair>(),
                    new DungeonCell(0, 0),
                    Array.Empty<DungeonCell>(),
                    Array.Empty<DungeonObjectPlacement>(),
                    Array.Empty<DungeonEncounterPlan>()
                ),
                Array.Empty<DungeonGenerationDiagnostic>()
            );
    }

    private sealed class RecordingExplorationPresentation
        : IDungeonExplorationPresentation,
            IDungeonStairTraversalPresentation
    {
        internal bool ConfirmNext { get; set; }
        internal DungeonStairTraversalPrompt LastPrompt { get; private set; }

        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) { }

        public void HideExploration() { }

        public void PresentStairTraversal(DungeonStairTraversalPrompt prompt, Action<bool> respond)
        {
            LastPrompt = prompt;
            respond(ConfirmNext);
        }

        public void DismissStairTraversal() { }
    }

    private sealed class ThrowingExplorationPresentation
        : IDungeonExplorationPresentation,
            IDungeonStairTraversalPresentation
    {
        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) => throw new InvalidOperationException("Simulated presenter failure.");

        public void HideExploration() { }

        public void PresentStairTraversal(
            DungeonStairTraversalPrompt prompt,
            Action<bool> respond
        ) { }

        public void DismissStairTraversal() { }
    }

    private sealed class ExplorationOnlyPresentation : IDungeonExplorationPresentation
    {
        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) { }

        public void HideExploration() { }
    }

    private sealed class RecordingRepository : IDungeonSaveRepository
    {
        private sealed class Checkpoint : IDungeonSaveRepositoryCheckpoint
        {
            internal Checkpoint(DungeonRunSave current)
            {
                Current = current;
            }

            internal DungeonRunSave Current { get; }
        }

        private readonly DungeonSaveDiagnostic loadFailure;

        internal RecordingRepository(DungeonRunSave current)
        {
            Current = current;
        }

        internal RecordingRepository(DungeonSaveDiagnostic loadFailure)
        {
            this.loadFailure = loadFailure;
        }

        internal int LoadCount { get; private set; }
        internal int SaveCount { get; private set; }
        internal int CheckpointCount { get; private set; }
        internal int RestoreCount { get; private set; }
        internal bool FailSave { get; set; }
        internal DungeonRunSave Current { get; private set; }

        public DungeonSaveResult<DungeonRunSave> Load()
        {
            LoadCount++;
            return loadFailure == null
                ? DungeonSaveResult<DungeonRunSave>.Success(Current)
                : DungeonSaveResult<DungeonRunSave>.Failure(
                    loadFailure.Code,
                    loadFailure.Path,
                    loadFailure.Message
                );
        }

        public DungeonSaveResult<bool> Save(DungeonRunSave save)
        {
            SaveCount++;
            if (FailSave)
            {
                return DungeonSaveResult<bool>.Failure(
                    DungeonSaveDiagnosticCode.IoFailure,
                    "publish",
                    "Simulated publication failure."
                );
            }
            Current = save;
            return DungeonSaveResult<bool>.Success(true);
        }

        public DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> CaptureCheckpoint()
        {
            CheckpointCount++;
            return DungeonSaveResult<IDungeonSaveRepositoryCheckpoint>.Success(
                new Checkpoint(Current)
            );
        }

        public DungeonSaveResult<bool> RestoreCheckpoint(
            IDungeonSaveRepositoryCheckpoint checkpoint
        )
        {
            RestoreCount++;
            if (checkpoint is not Checkpoint captured)
            {
                return DungeonSaveResult<bool>.Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "checkpoint",
                    "Unexpected repository checkpoint type."
                );
            }
            Current = captured.Current;
            return DungeonSaveResult<bool>.Success(true);
        }
    }
}
