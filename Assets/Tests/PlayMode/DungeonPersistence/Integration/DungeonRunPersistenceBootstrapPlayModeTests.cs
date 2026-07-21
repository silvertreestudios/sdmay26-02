using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;
using Game.KayKit;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Verifies the production persistence composition boundary with isolated storage.</summary>
public sealed class DungeonRunPersistenceBootstrapPlayModeTests
{
    private readonly List<Object> cleanup = new();
    private string repositoryRoot;

    /// <summary>Removes scene singletons and creates a short isolated repository path.</summary>
    [SetUp]
    public void SetUp()
    {
        DestroyExistingRuntime();
        repositoryRoot = Path.GetFullPath(
            Path.Combine(
                Application.dataPath,
                "..",
                ".agent-temp",
                "ds-b",
                Guid.NewGuid().ToString("N")
            )
        );
    }

    /// <summary>Destroys synthetic runtime objects and isolated repository files.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
                Object.DestroyImmediate(cleanup[index]);
        }
        cleanup.Clear();
        if (Directory.Exists(repositoryRoot))
            Directory.Delete(repositoryRoot, true);
        yield return null;
    }

    /// <summary>Verifies authored serialized IDs are usable without runtime name derivation.</summary>
    [Test]
    public void SerializedPartyIdentityIsConfigured()
    {
        DungeonPartyMemberIdentity identity = Track(new GameObject("Authored identity"))
            .AddComponent<DungeonPartyMemberIdentity>();

        JsonUtility.FromJsonOverwrite(
            "{\"rosterSlotId\":\"roster-alpha\",\"actorInstanceId\":\"actor-alpha\",\"creatureContentId\":\"fighter-alpha\"}",
            identity
        );

        Assert.That(identity.IsConfigured, Is.True);
        Assert.That(identity.RosterSlotId, Is.EqualTo("roster-alpha"));
        Assert.That(identity.ActorInstanceId, Is.EqualTo("actor-alpha"));
        Assert.That(identity.CreatureContentId, Is.EqualTo("fighter-alpha"));
    }

    /// <summary>
    /// Verifies a missing save creates one run, orders its authored party, and commits the floor
    /// before the bootstrap reports gameplay readiness.
    /// </summary>
    [Test]
    public void MissingSaveCreatesAndCommitsNewRunBeforeGameplay()
    {
        Track(new GameObject("Bootstrap combat log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Bootstrap team rules")).AddComponent<TeamRules>();
        CombatManager manager = Track(new GameObject("Bootstrap combat manager"))
            .AddComponent<CombatManager>();
        RuntimeTestActionController beta = CreatePartyMember(
            manager,
            "Beta",
            new Vector3(1f, 0f, 0f),
            "roster-beta",
            "actor-beta",
            "fighter-beta"
        );
        RuntimeTestActionController alpha = CreatePartyMember(
            manager,
            "Alpha",
            Vector3.zero,
            "roster-alpha",
            "actor-alpha",
            "fighter-alpha"
        );
        Map map = Track(new GameObject("Bootstrap map")).AddComponent<Map>();
        GameObject runtimeRoot = Track(new GameObject("Bootstrap runtime root"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        FileSystemDungeonSaveRepository repository = new(repositoryRoot);

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.Initialize(
            map,
            Document(),
            catalog,
            manager,
            new ActionController[] { beta, alpha },
            new RecordingExplorationPresentation(),
            repository,
            runtimeRoot
        );

        Assert.That(result, Is.TypeOf<DungeonRunPersistenceBootstrapSuccess>());
        DungeonRunPersistenceBootstrapSuccess success =
            (DungeonRunPersistenceBootstrapSuccess)result;
        Assert.That(success.RestoredExistingRun, Is.False);
        Assert.That(success.Runtime.IsInitialized, Is.True);
        Assert.That(success.AutosaveCoordinator.LastResult.IsSuccess, Is.True);
        Assert.That(success.Session.HasCommittedSave, Is.True);
        DungeonSaveLoadSuccess load = repository.Load() as DungeonSaveLoadSuccess;
        Assert.That(load, Is.Not.Null);
        Assert.That(load.Save.Manifest.Party.Members, Has.Count.EqualTo(2));
        Assert.That(load.Save.Manifest.Party.Members[0].RosterSlotId, Is.EqualTo("roster-alpha"));
        Assert.That(load.Save.Manifest.Party.Members[1].RosterSlotId, Is.EqualTo("roster-beta"));
        Assert.That(load.Save.Manifest.Party.LeaderRosterSlotId, Is.EqualTo("roster-alpha"));
    }

    /// <summary>
    /// Verifies the production restore path repopulates its map, reorders the authored party,
    /// restores registered grid actors, and resumes autosave ownership without rewriting on load.
    /// </summary>
    [Test]
    public void CommittedSaveRestoresThroughMapAndRuntimeComposition()
    {
        Track(new GameObject("Restore bootstrap combat log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Restore bootstrap team rules")).AddComponent<TeamRules>();
        CombatManager manager = Track(new GameObject("Restore bootstrap combat manager"))
            .AddComponent<CombatManager>();
        RuntimeTestActionController beta = CreatePartyMember(
            manager,
            "Beta",
            new Vector3(1f, 0.25f, 0f),
            "roster-beta",
            "actor-beta",
            "fighter-beta"
        );
        RuntimeTestActionController alpha = CreatePartyMember(
            manager,
            "Alpha",
            new Vector3(0f, 0.5f, 0f),
            "roster-alpha",
            "actor-alpha",
            "fighter-alpha"
        );
        DungeonLevelDocument document = Document();
        KayKitDungeonCatalog mapCatalog = CreateMapCatalog();
        GameObject mapOwner = Track(new GameObject("Restore bootstrap map"));
        mapOwner.SetActive(false);
        Map map = mapOwner.AddComponent<Map>();
        JsonUtility.FromJsonOverwrite("{\"legacyBitmapMigrationVersion\":1}", map);
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(document))),
            mapCatalog
        );
        GridBase grid = mapOwner.AddComponent<GridBase>();
        FileSystemDungeonSaveRepository repository = new(repositoryRoot);
        GameObject newRunRoot = Track(new GameObject("New run runtime root"));

        DungeonRunPersistenceBootstrapResult created = DungeonRunPersistenceBootstrap.Initialize(
            map,
            document,
            Track(ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()),
            manager,
            new ActionController[] { beta, alpha },
            new RecordingExplorationPresentation(),
            repository,
            newRunRoot
        );
        Assert.That(created, Is.TypeOf<DungeonRunPersistenceBootstrapSuccess>());
        Object.DestroyImmediate(newRunRoot);

        DungeonLevelDocument shellDocument = ShellDocument();
        map.ConfigureJson(
            Track(new TextAsset(DungeonLevelJsonSerializer.Serialize(shellDocument))),
            mapCatalog
        );
        alpha.transform.position = new Vector3(4f, 0.5f, 4f);
        beta.transform.position = new Vector3(4f, 0.25f, 3f);
        mapOwner.SetActive(true);
        Assert.That(grid.IsInitialized, Is.True);
        Assert.That(alpha.GetComponent<Token>().IsRegistered, Is.True);
        Assert.That(beta.GetComponent<Token>().IsRegistered, Is.True);
        GameObject restoredRoot = Track(new GameObject("Restored runtime root"));
        restoredRoot.transform.SetParent(map.transform, false);

        DungeonRunPersistenceBootstrapResult restored = DungeonRunPersistenceBootstrap.Initialize(
            map,
            shellDocument,
            Track(ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()),
            manager,
            new ActionController[] { beta, alpha },
            new RecordingExplorationPresentation(),
            repository,
            restoredRoot
        );

        Assert.That(
            restored,
            Is.TypeOf<DungeonRunPersistenceBootstrapSuccess>(),
            string.Join(
                Environment.NewLine,
                restored.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}"
                )
            )
        );
        DungeonRunPersistenceBootstrapSuccess success =
            (DungeonRunPersistenceBootstrapSuccess)restored;
        Assert.That(success.RestoredExistingRun, Is.True);
        Assert.That(
            success.AutosaveCoordinator.LastResult.Outcome,
            Is.EqualTo(Game.DungeonPersistence.Autosave.DungeonAutosaveAttemptOutcome.NotAttempted)
        );
        Assert.That(alpha.transform.position, Is.EqualTo(new Vector3(0f, 0.5f, 0f)));
        Assert.That(beta.transform.position, Is.EqualTo(new Vector3(1f, 0.25f, 0f)));
        Assert.That(grid.GetTiles()[0, 0].Occupants, Is.EqualTo(new[] { alpha.gameObject }));
        Assert.That(grid.GetTiles()[1, 0].Occupants, Is.EqualTo(new[] { beta.gameObject }));
        Assert.That(
            success.Runtime.CapturePartyControllers(),
            Is.EqualTo(new ActionController[] { alpha, beta })
        );
    }

    /// <summary>
    /// Verifies incompatible actor content discovered after population restores the original map,
    /// party positions, grid occupancy, and authored source configuration.
    /// </summary>
    [Test]
    public void IncompatibleActorContentRollsBackScenePopulation()
    {
        Track(new GameObject("Rollback bootstrap combat log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Rollback bootstrap team rules")).AddComponent<TeamRules>();
        CombatManager manager = Track(new GameObject("Rollback bootstrap combat manager"))
            .AddComponent<CombatManager>();
        RuntimeTestActionController beta = CreatePartyMember(
            manager,
            "Beta",
            new Vector3(1f, 0.25f, 0f),
            "roster-beta",
            "actor-beta",
            "fighter-beta"
        );
        RuntimeTestActionController alpha = CreatePartyMember(
            manager,
            "Alpha",
            new Vector3(0f, 0.5f, 0f),
            "roster-alpha",
            "actor-alpha",
            "fighter-alpha"
        );
        DungeonLevelDocument savedDocument = Document();
        DungeonLevelDocument shellDocument = ShellDocument();
        KayKitDungeonCatalog mapCatalog = CreateMapCatalog();
        GameObject mapOwner = Track(new GameObject("Rollback bootstrap map"));
        mapOwner.SetActive(false);
        Map map = mapOwner.AddComponent<Map>();
        JsonUtility.FromJsonOverwrite("{\"legacyBitmapMigrationVersion\":1}", map);
        TextAsset savedSource = Track(
            new TextAsset(DungeonLevelJsonSerializer.Serialize(savedDocument))
        );
        map.ConfigureJson(savedSource, mapCatalog);
        GridBase grid = mapOwner.AddComponent<GridBase>();
        FileSystemDungeonSaveRepository repository = new(repositoryRoot);
        GameObject newRunRoot = Track(new GameObject("Rollback new run root"));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        Assert.That(
            DungeonRunPersistenceBootstrap.Initialize(
                map,
                savedDocument,
                catalog,
                manager,
                new ActionController[] { beta, alpha },
                new RecordingExplorationPresentation(),
                repository,
                newRunRoot
            ),
            Is.TypeOf<DungeonRunPersistenceBootstrapSuccess>()
        );
        Object.DestroyImmediate(newRunRoot);
        DungeonRunSave saved = ((DungeonSaveLoadSuccess)repository.Load()).Save;
        Assert.That(repository.Save(WithIncompatiblePreparedState(saved)).IsSuccess, Is.True);

        TextAsset shellSource = Track(
            new TextAsset(DungeonLevelJsonSerializer.Serialize(shellDocument))
        );
        map.ConfigureJson(shellSource, mapCatalog);
        Vector3 originalAlphaPosition = new(4f, 0.5f, 4f);
        Vector3 originalBetaPosition = new(4f, 0.25f, 3f);
        alpha.transform.position = originalAlphaPosition;
        beta.transform.position = originalBetaPosition;
        mapOwner.SetActive(true);
        Assert.That(grid.IsInitialized, Is.True);
        GameObject failedRoot = Track(new GameObject("Failed restored runtime root"));
        failedRoot.transform.SetParent(map.transform, false);

        DungeonRunPersistenceBootstrapResult result = DungeonRunPersistenceBootstrap.Initialize(
            map,
            shellDocument,
            catalog,
            manager,
            new ActionController[] { beta, alpha },
            new RecordingExplorationPresentation(),
            repository,
            failedRoot
        );

        Assert.That(result, Is.TypeOf<DungeonRunPersistenceBootstrapFailure>());
        Assert.That(
            result.Diagnostics,
            Has.None.Matches<DungeonSaveDiagnostic>(diagnostic =>
                diagnostic.Path == "runtime.rollback"
            )
        );
        Assert.That(map.UsesRuntimeJsonSource, Is.False);
        Assert.That(map.JsonSource, Is.SameAs(shellSource));
        Assert.That(grid.GetTiles().GetLength(0), Is.EqualTo(5));
        Assert.That(grid.GetTiles().GetLength(1), Is.EqualTo(5));
        Assert.That(alpha.transform.position, Is.EqualTo(originalAlphaPosition));
        Assert.That(beta.transform.position, Is.EqualTo(originalBetaPosition));
        Assert.That(grid.GetTiles()[4, 4].Occupants, Is.EqualTo(new[] { alpha.gameObject }));
        Assert.That(grid.GetTiles()[4, 3].Occupants, Is.EqualTo(new[] { beta.gameObject }));
        Assert.That(failedRoot.activeSelf, Is.False);
        Assert.That(
            failedRoot.GetComponentsInChildren<ActionController>(includeInactive: true),
            Is.Empty
        );
    }

    private static DungeonRunSave WithIncompatiblePreparedState(DungeonRunSave saved)
    {
        DungeonPartyMemberSaveState[] members = saved
            .Manifest.Party.Members.Select(member =>
            {
                if (member.RosterSlotId != "roster-alpha")
                    return member;
                DungeonCreatureSaveState creature = member.Creature;
                DungeonCreatureSaveState incompatible = new(
                    creature.InstanceId,
                    creature.CreatureContentId,
                    creature.Cell,
                    creature.Health,
                    creature.IsDefeated,
                    creature.Conditions,
                    creature.TimedEffects,
                    new DungeonPreparedRuleSaveState(
                        new[] { "self:incompatible-content" },
                        Array.Empty<DungeonPreparedEffectSaveState>(),
                        Array.Empty<DungeonSpellPoolSaveState>()
                    ),
                    creature.Equipment
                );
                return new DungeonPartyMemberSaveState(member.RosterSlotId, incompatible);
            })
            .ToArray();
        DungeonPartySaveState party = new(saved.Manifest.Party.LeaderRosterSlotId, members);
        DungeonRunSaveManifest manifest = new(
            saved.Manifest.DocumentVersion,
            saved.Manifest.StartingSeed,
            saved.Manifest.GeneratorVersion,
            saved.Manifest.CurrentDepth,
            party,
            saved.Manifest.GeneratedFloors
        );
        return new DungeonRunSave(manifest, saved.Floors);
    }

    private RuntimeTestActionController CreatePartyMember(
        CombatManager manager,
        string name,
        Vector3 position,
        string rosterSlotId,
        string actorInstanceId,
        string creatureContentId
    )
    {
        GameObject owner = Track(new GameObject(name));
        owner.transform.position = position;
        CreatureComponent creature = owner.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.InitializeHealthBeforeEncounter(10, 10);
        owner.AddComponent<Conditions>();
        Team team = owner.AddComponent<Team>();
        team.Name = "Players";
        RuntimeTestActionController controller = owner.AddComponent<RuntimeTestActionController>();
        owner.AddComponent<Token>();
        owner
            .AddComponent<DungeonPartyMemberIdentity>()
            .Configure(rosterSlotId, actorInstanceId, creatureContentId);
        manager.AddCombatant(controller);
        return controller;
    }

    private KayKitDungeonCatalog CreateMapCatalog()
    {
        KayKitDungeonCatalog catalog = Track(
            ScriptableObject.CreateInstance<KayKitDungeonCatalog>()
        );
        GameObject floor = Track(new GameObject("Synthetic dungeon floor prefab"));
        catalog.ConfigureStructure(null, floor, null, null, null, null);
        return catalog;
    }

    private static DungeonLevelDocument Document() =>
        new(
            new DungeonGenerationMetadata("bootstrap-test-generator", 159, 0, 0),
            new[] { "...", "...", "..." },
            new[] { new DungeonRoom(1, 0, 0, 2, 2) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(0, 0),
            new[] { new DungeonCell(0, 0), new DungeonCell(1, 0) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>()
        );

    private static DungeonLevelDocument ShellDocument() =>
        new(
            new DungeonGenerationMetadata("bootstrap-test-generator", 999, 0, 0),
            new[] { ".....", ".....", ".....", ".....", "....." },
            new[] { new DungeonRoom(1, 0, 0, 4, 4) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(4, 4),
            new[] { new DungeonCell(4, 4), new DungeonCell(4, 3) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>()
        );

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    private static void DestroyExistingRuntime()
    {
        HashSet<GameObject> owners = new();
        foreach (
            GameManager component in Object.FindObjectsByType<GameManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            CombatManagerInterface component in Object.FindObjectsByType<CombatManagerInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            CombatLogInterface component in Object.FindObjectsByType<CombatLogInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            TeamRules component in Object.FindObjectsByType<TeamRules>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            GridAPI component in Object.FindObjectsByType<GridAPI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (GameObject owner in owners)
            Object.DestroyImmediate(owner);
    }

    private sealed class RuntimeTestActionController : ActionController
    {
        /// <inheritdoc/>
        public override void EndTurn() => ResetEncounterTurnState();
    }

    private sealed class RecordingExplorationPresentation : IDungeonExplorationPresentation
    {
        /// <inheritdoc/>
        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) { }

        /// <inheritdoc/>
        public void HideExploration() { }
    }

    private sealed class RuntimeTestCombatLog : CombatLogInterface
    {
        /// <inheritdoc/>
        public override void DevMode() { }

        /// <inheritdoc/>
        public override void ReleaseMode() { }

        /// <inheritdoc/>
        public override void AddWhiteList(string tag) { }

        /// <inheritdoc/>
        public override void AddBlackList(string tag) { }

        /// <inheritdoc/>
        public override void Log(string msg) { }

        /// <inheritdoc/>
        public override void DevLog(string msg) { }

        /// <inheritdoc/>
        public override void DevLog(string msg, string tag) { }

        /// <inheritdoc/>
        public override void DevLog(string msg, List<string> tags) { }

        /// <inheritdoc/>
        public override void Log(string msg, string tag) { }

        /// <inheritdoc/>
        public override void Log(string msg, List<string> tags) { }

        /// <inheritdoc/>
        public override List<string> GetMessages() => new();
    }
}
