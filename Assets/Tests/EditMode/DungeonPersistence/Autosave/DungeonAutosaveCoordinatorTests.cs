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

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.StartNewRun(
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
    public void ContinueRestoresOnlyIndexedCurrentFloorAndRetainsCompleteBaseline()
    {
        CombatManager manager = Track(new GameObject("Continue Combat Manager"))
            .AddComponent<CombatManager>();
        DungeonPersistenceTestActionController party = CreateParty("Continue Party", 12);
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
        DungeonRunSave saved = CreateRun(0, 2).WithSelectedFloor(2, PartyState(8));
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
        DungeonAutosaveCoordinator coordinator =
            runtimeRoot.GetComponent<DungeonAutosaveCoordinator>();
        Assert.That(coordinator.LastCommittedSnapshot.Manifest.Floors.Length, Is.EqualTo(2));
        Assert.That(party.GetComponent<CreatureComponent>().hp, Is.EqualTo(8));
    }

    private DungeonPersistenceTestActionController CreateParty(string name, int hitPoints)
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

    private sealed class RecordingExplorationPresentation : IDungeonExplorationPresentation
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
    }
}
