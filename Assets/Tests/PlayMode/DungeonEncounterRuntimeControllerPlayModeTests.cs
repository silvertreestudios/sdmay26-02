using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.KayKit;
using Game.Rules.Runtime;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Verifies transform-based room observation drives production encounter composition.</summary>
public sealed class DungeonEncounterRuntimeControllerPlayModeTests
{
    private const string GoblinJson = "DataFiles/pathfinder-monster-core/goblin-warrior";
    private readonly List<Object> cleanup = new();
    private UnityEngine.Random.State randomState;

    /// <summary>Removes scene singletons and fixes initiative randomness for each test.</summary>
    [SetUp]
    public void SetUp()
    {
        DestroyExistingRuntime();
        randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(158);
    }

    /// <summary>Destroys every synthetic runtime object and restores random state.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        UnityEngine.Random.state = randomState;
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
                Object.DestroyImmediate(cleanup[index]);
        }
        cleanup.Clear();
        yield return null;
    }

    /// <summary>
    /// Verifies room transitions are retained and suspension waits for party and enemy actions.
    /// </summary>
    [UnityTest]
    public IEnumerator PartyRoomTransitionsActivateAndSuspendEncounter()
    {
        Track(new GameObject("Test Combat Log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Team Rules")).AddComponent<TeamRules>();
        CombatManager manager = Track(new GameObject("Combat Manager"))
            .AddComponent<CombatManager>();
        RuntimeTestActionController player = CreatePlayer(manager);
        GameObject creaturePrefab = CreaturePrefab();
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        catalog.ReplaceEntries(
            new[]
            {
                new DungeonEncounterCreatureCatalogEntry(
                    "goblin-warrior",
                    GoblinJson,
                    creaturePrefab
                ),
            }
        );
        DungeonEncounterRuntimeController runtime = Track(
                new GameObject("Dungeon Encounter Runtime")
            )
            .AddComponent<DungeonEncounterRuntimeController>();
        RecordingExplorationPresentation presentation = new();
        runtime.InitializePristine(Document(), catalog, manager, new[] { player }, presentation);

        yield return null;
        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(player.IsInDungeonExploration, Is.True);
        Assert.That(presentation.ShowCount, Is.EqualTo(1));

        player.IsTakingAction = true;
        player.transform.position = new Vector3(2f, 0f, 2f);
        yield return null;
        Assert.That(manager.IsCombatActive, Is.False);

        player.transform.position = Vector3.zero;
        yield return null;
        Assert.That(manager.IsCombatActive, Is.False);

        player.IsTakingAction = false;
        yield return null;

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(player.IsInDungeonExploration, Is.True);
        Assert.That(presentation.HideCount, Is.EqualTo(1));
        Assert.That(
            runtime.Lifecycle.GetRoomEncounter(1).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );
        Assert.That(
            runtime.GetComponentsInChildren<DungeonEncounterMember>(),
            Has.Length.EqualTo(1)
        );

        RuntimeTestActionController enemy = runtime
            .GetComponentsInChildren<DungeonEncounterMember>()
            .Single()
            .GetComponent<RuntimeTestActionController>();
        enemy.transform.position = new Vector3(2f, 0f, 2f);
        player.transform.position = new Vector3(1f, 0f, 2f);
        int hideCountAfterRetreat = presentation.HideCount;
        int showCountAfterRetreat = presentation.ShowCount;
        yield return null;
        yield return null;

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(player.IsInDungeonExploration, Is.True);
        Assert.That(presentation.HideCount, Is.EqualTo(hideCountAfterRetreat));
        Assert.That(presentation.ShowCount, Is.EqualTo(showCountAfterRetreat));
        Assert.That(
            runtime.Lifecycle.GetRoomEncounter(1).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended),
            "An in-room survivor must not resume combat with a party member outside the room."
        );

        player.transform.position = new Vector3(2f, 0f, 2f);
        yield return null;

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(player.IsInDungeonExploration, Is.False);
        Assert.That(presentation.HideCount, Is.EqualTo(2));
        Assert.That(
            runtime.Lifecycle.GetRoomEncounter(1).State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );

        enemy.IsTakingAction = true;
        player.transform.position = Vector3.zero;
        yield return null;
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(enemy.IsTakingAction, Is.True);

        enemy.IsTakingAction = false;
        yield return null;
        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(player.IsInDungeonExploration, Is.True);
        Assert.That(presentation.ShowCount, Is.EqualTo(3));
        Assert.That(
            runtime.Lifecycle.GetRoomEncounter(1).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );
    }

    /// <summary>
    /// Verifies a floor reset detaches suspended survivors before the destination grid rebinds.
    /// </summary>
    [UnityTest]
    public IEnumerator FloorResetDetachesSuspendedSurvivorsFromGridAndCombatManager()
    {
        Track(new GameObject("Test Combat Log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Team Rules")).AddComponent<TeamRules>();
        TrackingCombatManager manager = Track(new GameObject("Combat Manager"))
            .AddComponent<TrackingCombatManager>();
        KayKitDungeonCatalog mapCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            "Assets/KayKit/Catalogs/KayKitDungeonCatalog.asset"
        );
        Assert.That(mapCatalog, Is.Not.Null);

        GameObject mapObject = Track(new GameObject("Encounter Grid Map"));
        mapObject.SetActive(false);
        Map map = mapObject.AddComponent<Map>();
        Assert.That(
            map.TryPopulateJson(
                DungeonLevelJsonSerializer.Serialize(Document()),
                mapCatalog,
                out MapSourceValidationResult initialValidation
            ),
            Is.True,
            string.Join(" ", initialValidation.Errors)
        );
        GridBase grid = mapObject.AddComponent<GridBase>();
        mapObject.SetActive(true);
        Assert.That(grid.IsInitialized, Is.True);

        RuntimeTestActionController player = CreatePlayer(manager);
        player.transform.position = Vector3.zero;
        Token playerToken = player.gameObject.AddComponent<Token>();
        Assert.That(playerToken.IsRegistered, Is.True);

        GameObject creaturePrefab = CreaturePrefab(new Vector3(1f, 0f, 0f));
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        catalog.ReplaceEntries(
            new[]
            {
                new DungeonEncounterCreatureCatalogEntry(
                    "goblin-warrior",
                    GoblinJson,
                    creaturePrefab
                ),
            }
        );
        DungeonEncounterRuntimeController runtime = Track(
                new GameObject("Dungeon Encounter Runtime")
            )
            .AddComponent<DungeonEncounterRuntimeController>();
        runtime.transform.SetParent(map.transform, false);
        runtime.InitializePristine(
            Document(),
            catalog,
            manager,
            new[] { player },
            new RecordingExplorationPresentation()
        );

        player.transform.position = new Vector3(2f, 0f, 2f);
        yield return null;
        DungeonEncounterMember survivor = runtime
            .GetComponentsInChildren<DungeonEncounterMember>()
            .Single();
        ActionController survivorController = survivor.GetComponent<ActionController>();
        Token survivorToken = survivor.GetComponent<Token>();
        Assert.That(survivorToken.IsRegistered, Is.True);
        Assert.That(manager.RegisteredCombatants, Does.Contain(survivorController));

        player.transform.position = Vector3.zero;
        yield return null;
        Assert.That(
            runtime.Lifecycle.GetRoomEncounter(1).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );

        MethodInfo reset = typeof(DungeonEncounterRuntimeController).GetMethod(
            "ResetForFloorTransition",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(reset, Is.Not.Null);
        reset.Invoke(runtime, null);

        Assert.That(survivorToken.IsRegistered, Is.False);
        Assert.That(manager.RegisteredCombatants, Has.No.Member(survivorController));
        Assert.That(manager.RegisteredCombatants, Does.Contain(player));
        Assert.That(playerToken.IsRegistered, Is.True);
        Assert.That(
            map.TryPopulateJson(
                DungeonLevelJsonSerializer.Serialize(EmptyDocument()),
                mapCatalog,
                out MapSourceValidationResult validation
            ),
            Is.True,
            string.Join(" ", validation.Errors)
        );
        Assert.That(playerToken.IsRegistered, Is.True);

        yield return null;
        Assert.That(survivor == null, Is.True);
    }

    /// <summary>Verifies an encounter-free generated floor still grants exploration authority.</summary>
    [UnityTest]
    public IEnumerator EncounterFreeFloorEnablesExplorationMovement()
    {
        Track(new GameObject("Test Combat Log")).AddComponent<RuntimeTestCombatLog>();
        CombatManager manager = Track(new GameObject("Combat Manager"))
            .AddComponent<CombatManager>();
        RuntimeTestActionController player = CreatePlayer(manager);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        RecordingExplorationPresentation presentation = new();
        DungeonEncounterRuntimeController runtime = Track(
                new GameObject("Dungeon Encounter Runtime")
            )
            .AddComponent<DungeonEncounterRuntimeController>();

        runtime.InitializePristine(
            EmptyDocument(),
            catalog,
            manager,
            new[] { player },
            presentation
        );
        yield return null;

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(player.IsInDungeonExploration, Is.True);
        Assert.That(presentation.ShowCount, Is.EqualTo(1));
    }

    /// <summary>Verifies a touched persisted encounter materializes before room re-entry.</summary>
    [UnityTest]
    public IEnumerator PersistedTouchedEncounterMaterializesWhenFloorLoads()
    {
        Track(new GameObject("Test Combat Log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Team Rules")).AddComponent<TeamRules>();
        CombatManager manager = Track(new GameObject("Combat Manager"))
            .AddComponent<CombatManager>();
        RuntimeTestActionController player = CreatePlayer(manager);
        player.transform.position = new Vector3(0f, 0f, 4f);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        catalog.ReplaceEntries(
            new[]
            {
                new DungeonEncounterCreatureCatalogEntry(
                    "goblin-warrior",
                    GoblinJson,
                    CreaturePrefab()
                ),
            }
        );
        DungeonEncounterRuntimeController runtime = Track(
                new GameObject("Dungeon Encounter Runtime")
            )
            .AddComponent<DungeonEncounterRuntimeController>();

        runtime.InitializePersisted(
            PersistedDocument(),
            catalog,
            manager,
            new[] { player },
            new RecordingExplorationPresentation()
        );
        yield return null;

        DungeonEncounterMember survivor = runtime
            .GetComponentsInChildren<DungeonEncounterMember>()
            .Single();
        Assert.That(survivor.InstanceId, Is.EqualTo("encounter-1/creature-0000"));
        Assert.That(survivor.GetComponent<CreatureComponent>().hp, Is.EqualTo(3));
        Assert.That(survivor.PersistentState, Is.EqualTo("persisted-survivor-state"));
        Assert.That(
            runtime.Lifecycle.GetEncounter("encounter-1").State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );
        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(player.IsInDungeonExploration, Is.True);

        survivor.transform.position = new Vector3(1f, 0f, 1f);
        player.transform.position = new Vector3(1f, 0f, 0f);
        yield return null;

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(player.IsInDungeonExploration, Is.False);
        Assert.That(
            runtime.Lifecycle.GetEncounter("encounter-1").State,
            Is.EqualTo(DungeonEncounterGroupState.Active),
            "Reaching a displaced corridor survivor should resume its encounter."
        );
    }

    /// <summary>Verifies exploration resumes after a final combat action without restoring AP.</summary>
    [UnityTest]
    public IEnumerator FinalDefeatDuringActionDefersExplorationUntilActionCompletes()
    {
        Track(new GameObject("Test Combat Log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Team Rules")).AddComponent<TeamRules>();
        CombatManager manager = Track(new GameObject("Combat Manager"))
            .AddComponent<CombatManager>();
        RuntimeTestActionController player = CreatePlayer(manager);
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        catalog.ReplaceEntries(
            new[]
            {
                new DungeonEncounterCreatureCatalogEntry(
                    "goblin-warrior",
                    GoblinJson,
                    CreaturePrefab()
                ),
            }
        );
        DungeonEncounterRuntimeController runtime = Track(
                new GameObject("Dungeon Encounter Runtime")
            )
            .AddComponent<DungeonEncounterRuntimeController>();
        runtime.InitializePristine(
            Document(),
            catalog,
            manager,
            new[] { player },
            new RecordingExplorationPresentation()
        );
        player.transform.position = new Vector3(2f, 0f, 2f);
        yield return null;

        DungeonEncounterMember enemy = runtime
            .GetComponentsInChildren<DungeonEncounterMember>()
            .Single();
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(player.ActionPoints, Is.EqualTo(3u));
        player.IsTakingAction = true;

        CreatureComponent enemyCreature = enemy.GetComponent<CreatureComponent>();
        enemyCreature.ApplyFinalDamage(
            enemyCreature.hp,
            RuleSource.FromSlug("test-final-action-defeat")
        );

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(player.IsInDungeonExploration, Is.False);

        player.ActionPoints -= 3;
        player.IsTakingAction = false;
        Assert.That(manager.CheckForEndOfGame(), Is.True);

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(player.IsInDungeonExploration, Is.True);
        Assert.That(
            player.ActionPoints,
            Is.Zero,
            "Exploration authority must not manufacture combat action points."
        );
        Assert.That(player.HasTurnAuthority, Is.False);
    }

    private RuntimeTestActionController CreatePlayer(CombatManager manager)
    {
        GameObject owner = Track(new GameObject("Player"));
        CreatureComponent creature = owner.AddComponent<CreatureComponent>();
        creature.name = "Player";
        creature.InitializeHealthBeforeEncounter(10, 10);
        creature.initiative = 100;
        owner.AddComponent<Conditions>();
        Team team = owner.AddComponent<Team>();
        team.Name = "Players";
        RuntimeTestActionController controller = owner.AddComponent<RuntimeTestActionController>();
        manager.AddCombatant(controller);
        return controller;
    }

    private GameObject CreaturePrefab() => CreaturePrefab(Vector3.zero);

    private GameObject CreaturePrefab(Vector3 position)
    {
        GameObject prefab = Track(new GameObject("Encounter Creature Prefab"));
        prefab.transform.position = position;
        prefab.AddComponent<RuntimeTestActionController>();
        Team team = prefab.AddComponent<Team>();
        team.Name = "Enemies";
        prefab.AddComponent<Token>();
        return prefab;
    }

    private static DungeonLevelDocument Document()
    {
        DungeonEncounterPlan plan = new(
            "encounter-1",
            1,
            DungeonEncounterThreat.Trivial,
            40,
            new[] { new DungeonCell(4, 4) },
            new[] { "goblin-warrior" }
        );
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata("runtime-test", 158, 0, 0),
            new[] { ".....", ".....", ".....", ".....", "....." },
            new[] { new DungeonRoom(1, 2, 2, 4, 4) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(0, 0),
            new[] { new DungeonCell(0, 0) },
            Array.Empty<DungeonObjectPlacement>(),
            new[] { plan }
        );
    }

    private static DungeonLevelDocument EmptyDocument() =>
        new(
            new DungeonGenerationMetadata("empty-runtime-test", 158, 0, 0),
            new[] { "...", "...", "..." },
            new[] { new DungeonRoom(1, 1, 1, 1, 1) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(0, 0),
            new[] { new DungeonCell(0, 0) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>()
        );

    private static DungeonLevelDocument PersistedDocument()
    {
        DungeonLevelDocument pristine = Document();
        DungeonRuntimeState runtimeState = new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[]
            {
                new DungeonCreatureRuntimeState(
                    "encounter-1/creature-0000",
                    "goblin-warrior",
                    "encounter-1",
                    new DungeonCell(1, 1),
                    3,
                    "persisted-survivor-state"
                ),
            }
        );
        return new DungeonLevelDocument(
            pristine.Generation,
            pristine.Rows,
            pristine.Rooms,
            pristine.Doors,
            pristine.Stairs,
            pristine.StartCell,
            pristine.SafeCells,
            pristine.Objects,
            pristine.EncounterPlans,
            runtimeState
        );
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    private static void DestroyExistingRuntime()
    {
        foreach (
            Token token in Object.FindObjectsByType<Token>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(token.gameObject);
        foreach (
            GridAPI grid in Object.FindObjectsByType<GridAPI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(grid.gameObject);
        foreach (
            GameManager gameManager in Object.FindObjectsByType<GameManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(gameManager.gameObject);
        foreach (
            CombatManagerInterface combatManager in Object.FindObjectsByType<CombatManagerInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(combatManager.gameObject);
        foreach (
            CombatLogInterface combatLog in Object.FindObjectsByType<CombatLogInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(combatLog.gameObject);
    }

    private sealed class RuntimeTestActionController : ActionController
    {
        /// <inheritdoc/>
        public override void EndTurn()
        {
            ResetEncounterTurnState();
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    private sealed class TrackingCombatManager : CombatManager
    {
        internal IReadOnlyList<ActionController> RegisteredCombatants => Combatants;
    }

    private sealed class RecordingExplorationPresentation : IDungeonExplorationPresentation
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }

        /// <inheritdoc/>
        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) => ShowCount++;

        /// <inheritdoc/>
        public void HideExploration() => HideCount++;
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
