using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Autosave;
using Game.DungeonPersistence.Repository;
using Game.KayKit;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Exercises the player-facing menu against the production reusable dungeon scene.</summary>
public sealed class DungeonProductionFlowPlayModeTests
{
    private const string MainMenuScene = "MainMenuScene";
    private const string DungeonScene = "ProceduralDungeon";
    private readonly List<string> autosaveDirectories = new();
    private UnityEngine.Random.State randomState;
    private VisualElement menuRoot;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        randomState = UnityEngine.Random.state;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = 1f;
        if (SceneManager.GetActiveScene().name != MainMenuScene)
            yield return SceneManager.LoadSceneAsync(MainMenuScene, LoadSceneMode.Single);

        GameObject transitionManager = GameObject.Find("SceneTransitionManager");
        if (transitionManager != null)
        {
            Object.Destroy(transitionManager);
            yield return null;
        }

        foreach (string directory in autosaveDirectories)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        autosaveDirectories.Clear();
        UnityEngine.Random.state = randomState;
    }

    /// <summary>Returns from a launched procedural dungeon to the production main menu.</summary>
    [UnityTest]
    public IEnumerator DungeonHudReturnsToMainMenu()
    {
        string directory = TrackDirectory("return-to-menu");
        yield return LaunchNewRun(directory, "154");

        Button mainMenuButton = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Button>("DungeonMainMenuButton"))
            .FirstOrDefault(button => button != null);
        Assert.That(mainMenuButton, Is.Not.Null);
        Assert.That(mainMenuButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

        PushButton(mainMenuButton);
        yield return WaitForMainMenu();

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(MainMenuScene));
        Assert.That(SceneTransitionManager.IsTransitioning, Is.False);
    }

    /// <summary>
    /// Confirms a stair during exploration-action cleanup and commits it after the runtime settles.
    /// </summary>
    [UnityTest]
    public IEnumerator DungeonHudConfirmTraversesRequestedStair()
    {
        string directory = TrackDirectory("hud-stair-confirm");
        yield return LaunchNewRun(directory, "807239348");

        DungeonRunController controller = RequireController();
        DungeonStairMarker down = RequireStair(DungeonStairKind.Down);
        int startingDepth = controller.CurrentDepth;
        Assert.That(controller.StartingSeed, Is.EqualTo(807239348));
        Assert.That(down.Cell, Is.EqualTo(new DungeonCell(13, 3)));
        Assert.That(down.ArrivalCell, Is.EqualTo(new DungeonCell(13, 2)));
        IReadOnlyDictionary<string, ActionController> partyByName = ProductionParty()
            .ToDictionary(member => member.name, member => member, StringComparer.Ordinal);
        partyByName["Lena"].transform.position = new Vector3(13f, 0f, 2f);
        partyByName["Torgrim"].transform.position = new Vector3(13f, 0f, 1f);
        partyByName["Lena"].IsTakingAction = true;
        Physics.SyncTransforms();
        yield return null;

        controller.RequestUseStair(down);
        yield return null;
        Button confirm = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Button>("DungeonStairConfirmButton"))
            .FirstOrDefault(button => button != null);
        Assert.That(confirm, Is.Not.Null);
        VisualElement pointerTarget = confirm.panel.Pick(confirm.worldBound.center);
        Assert.That(
            pointerTarget == confirm || confirm.Contains(pointerTarget),
            Is.True,
            $"The stair Confirm button is covered by '{pointerTarget?.name}'."
        );

        ClickButtonWithPointer(confirm);
        yield return null;

        Assert.That(controller.CurrentDepth, Is.EqualTo(startingDepth));
        Assert.That(
            controller.LastDiagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.RuntimeBusy)
        );
        DungeonSaveResult<DungeonRunSave> busySave = new FileSystemDungeonSaveRepository(
            directory
        ).Load();
        Assert.That(
            busySave.IsSuccess,
            Is.True,
            string.Join(" ", busySave.Diagnostics.Select(item => item.Message))
        );
        Assert.That(busySave.Value.Manifest.CurrentDepth, Is.Zero);
        Assert.That(busySave.Value.HasFloor(1), Is.False);

        partyByName["Lena"].IsTakingAction = false;
        yield return null;

        Assert.That(controller.CurrentDepth, Is.EqualTo(startingDepth + 1));
        DungeonSaveResult<DungeonRunSave> committed = new FileSystemDungeonSaveRepository(
            directory
        ).Load();
        Assert.That(
            committed.IsSuccess,
            Is.True,
            string.Join(" ", committed.Diagnostics.Select(item => item.Message))
        );
        Assert.That(committed.Value.Manifest.CurrentDepth, Is.EqualTo(1));
        Assert.That(committed.Value.GetFloor(1), Is.Not.Null);
    }

    /// <summary>
    /// Walks to a generated stair through the production exploration controls and confirms the
    /// grid-selected endpoint through the live HUD.
    /// </summary>
    [UnityTest]
    public IEnumerator ExplorationStrideAndHudConfirmTraversesClickedStair()
    {
        string directory = TrackDirectory("exploration-stair-confirm");
        yield return LaunchNewRun(directory, "362");

        DungeonRunController controller = RequireController();
        DungeonStairMarker down = RequireStair(DungeonStairKind.Down);
        ActionController leader = ProductionParty().Single(member => member.IsInDungeonExploration);
        GridBase grid = RequireMap().GetComponent<GridBase>();
        Assert.That(grid, Is.Not.Null, "The production dungeon map has no GridBase.");
        DungeonEncounterRuntimeController runtime =
            Object.FindFirstObjectByType<DungeonEncounterRuntimeController>();
        Assert.That(runtime, Is.Not.Null, "The production dungeon encounter runtime is missing.");
        ResolveGeneratedEncountersForTraversal(runtime.Lifecycle);
        Assert.That(controller.StartingSeed, Is.EqualTo(362));

        yield return WalkLeaderWithExplorationStride(
            leader,
            grid,
            new Vector3Int(down.ArrivalCell.X, 0, down.ArrivalCell.Z)
        );

        Assert.That(
            Vector3Int.RoundToInt(leader.transform.position),
            Is.EqualTo(new Vector3Int(down.ArrivalCell.X, 0, down.ArrivalCell.Z))
        );
        RaiseGridCellClick(grid.GetComponent<GridInput>(), down.Cell);
        yield return null;

        Button confirm = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Button>("DungeonStairConfirmButton"))
            .FirstOrDefault(button => button != null);
        Assert.That(confirm, Is.Not.Null);
        ClickButtonWithPointer(confirm);
        yield return null;

        Assert.That(controller.CurrentDepth, Is.EqualTo(1));
        DungeonSaveResult<DungeonRunSave> committed = new FileSystemDungeonSaveRepository(
            directory
        ).Load();
        Assert.That(
            committed.IsSuccess,
            Is.True,
            string.Join(" ", committed.Diagnostics.Select(item => item.Message))
        );
        Assert.That(committed.Value.Manifest.CurrentDepth, Is.EqualTo(1));
        Assert.That(committed.Value.GetFloor(1), Is.Not.Null);
    }

    /// <summary>Restores dungeon controls when another scene transition rejects the return request.</summary>
    [UnityTest]
    public IEnumerator DungeonHudRecoversWhenReturnTransitionIsRejected()
    {
        string directory = TrackDirectory("rejected-return");
        yield return LaunchNewRun(directory, "156");
        yield return WaitForTransitionIdle();

        Button mainMenuButton = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Button>("DungeonMainMenuButton"))
            .FirstOrDefault(button => button != null);
        Assert.That(mainMenuButton, Is.Not.Null);
        Assert.That(SceneTransitionManager.FadeAndLoad(MainMenuScene, duration: 0.25f), Is.True);

        LogAssert.Expect(
            LogType.Warning,
            "Scene transition to 'MainMenuScene' was rejected because another scene transition is already in progress."
        );
        PushButton(mainMenuButton);
        yield return null;

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(DungeonScene));
        Assert.That(SceneTransitionManager.IsTransitioning, Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(mainMenuButton.enabledSelf, Is.True);

        yield return WaitForMainMenu();
    }

    /// <summary>Releases the blocking overlay when Unity rejects a scene load request.</summary>
    [UnityTest]
    public IEnumerator FailedSceneLoadReleasesTransitionOverlay()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        const string missingScene = "MissingSceneForTransitionRecoveryTest";
        LogAssert.Expect(LogType.Error, new Regex($"^Scene '{missingScene}' couldn't be loaded"));
        LogAssert.Expect(
            LogType.Error,
            new Regex($"^Scene transition to '{missingScene}' failed to start:")
        );

        Assert.That(SceneTransitionManager.FadeAndLoad(missingScene, duration: 0f), Is.True);
        yield return WaitForTransitionIdle();

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(activeScene));
        Assert.That(SceneTransitionManager.FadeAndLoad(MainMenuScene, duration: 0f), Is.True);
        yield return WaitForTransitionIdle();
    }

    /// <summary>Shows a player-facing defeat message with a working menu recovery control.</summary>
    [UnityTest]
    public IEnumerator DungeonDefeatPresentsMainMenuRecovery()
    {
        string directory = TrackDirectory("defeat-presentation");
        yield return LaunchNewRun(directory, "157");
        yield return WaitForTransitionIdle();

        OnCombatOutcome.Invoke(false);
        yield return null;

        Label status = RequireDungeonStatus();
        Assert.That(status.text, Does.Contain("defeated"));
        Assert.That(status.ClassListContains("dungeon-run-status--error"), Is.True);
        Button mainMenuButton = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Button>("DungeonMainMenuButton"))
            .FirstOrDefault(button => button != null);
        Assert.That(mainMenuButton, Is.Not.Null);
        Assert.That(mainMenuButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        Assert.That(mainMenuButton.enabledSelf, Is.True);
    }

    /// <summary>Restores the player's prior speed when the required return checkpoint fails.</summary>
    [UnityTest]
    public IEnumerator FailedReturnCheckpointRestoresPriorTimeScale()
    {
        string directory = TrackDirectory("failed-return-checkpoint");
        yield return LaunchNewRun(directory, "158");
        yield return WaitForTransitionIdle();

        DungeonEncounterRuntimeController runtime =
            Object.FindFirstObjectByType<DungeonEncounterRuntimeController>();
        Assert.That(runtime, Is.Not.Null);
        Object.Destroy(runtime);
        yield return null;

        Button mainMenuButton = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Button>("DungeonMainMenuButton"))
            .FirstOrDefault(button => button != null);
        Assert.That(mainMenuButton, Is.Not.Null);
        Time.timeScale = 3f;

        PushButton(mainMenuButton);
        yield return null;

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(DungeonScene));
        Assert.That(SceneTransitionManager.IsTransitioning, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(3f));
        Assert.That(mainMenuButton.enabledSelf, Is.True);
        Assert.That(RequireDungeonStatus().text, Does.Contain("could not be saved"));
    }

    /// <summary>Waits for an active action and checkpoints before leaving the dungeon.</summary>
    [UnityTest]
    public IEnumerator DungeonReturnWaitsForActionAndCheckpointsBeforeLeaving()
    {
        string directory = TrackDirectory("return-after-action");
        yield return LaunchNewRun(directory, "155");
        yield return WaitForTransitionIdle();

        ActionController actor = ProductionParty()[0];
        actor.IsTakingAction = true;
        Button mainMenuButton = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Button>("DungeonMainMenuButton"))
            .FirstOrDefault(button => button != null);
        Assert.That(mainMenuButton, Is.Not.Null);

        PushButton(mainMenuButton);
        yield return null;

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(DungeonScene));
        Assert.That(SceneTransitionManager.IsTransitioning, Is.False);
        Assert.That(mainMenuButton.enabledSelf, Is.False);

        FileSystemDungeonSaveRepository repository = new(directory);
        File.Delete(repository.AutosavePath);
        Assert.That(File.Exists(repository.AutosavePath), Is.False);

        actor.IsTakingAction = false;
        yield return null;

        Assert.That(SceneTransitionManager.IsTransitioning, Is.True);
        Assert.That(Time.timeScale, Is.Zero);

        yield return WaitForMainMenu();

        Assert.That(Time.timeScale, Is.EqualTo(1f));
        DungeonSaveResult<DungeonRunSave> checkpoint = repository.Load();
        Assert.That(
            checkpoint.IsSuccess,
            Is.True,
            string.Join(" ", checkpoint.Diagnostics.Select(item => item.Message))
        );
    }

    /// <summary>
    /// Launches two separate runs from the same displayed signed seed and compares their complete
    /// initial floor documents.
    /// </summary>
    [UnityTest]
    public IEnumerator DisplayedSeedReproducesFreshProductionRun()
    {
        const string suppliedSeed = "9223372036854775807";
        string firstDirectory = TrackDirectory("reproduce-a");
        yield return LaunchNewRun(firstDirectory, suppliedSeed);

        DungeonRunController firstController = RequireController();
        DungeonLevelDocument firstFloor = RequireMap().ValidateSource().JsonMap.LevelDocument;
        string firstJson = DungeonLevelJsonSerializer.Serialize(firstFloor);
        Label firstStatus = RequireDungeonStatus();
        Assert.That(firstController.StartingSeed, Is.EqualTo(int.MinValue));
        Assert.That(firstController.CurrentDepth, Is.Zero);
        Assert.That(firstStatus.text, Does.Contain("Seed -2147483648"));
        Assert.That(firstStatus.text, Does.Contain("Depth 0"));
        Assert.That(ProductionParty(), Has.Length.EqualTo(2));
        Assert.That(ProductionParty().All(member => member.gameObject.activeInHierarchy), Is.True);

        DungeonSaveResult<DungeonRunSave> firstSave = new FileSystemDungeonSaveRepository(
            firstDirectory
        ).Load();
        Assert.That(
            firstSave.IsSuccess,
            Is.True,
            string.Join(" ", firstSave.Diagnostics.Select(item => item.Message))
        );
        Assert.That(firstSave.Value.Manifest.StartingSeed, Is.EqualTo(int.MinValue));

        string secondDirectory = TrackDirectory("reproduce-b");
        yield return LaunchNewRun(secondDirectory, suppliedSeed);

        DungeonRunController secondController = RequireController();
        DungeonLevelDocument secondFloor = RequireMap().ValidateSource().JsonMap.LevelDocument;
        Label secondStatus = RequireDungeonStatus();
        Assert.That(secondController.StartingSeed, Is.EqualTo(int.MinValue));
        Assert.That(secondStatus.text, Does.Contain("Seed -2147483648"));
        Assert.That(
            DungeonLevelJsonSerializer.Serialize(secondFloor),
            Is.EqualTo(firstJson),
            "A displayed normalized seed must reproduce topology, decoration, stairs, and encounter plans in a separate fresh run."
        );
    }

    /// <summary>
    /// Descends, mutates the floor, materializes an unfinished encounter, and continues that exact
    /// run through the production menu.
    /// </summary>
    [UnityTest]
    public IEnumerator ContinueRestoresDepthPartyFloorAndUnfinishedEncounter()
    {
        const string suppliedSeed = "7345621";
        const int expectedSeed = 7345621;
        string directory = TrackDirectory("continue");
        yield return LaunchNewRun(directory, suppliedSeed);

        DungeonRunController controller = RequireController();
        ActionController[] party = ProductionParty();
        DungeonStairMarker down = RequireStair(DungeonStairKind.Down);
        PlacePartyAtStair(down, party);
        DungeonTravelResult descent = controller.TryUseStair(down, confirmed: true);
        Assert.That(
            descent.IsSuccess,
            Is.True,
            string.Join(" ", descent.Diagnostics.Select(item => item.Message))
        );
        Assert.That(controller.CurrentDepth, Is.EqualTo(1));
        yield return null;

        Map map = RequireMap();
        DungeonLevelDocument floor = map.ValidateSource().JsonMap.LevelDocument;
        party = ProductionParty();
        DungeonEncounterRuntimeController runtime =
            Object.FindFirstObjectByType<DungeonEncounterRuntimeController>();
        Assert.That(runtime, Is.Not.Null);

        DungeonDoorController[] closedDoors = Object
            .FindObjectsByType<DungeonDoorController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            )
            .OrderBy(item => item.StableId, StringComparer.Ordinal)
            .Where(item => !item.IsOpen)
            .ToArray();
        Assert.That(
            closedDoors,
            Is.Not.Empty,
            $"Generated depth 1 documented {floor.Doors.Count} doors but exposed no closed door controller."
        );
        DungeonDoorController door = closedDoors[0];
        PlaceActorAdjacentToDoor(party[0], door, floor);
        Assert.That(runtime.TryOpenDoor(door.Cell), Is.True);
        string openedDoorId = door.StableId;

        DungeonEncounterPlan[] encounters = floor
            .EncounterPlans.OrderBy(plan => plan.Id, StringComparer.Ordinal)
            .ToArray();
        Assert.That(encounters, Is.Not.Empty, "Generated depth 1 has no encounter plans.");
        DungeonEncounterPlan encounter = encounters[0];
        DungeonRoom[] encounterRooms = floor
            .Rooms.Where(room => room.Id == encounter.RoomId)
            .ToArray();
        Assert.That(
            encounterRooms,
            Has.Length.EqualTo(1),
            $"Encounter '{encounter.Id}' does not identify exactly one generated room."
        );
        DungeonRoom encounterRoom = encounterRooms[0];
        SetPartyCells(party, AvailablePartyCells(floor, encounterRoom, party.Length));
        InvokeRuntimeUpdate(runtime);
        Assert.That(
            runtime.Lifecycle.GetRoomEncounter(encounter.RoomId).State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );

        DungeonRoom[] safeRooms = floor
            .Rooms.Where(room => floor.EncounterPlans.All(plan => plan.RoomId != room.Id))
            .ToArray();
        Assert.That(
            safeRooms,
            Has.Length.EqualTo(1),
            "A deeper generated floor must have exactly one encounter-free arrival room."
        );
        DungeonRoom safeRoom = safeRooms[0];
        Assert.That(
            floor.EncounterPlans.Any(plan => plan.RoomId == safeRoom.Id),
            Is.False,
            "The depth arrival room must remain encounter-free."
        );
        SetPartyCells(party, AvailablePartyCells(floor, safeRoom, party.Length));

        DungeonAutosaveCoordinator autosave =
            Object.FindFirstObjectByType<DungeonAutosaveCoordinator>();
        Assert.That(autosave, Is.Not.Null);
        DungeonSaveResult<DungeonRunSave> checkpoint = autosave.CheckpointCurrentFloor();
        Assert.That(
            checkpoint.IsSuccess,
            Is.True,
            string.Join(" ", checkpoint.Diagnostics.Select(item => item.Message))
        );
        DungeonLevelDocument checkpointedFloor = checkpoint.Value.GetFloor(1);
        Assert.That(checkpointedFloor.RuntimeState.OpenDoorIds, Does.Contain(openedDoorId));
        Assert.That(
            checkpointedFloor.RuntimeState.Creatures.Count(creature =>
                string.Equals(creature.EncounterId, encounter.Id, StringComparison.Ordinal)
            ),
            Is.EqualTo(encounter.CreatureIds.Count),
            "The unfinished encounter must be materialized in the autosave."
        );

        Dictionary<string, DungeonCell> expectedPartyCells =
            checkpoint.Value.Manifest.Party.ToDictionary(
                member => member.RosterSlotId,
                member => new DungeonCell(member.CellX, member.CellZ),
                StringComparer.Ordinal
            );

        yield return LoadMenu(directory, entropy: 1L);
        Button continueButton = menuRoot.Q<Button>("ContinueButton");
        Label menuStatus = menuRoot.Q<Label>("MenuStatusLabel");
        Assert.That(continueButton.enabledSelf, Is.True);
        Assert.That(menuStatus.text, Does.Contain("depth 1"));
        Assert.That(menuStatus.text, Does.Contain("seed 7345621"));

        DungeonSaveResult<DungeonRunSave> committed = new FileSystemDungeonSaveRepository(
            directory
        ).Load();
        Assert.That(
            committed.IsSuccess,
            Is.True,
            string.Join(" ", committed.Diagnostics.Select(item => item.Message))
        );
        string committedFloorJson = DungeonLevelJsonSerializer.Serialize(
            committed.Value.GetFloor(1)
        );

        PushButton(continueButton);
        yield return WaitForDungeonRuntime();

        DungeonRunController restoredController = RequireController();
        Assert.That(restoredController.StartingSeed, Is.EqualTo(expectedSeed));
        Assert.That(restoredController.CurrentDepth, Is.EqualTo(1));
        Assert.That(RequireDungeonStatus().text, Does.Contain("Depth 1"));
        Assert.That(
            DungeonLevelJsonSerializer.Serialize(
                RequireMap().ValidateSource().JsonMap.LevelDocument
            ),
            Is.EqualTo(committedFloorJson),
            "Continue must populate the exact committed floor JSON rather than regenerating it."
        );

        foreach (ActionController member in ProductionParty())
        {
            DungeonPartyMemberIdentity identity = member.GetComponent<DungeonPartyMemberIdentity>();
            Vector3Int position = Vector3Int.RoundToInt(member.transform.position);
            Assert.That(
                new DungeonCell(position.x, position.z),
                Is.EqualTo(expectedPartyCells[identity.RosterSlotId]),
                identity.RosterSlotId
            );
        }

        DungeonDoorController[] restoredDoors = Object
            .FindObjectsByType<DungeonDoorController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            )
            .Where(item => string.Equals(item.StableId, openedDoorId, StringComparison.Ordinal))
            .ToArray();
        Assert.That(
            restoredDoors,
            Has.Length.EqualTo(1),
            $"Continue did not restore door '{openedDoorId}'."
        );
        DungeonDoorController restoredDoor = restoredDoors[0];
        Assert.That(restoredDoor.IsOpen, Is.True);

        DungeonEncounterRuntimeController restoredRuntime =
            Object.FindFirstObjectByType<DungeonEncounterRuntimeController>();
        Assert.That(
            restoredRuntime.Lifecycle.GetRoomEncounter(encounter.RoomId).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended),
            "A restored unfinished fight must return in suspended exploration until re-entered."
        );
        Assert.That(
            restoredRuntime
                .GetComponentsInChildren<DungeonEncounterMember>(includeInactive: true)
                .Count(member =>
                    string.Equals(member.EncounterId, encounter.Id, StringComparison.Ordinal)
                ),
            Is.EqualTo(encounter.CreatureIds.Count)
        );
    }

    private IEnumerator LaunchNewRun(string directory, string seedText)
    {
        yield return LoadMenu(directory, entropy: 17L);
        menuRoot.Q<TextField>("SeedField").value = seedText;
        PushButton(menuRoot.Q<Button>("NewRunButton"));
        yield return WaitForDungeonRuntime();
    }

    private IEnumerator LoadMenu(string directory, long entropy)
    {
        yield return SceneManager.LoadSceneAsync(MainMenuScene, LoadSceneMode.Single);
        MainMenuControl menu = Object.FindFirstObjectByType<MainMenuControl>();
        Assert.That(menu, Is.Not.Null);
        UIDocument document = menu.GetComponent<UIDocument>();
        Assert.That(document, Is.Not.Null);
        menuRoot = document.rootVisualElement;
        menu.ConfigureForTests(
            new DungeonRunMenuService(directory, () => entropy),
            request => SceneTransitionManager.FadeAndLoadDungeon(request, duration: 0f)
        );
        yield return null;
    }

    private static IEnumerator WaitForDungeonRuntime()
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < deadline)
        {
            DungeonRunController controller = Object.FindFirstObjectByType<DungeonRunController>();
            if (
                SceneManager.GetActiveScene().name == DungeonScene
                && controller != null
                && controller.IsInitialized
            )
            {
                yield break;
            }
            yield return null;
        }

        Assert.Fail("The production procedural dungeon did not initialize within 30 seconds.");
    }

    private static IEnumerator WaitForMainMenu()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (
                SceneManager.GetActiveScene().name == MainMenuScene
                && !SceneTransitionManager.IsTransitioning
            )
            {
                yield break;
            }
            yield return null;
        }

        Assert.Fail("The production main menu did not finish loading within 10 seconds.");
    }

    private string TrackDirectory(string suffix)
    {
        string directory = Path.Combine(
            Application.temporaryCachePath,
            "issue-154-" + suffix + "-" + Guid.NewGuid().ToString("N")
        );
        autosaveDirectories.Add(directory);
        return directory;
    }

    private static DungeonRunController RequireController()
    {
        DungeonRunController controller = Object.FindFirstObjectByType<DungeonRunController>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.IsInitialized, Is.True);
        return controller;
    }

    private static Map RequireMap()
    {
        Map map = Object.FindFirstObjectByType<Map>();
        Assert.That(map, Is.Not.Null);
        return map;
    }

    private static Label RequireDungeonStatus()
    {
        Label status = Object
            .FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Select(document => document.rootVisualElement.Q<Label>("DungeonRunStatusLabel"))
            .FirstOrDefault(label => label != null);
        Assert.That(status, Is.Not.Null);
        return status;
    }

    private static ActionController[] ProductionParty() =>
        Object
            .FindObjectsByType<ActionController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID
            )
            .Where(member =>
                string.Equals(
                    member.GetComponent<Team>()?.Name,
                    "Players",
                    StringComparison.OrdinalIgnoreCase
                )
                && member.GetComponent<DungeonPartyMemberIdentity>() != null
            )
            .OrderBy(
                member => member.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                StringComparer.Ordinal
            )
            .ToArray();

    private static DungeonStairMarker RequireStair(DungeonStairKind kind)
    {
        DungeonStairMarker[] matches = Object
            .FindObjectsByType<DungeonStairMarker>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            )
            .Where(stair => stair.Kind == kind)
            .ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected one active {kind} stair.");
        return matches[0];
    }

    private static void PlacePartyAtStair(DungeonStairMarker stair, ActionController[] party)
    {
        DungeonLevelDocument document = RequireMap().ValidateSource().JsonMap.LevelDocument;
        DungeonStair[] sources = document
            .Stairs.Where(candidate =>
                string.Equals(candidate.Id, stair.StableId, StringComparison.Ordinal)
            )
            .ToArray();
        Assert.That(
            sources,
            Has.Length.EqualTo(1),
            $"Active stair '{stair.StableId}' is absent from the floor document."
        );
        DungeonStair source = sources[0];
        DungeonCell[] cells = DungeonStairInteractionRegion
            .SelectCells(document, source, BlockedStairCells(document), party.Length)
            .ToArray();
        Assert.That(cells, Has.Length.GreaterThanOrEqualTo(party.Length));
        SetPartyCells(party, cells);
    }

    private static IEnumerable<DungeonCell> BlockedStairCells(DungeonLevelDocument document)
    {
        foreach (DungeonObjectPlacement placement in document.Objects)
            yield return placement.Cell;
        foreach (DungeonEncounterPlan plan in document.EncounterPlans)
        foreach (DungeonCell spawn in plan.SpawnCells)
            yield return spawn;
    }

    private static void PlaceActorAdjacentToDoor(
        ActionController actor,
        DungeonDoorController door,
        DungeonLevelDocument document
    )
    {
        DungeonCell[] candidates =
        {
            new(door.Cell.X + 1, door.Cell.Z),
            new(door.Cell.X - 1, door.Cell.Z),
            new(door.Cell.X, door.Cell.Z + 1),
            new(door.Cell.X, door.Cell.Z - 1),
        };
        DungeonCell[] walkable = candidates.Where(cell => IsOpenFloor(document, cell)).ToArray();
        Assert.That(
            walkable,
            Is.Not.Empty,
            $"Door '{door.StableId}' has no cardinally adjacent open floor cell."
        );
        DungeonCell adjacent = walkable[0];
        actor.transform.position = new Vector3(adjacent.X, actor.transform.position.y, adjacent.Z);
        Physics.SyncTransforms();
    }

    private static DungeonCell[] AvailablePartyCells(
        DungeonLevelDocument document,
        DungeonRoom room,
        int count
    )
    {
        HashSet<DungeonCell> blocked = new(
            document
                .Objects.Select(item => item.Cell)
                .Concat(document.Doors.Select(item => item.Cell))
                .Concat(document.Stairs.SelectMany(item => new[] { item.Cell, item.ArrivalCell }))
                .Concat(document.EncounterPlans.SelectMany(item => item.SpawnCells))
        );
        DungeonCell[] cells = Enumerable
            .Range(room.MinimumZ, room.MaximumZ - room.MinimumZ + 1)
            .SelectMany(z =>
                Enumerable
                    .Range(room.MinimumX, room.MaximumX - room.MinimumX + 1)
                    .Select(x => new DungeonCell(x, z))
            )
            .Where(cell => IsOpenFloor(document, cell) && !blocked.Contains(cell))
            .OrderBy(cell => cell.Z)
            .ThenBy(cell => cell.X)
            .Take(count)
            .ToArray();
        Assert.That(cells, Has.Length.EqualTo(count), $"Room {room.Id} lacks party cells.");
        return cells;
    }

    private static bool IsOpenFloor(DungeonLevelDocument document, DungeonCell cell)
    {
        if (cell.X < 0 || cell.X >= document.Width || cell.Z < 0 || cell.Z >= document.Height)
        {
            return false;
        }
        return document.Rows[document.Height - 1 - cell.Z][cell.X] == '.';
    }

    private static bool Contains(DungeonRoom room, DungeonCell cell) =>
        cell.X >= room.MinimumX
        && cell.X <= room.MaximumX
        && cell.Z >= room.MinimumZ
        && cell.Z <= room.MaximumZ;

    private static void SetPartyCells(
        IReadOnlyList<ActionController> party,
        IReadOnlyList<DungeonCell> cells
    )
    {
        for (int index = 0; index < party.Count; index++)
        {
            party[index].transform.position = new Vector3(
                cells[index].X,
                party[index].transform.position.y,
                cells[index].Z
            );
        }
        Physics.SyncTransforms();
    }

    private static void InvokeRuntimeUpdate(DungeonEncounterRuntimeController runtime)
    {
        MethodInfo update = typeof(DungeonEncounterRuntimeController).GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(update, Is.Not.Null);
        update.Invoke(runtime, null);
    }

    private static IEnumerator WalkLeaderWithExplorationStride(
        ActionController leader,
        GridBase grid,
        Vector3Int destination
    )
    {
        int remainingStrides = 100;
        HashSet<Vector3Int> visited = new();
        while (
            Vector3Int.RoundToInt(leader.transform.position) != destination
            && remainingStrides-- > 0
        )
        {
            Vector3Int origin = Vector3Int.RoundToInt(leader.transform.position);
            visited.Add(origin);
            List<PathNode> route = grid.GetPathfinder()
                .Pathfind(leader.gameObject, origin, destination);
            if (route == null || route.Count == 0)
            {
                DungeonDoorController door = FindReachableClosedDoor(
                    leader.gameObject,
                    grid,
                    origin,
                    destination,
                    out route
                );
                Assert.That(
                    door,
                    Is.Not.Null,
                    $"No production path or reachable closed door leads from {origin} to {destination}."
                );
                if (route.Count == 1)
                {
                    Assert.That(
                        ProductionParty()
                            .Any(member =>
                            {
                                Vector3Int cell = Vector3Int.RoundToInt(member.transform.position);
                                return Math.Abs(cell.x - door.Cell.X)
                                        + Math.Abs(cell.z - door.Cell.Z)
                                    == 1;
                            }),
                        Is.True,
                        $"No living party member is cardinally adjacent to door '{door.StableId}'."
                    );
                    RaiseGridCellClick(grid.GetComponent<GridInput>(), door.Cell);
                    yield return null;
                    Assert.That(door.IsOpen, Is.True, $"Door '{door.StableId}' did not open.");
                    continue;
                }
            }

            Vector3Int routeDestination = route[^1].Location;
            Tile[,] tiles = grid.GetTiles();
            Vector3Int strideDestination = route
                .Skip(1)
                .Take(5)
                .TakeWhile(node => tiles[node.Location.x, node.Location.z].Occupants.Count == 0)
                .Select(node => node.Location)
                .LastOrDefault();
            if (strideDestination == default)
            {
                strideDestination = new[]
                {
                    Vector3Int.right,
                    Vector3Int.left,
                    new Vector3Int(0, 0, 1),
                    new Vector3Int(0, 0, -1),
                }
                    .Select(offset => origin + offset)
                    .Where(cell =>
                        cell.x >= 0
                        && cell.z >= 0
                        && cell.x < tiles.GetLength(0)
                        && cell.z < tiles.GetLength(1)
                        && tiles[cell.x, cell.z] != null
                        && tiles[cell.x, cell.z].Occupants.Count == 0
                    )
                    .Select(cell => new
                    {
                        Cell = cell,
                        Route = grid.GetPathfinder()
                            .Pathfind(leader.gameObject, cell, routeDestination),
                    })
                    .Where(candidate => candidate.Route != null && candidate.Route.Count > 0)
                    .OrderBy(candidate => visited.Contains(candidate.Cell))
                    .ThenBy(candidate => candidate.Route.Count)
                    .ThenBy(candidate => candidate.Cell.z)
                    .ThenBy(candidate => candidate.Cell.x)
                    .Select(candidate => candidate.Cell)
                    .First();
            }
            yield return ExecuteExplorationStrideThroughHud(leader, grid, strideDestination);
        }

        Assert.That(
            remainingStrides,
            Is.GreaterThan(0),
            $"Exploration Stride did not reach {destination} within the deterministic bound."
        );
    }

    private static void ResolveGeneratedEncountersForTraversal(
        DungeonEncounterStateMachine lifecycle
    )
    {
        Assert.That(lifecycle, Is.Not.Null);
        foreach (DungeonEncounterGroupView encounter in lifecycle.Encounters.ToArray())
        {
            DungeonEncounterGroupView current = lifecycle.GetEncounter(encounter.Plan.Id);
            if (current.State == DungeonEncounterGroupState.Dormant)
                lifecycle.EnterRoom(current.Plan.RoomId);
            foreach (
                DungeonEncounterCreatureView creature in lifecycle
                    .GetEncounter(encounter.Plan.Id)
                    .LivingCreatures.ToArray()
            )
                lifecycle.MarkCreatureDefeated(creature.InstanceId);
        }
        Assert.That(
            lifecycle.Encounters.All(encounter =>
                encounter.State == DungeonEncounterGroupState.Cleared
            )
        );
    }

    private static DungeonDoorController FindReachableClosedDoor(
        GameObject leader,
        GridBase grid,
        Vector3Int origin,
        Vector3Int destination,
        out List<PathNode> route
    )
    {
        DungeonDoorController selected = null;
        route = null;
        float selectedScore = float.PositiveInfinity;
        Vector3Int[] directions =
        {
            Vector3Int.right,
            Vector3Int.left,
            new(0, 0, 1),
            new(0, 0, -1),
        };
        foreach (
            DungeonDoorController door in Object
                .FindObjectsByType<DungeonDoorController>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None
                )
                .Where(candidate => !candidate.IsOpen)
                .OrderBy(candidate => candidate.StableId, StringComparer.Ordinal)
        )
        {
            foreach (Vector3Int direction in directions)
            {
                Vector3Int adjacent = new Vector3Int(door.Cell.X, 0, door.Cell.Z) + direction;
                List<PathNode> candidate = grid.GetPathfinder().Pathfind(leader, origin, adjacent);
                if (candidate == null || candidate.Count == 0)
                    continue;
                float targetDistance =
                    Math.Abs(door.Cell.X - destination.x) + Math.Abs(door.Cell.Z - destination.z);
                float score = candidate[^1].Dist + targetDistance;
                if (score >= selectedScore)
                    continue;
                selected = door;
                route = candidate;
                selectedScore = score;
            }
        }
        return selected;
    }

    private static IEnumerator ExecuteExplorationStrideThroughHud(
        ActionController leader,
        GridBase grid,
        Vector3Int destination
    )
    {
        Button stride = null;
        float buttonDeadline = Time.realtimeSinceStartup + 5f;
        while (stride == null && Time.realtimeSinceStartup < buttonDeadline)
        {
            stride = Object
                .FindObjectsByType<UIDocument>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None
                )
                .Select(document => document.rootVisualElement.Q<Button>("StrideButton"))
                .FirstOrDefault(button => button != null && button.enabledSelf);
            if (stride == null)
                yield return null;
        }
        Assert.That(stride, Is.Not.Null, "The production exploration Stride button was not ready.");
        PushButton(stride);

        float selectionDeadline = Time.realtimeSinceStartup + 5f;
        while (
            grid.Fsm.CurrentState is not StateStride
            && Time.realtimeSinceStartup < selectionDeadline
        )
        {
            yield return null;
        }
        Assert.That(grid.Fsm.CurrentState, Is.TypeOf<StateStride>());

        OnHover.Invoke(new List<Vector3Int> { destination });
        grid.Fsm.CurrentState.Leftclick();

        float movementDeadline = Time.realtimeSinceStartup + 10f;
        while (leader.IsTakingAction && Time.realtimeSinceStartup < movementDeadline)
            yield return null;

        Assert.That(
            leader.IsTakingAction,
            Is.False,
            $"The production exploration Stride from "
                + $"{Vector3Int.RoundToInt(leader.transform.position)} toward {destination} timed out."
        );
        DungeonEncounterRuntimeController runtime =
            Object.FindFirstObjectByType<DungeonEncounterRuntimeController>();
        Assert.That(runtime, Is.Not.Null, "The production dungeon encounter runtime is missing.");
        while (runtime.HasActionInProgress && Time.realtimeSinceStartup < movementDeadline)
            yield return null;
        Assert.That(
            runtime.HasActionInProgress,
            Is.False,
            "The production exploration party did not finish following the leader."
        );
        Assert.That(
            Vector3Int.RoundToInt(leader.transform.position),
            Is.EqualTo(destination),
            "The production exploration Stride did not commit its selected destination."
        );
    }

    private static void RaiseGridCellClick(GridInput input, DungeonCell cell)
    {
        Assert.That(input, Is.Not.Null);
        FieldInfo clickedField = typeof(GridInput).GetField(
            "CellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(clickedField, Is.Not.Null);
        Action<Vector3Int> clicked = (Action<Vector3Int>)clickedField.GetValue(input);
        clicked(new Vector3Int(cell.X, 0, cell.Z));
    }

    private static void PushButton(Button button)
    {
        Assert.That(button, Is.Not.Null);
        using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
        submit.target = button;
        button.SendEvent(submit);
    }

    private static void ClickButtonWithPointer(Button button)
    {
        Assert.That(button, Is.Not.Null);
        Vector2 position = button.worldBound.center;
        Event pointerDownSource = new()
        {
            type = EventType.MouseDown,
            mousePosition = position,
            button = 0,
            clickCount = 1,
        };
        using (PointerDownEvent pointerDown = PointerDownEvent.GetPooled(pointerDownSource))
        {
            pointerDown.target = button;
            button.SendEvent(pointerDown);
        }

        Event pointerUpSource = new()
        {
            type = EventType.MouseUp,
            mousePosition = position,
            button = 0,
            clickCount = 1,
        };
        using PointerUpEvent pointerUp = PointerUpEvent.GetPooled(pointerUpSource);
        pointerUp.target = button;
        button.SendEvent(pointerUp);
    }

    private static IEnumerator WaitForTransitionIdle()
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline && SceneTransitionManager.IsTransitioning)
            yield return null;

        Assert.That(SceneTransitionManager.IsTransitioning, Is.False);
    }
}
