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
using Game.KayKit;
using GridPublic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Exercises three generated depths through the production reusable-scene flow.</summary>
public sealed class DungeonRunTraversalPlayModeTests
{
    private const string ScenePath = "Assets/Scenes/ProceduralDungeon.unity";
    private readonly List<string> autosaveDirectories = new();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (string directory in autosaveDirectories)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        autosaveDirectories.Clear();
        yield return null;
    }

    /// <summary>
    /// Descends three times, restores one changed floor exactly, and follows paired stairs home.
    /// </summary>
    [UnityTest]
    public IEnumerator ThreeDepthTraversalRestoresDoorAndPlacesFullPartyAtPairedArrivals()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        Assert.That(map, Is.Not.Null);
        int sceneAssetCount = UnityEditor
            .AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
            .Length;
        DungeonLevelDocument initialTemplate = map.ValidateSource().JsonMap.LevelDocument;
        TrackRuntimeDependencies(out CombatManager manager);
        TraversalTestActionController[] party =
        {
            CreateParty(initialTemplate, "party-slot-a"),
            CreateParty(initialTemplate, "party-slot-b"),
        };
        GameObject runtimeRoot = new("Dungeon Traversal Runtime");
        runtimeRoot.transform.SetParent(map.transform, false);
        string autosaveDirectory = Path.Combine(
            Application.temporaryCachePath,
            "issue-157-" + Guid.NewGuid().ToString("N")
        );
        autosaveDirectories.Add(autosaveDirectory);
        DungeonRunPersistenceBootstrapResult bootstrap = DungeonRunPersistenceBootstrap.StartNewRun(
            map,
            initialTemplate,
            DungeonEncounterCreatureCatalog.LoadDefaultOrThrow(),
            manager,
            party,
            new RecordingExplorationPresentation(),
            runtimeRoot,
            autosaveDirectory
        );
        Assert.That(
            bootstrap.IsSuccess,
            Is.True,
            string.Join(" ", bootstrap.Diagnostics.Select(item => item.Message))
        );
        DungeonRunController controller = bootstrap.Controller;
        Assert.That(controller.CurrentDepth, Is.Zero);
        DungeonLevelDocument generatedInitial = map.ValidateSource().JsonMap.LevelDocument;
        Assert.That(
            Object
                .FindObjectsByType<DungeonStairMarker>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None
                )
                .Count(stair => stair.Kind == DungeonStairKind.Up),
            Is.Zero,
            "Depth zero must not expose an Up stair."
        );
        Assert.That(
            generatedInitial.Stairs.Count(stair => stair.Kind == DungeonStairKind.Down),
            Is.EqualTo(1)
        );
        DungeonRoom initialRoom = generatedInitial.Rooms.Single(room =>
            Contains(room, generatedInitial.StartCell)
        );
        Assert.That(
            generatedInitial.EncounterPlans.Any(plan => plan.RoomId == initialRoom.Id),
            Is.False
        );
        DungeonStair initialDown = generatedInitial.Stairs.Single();
        Assert.That(
            Contains(initialRoom, initialDown.Cell)
                || Contains(initialRoom, initialDown.ArrivalCell),
            Is.False
        );
        Assert.That(
            LivingParty()
                .Select(member => Vector3Int.RoundToInt(member.transform.position))
                .Distinct()
                .Count(),
            Is.EqualTo(party.Length)
        );
        Assert.That(
            LivingParty()
                .All(member =>
                {
                    Vector3Int cell = Vector3Int.RoundToInt(member.transform.position);
                    return Contains(initialRoom, new DungeonCell(cell.x, cell.z));
                }),
            Is.True
        );

        string changedDoorId = string.Empty;
        for (int targetDepth = 1; targetDepth <= 3; targetDepth++)
        {
            DungeonStairMarker down = RequireStair(DungeonStairKind.Down);
            PlaceLivingPartyAt(down, LivingParty());
            DungeonTravelResult result = controller.TryUseStair(down, confirmed: true);
            Assert.That(
                result.IsSuccess,
                Is.True,
                string.Join(" ", result.Diagnostics.Select(item => item.Message))
            );
            Assert.That(result.Depth, Is.EqualTo(targetDepth));
            DungeonStairMarker upArrival = RequireStair(DungeonStairKind.Up);
            AssertPartyInRegion(upArrival, LivingParty());
            DungeonTravelResult eligibility = controller.TryUseStair(upArrival, confirmed: false);
            Assert.That(
                eligibility.Diagnostics.Single().Code,
                Is.EqualTo(DungeonTravelDiagnosticCode.ConfirmationRequired),
                "All arrived PCs must immediately be eligible at the paired stair."
            );
            yield return null;

            if (targetDepth == 1)
            {
                DungeonDoorController door = Object
                    .FindObjectsByType<DungeonDoorController>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None
                    )
                    .OrderBy(item => item.StableId, StringComparer.Ordinal)
                    .First();
                ActionController opener = LivingParty().First();
                opener.transform.position = new Vector3(
                    door.Cell.X + 1,
                    opener.transform.position.y,
                    door.Cell.Z
                );
                DungeonEncounterRuntimeController runtime =
                    Object.FindFirstObjectByType<DungeonEncounterRuntimeController>();
                Assert.That(runtime.TryOpenDoor(door.Cell), Is.True);
                Assert.That(door.IsOpen, Is.True);
                changedDoorId = door.StableId;
            }
        }

        for (int targetDepth = 2; targetDepth >= 0; targetDepth--)
        {
            DungeonStairMarker up = RequireStair(DungeonStairKind.Up);
            PlaceLivingPartyAt(up, LivingParty());
            DungeonTravelResult result = controller.TryUseStair(up, confirmed: true);
            Assert.That(
                result.IsSuccess,
                Is.True,
                string.Join(" ", result.Diagnostics.Select(item => item.Message))
            );
            Assert.That(result.Depth, Is.EqualTo(targetDepth));
            AssertPartyInRegion(RequireStair(DungeonStairKind.Down), LivingParty());
            yield return null;

            if (targetDepth == 1)
            {
                DungeonDoorController restored = Object
                    .FindObjectsByType<DungeonDoorController>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None
                    )
                    .Single(item =>
                        string.Equals(item.StableId, changedDoorId, StringComparison.Ordinal)
                    );
                Assert.That(restored.IsOpen, Is.True);
            }
        }

        Assert.That(controller.CurrentDepth, Is.Zero);
        Assert.That(
            SceneManager.sceneCount,
            Is.EqualTo(1),
            "Traversal must reuse the one loaded gameplay scene."
        );
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ScenePath));
        Assert.That(
            UnityEditor.AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Length,
            Is.EqualTo(sceneAssetCount),
            "Traversal must not create per-depth scene assets."
        );

        Object.Destroy(runtimeRoot);
        foreach (TraversalTestActionController member in party)
            Object.Destroy(member.gameObject);
        yield return null;
    }

    /// <summary>
    /// Verifies a standard four-PC party receives unique cells and is immediately eligible at the
    /// paired, encounter-free Up stair after descending.
    /// </summary>
    [UnityTest]
    public IEnumerator FourLivingPartyMembersArriveEligibleAtPairedStair()
    {
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single)
        );
        while (!load.isDone)
            yield return null;

        Map map = Object.FindFirstObjectByType<Map>();
        Assert.That(map, Is.Not.Null);
        DungeonLevelDocument initialTemplate = map.ValidateSource().JsonMap.LevelDocument;
        TrackRuntimeDependencies(out CombatManager manager);
        TraversalTestActionController[] party =
        {
            CreateParty(initialTemplate, "four-party-slot-a"),
            CreateParty(initialTemplate, "four-party-slot-b"),
            CreateParty(initialTemplate, "four-party-slot-c"),
            CreateParty(initialTemplate, "four-party-slot-d"),
        };
        GameObject runtimeRoot = new("Four-PC Dungeon Traversal Runtime");
        runtimeRoot.transform.SetParent(map.transform, false);
        string autosaveDirectory = Path.Combine(
            Application.temporaryCachePath,
            "issue-157-four-party-" + Guid.NewGuid().ToString("N")
        );
        autosaveDirectories.Add(autosaveDirectory);
        DungeonRunPersistenceBootstrapResult bootstrap = DungeonRunPersistenceBootstrap.StartNewRun(
            map,
            initialTemplate,
            DungeonEncounterCreatureCatalog.LoadDefaultOrThrow(),
            manager,
            party,
            new RecordingExplorationPresentation(),
            runtimeRoot,
            autosaveDirectory
        );
        Assert.That(
            bootstrap.IsSuccess,
            Is.True,
            string.Join(" ", bootstrap.Diagnostics.Select(item => item.Message))
        );

        DungeonStairMarker down = RequireStair(DungeonStairKind.Down);
        PlaceLivingPartyAt(down, party);
        DungeonTravelResult descent = bootstrap.Controller.TryUseStair(down, confirmed: true);

        Assert.That(
            descent.IsSuccess,
            Is.True,
            string.Join(" ", descent.Diagnostics.Select(item => item.Message))
        );
        DungeonStairMarker pairedUp = RequireStair(DungeonStairKind.Up);
        AssertPartyInRegion(pairedUp, party);
        DungeonTravelResult eligibility = bootstrap.Controller.TryUseStair(
            pairedUp,
            confirmed: false
        );
        Assert.That(
            eligibility.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.ConfirmationRequired),
            "All four arrived PCs must immediately be eligible at the paired stair."
        );

        Object.Destroy(runtimeRoot);
        foreach (TraversalTestActionController member in party)
            Object.Destroy(member.gameObject);
        yield return null;
    }

    private static void TrackRuntimeDependencies(out CombatManager manager)
    {
        if (Object.FindFirstObjectByType<TeamRules>() == null)
            new GameObject("Traversal Team Rules").AddComponent<TeamRules>();
        manager = Object.FindFirstObjectByType<CombatManager>();
        if (manager == null)
            manager = new GameObject("Traversal Combat Manager").AddComponent<CombatManager>();
    }

    private static TraversalTestActionController CreateParty(
        DungeonLevelDocument document,
        string rosterSlotId
    )
    {
        GameObject actor = new("Traversal Party " + rosterSlotId);
        actor.SetActive(false);
        TraversalTestActionController controller =
            actor.AddComponent<TraversalTestActionController>();
        actor.AddComponent<CreatureComponent>().InitializeHealthBeforeEncounter(12, 12);
        actor.AddComponent<Conditions>();
        actor.AddComponent<Token>();
        actor.AddComponent<Team>().Name = "Players";
        actor
            .AddComponent<DungeonPartyMemberIdentity>()
            .Configure(rosterSlotId, "party-content-" + rosterSlotId);
        actor.transform.position = new Vector3(document.StartCell.X, 0f, document.StartCell.Z);
        actor.SetActive(true);
        return controller;
    }

    private static bool Contains(DungeonRoom room, DungeonCell cell) =>
        cell.X >= room.MinimumX
        && cell.X <= room.MaximumX
        && cell.Z >= room.MinimumZ
        && cell.Z <= room.MaximumZ;

    private static ActionController[] LivingParty() =>
        Object
            .FindObjectsByType<ActionController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID
            )
            .Where(member =>
            {
                Team team = member.GetComponent<Team>();
                CreatureComponent creature = member.GetComponent<CreatureComponent>();
                return string.Equals(team?.Name, "Players", StringComparison.OrdinalIgnoreCase)
                    && (creature == null || !creature.IsDefeated);
            })
            .ToArray();

    private static DungeonStairMarker RequireStair(DungeonStairKind kind) =>
        Object
            .FindObjectsByType<DungeonStairMarker>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            )
            .Single(stair => stair.Kind == kind);

    private static void PlaceLivingPartyAt(DungeonStairMarker stair, ActionController[] livingParty)
    {
        DungeonLevelDocument document = stair
            .GetComponentInParent<Map>()
            .ValidateSource()
            .JsonMap.LevelDocument;
        DungeonStair documented = document.Stairs.Single(candidate =>
            string.Equals(candidate.Id, stair.StableId, StringComparison.Ordinal)
        );
        DungeonCell[] walkable = DungeonStairInteractionRegion
            .SelectCells(document, documented, BlockedStairCells(document), livingParty.Length)
            .ToArray();
        Assert.That(livingParty, Is.Not.Empty);
        Assert.That(livingParty.Length, Is.LessThanOrEqualTo(walkable.Length));
        for (int index = 0; index < livingParty.Length; index++)
        {
            Transform actor = livingParty[index].transform;
            DungeonCell cell = walkable[index];
            actor.position = new Vector3(cell.X, actor.position.y, cell.Z);
        }
        Physics.SyncTransforms();
    }

    private static void AssertPartyInRegion(
        DungeonStairMarker stair,
        ActionController[] livingParty
    )
    {
        DungeonLevelDocument document = stair
            .GetComponentInParent<Map>()
            .ValidateSource()
            .JsonMap.LevelDocument;
        DungeonStair documented = document.Stairs.Single(candidate =>
            string.Equals(candidate.Id, stair.StableId, StringComparison.Ordinal)
        );
        HashSet<DungeonCell> expected = new(
            DungeonStairInteractionRegion.SelectCells(
                document,
                documented,
                BlockedStairCells(document),
                livingParty.Length
            )
        );
        foreach (ActionController member in livingParty)
        {
            Vector3Int cell = Vector3Int.RoundToInt(member.transform.position);
            Assert.That(
                expected,
                Does.Contain(new DungeonCell(cell.x, cell.z)),
                $"Party member '{member.name}' did not arrive in the {stair.Kind} region."
            );
            Assert.That(
                new DungeonCell(cell.x, cell.z),
                Is.Not.EqualTo(documented.Cell),
                $"Party member '{member.name}' was placed on the {stair.Kind} stair model."
            );
        }
        Assert.That(
            livingParty
                .Select(member => Vector3Int.RoundToInt(member.transform.position))
                .Distinct()
                .Count(),
            Is.EqualTo(livingParty.Length),
            "Living arrivals must occupy unique cells."
        );
    }

    private static IEnumerable<DungeonCell> BlockedStairCells(DungeonLevelDocument document)
    {
        foreach (DungeonObjectPlacement placement in document.Objects)
            yield return placement.Cell;
        foreach (DungeonEncounterPlan plan in document.EncounterPlans)
        foreach (DungeonCell spawn in plan.SpawnCells)
            yield return spawn;
        if (document.RuntimeState == null)
            yield break;
        foreach (DungeonCreatureRuntimeState creature in document.RuntimeState.Creatures)
            yield return creature.Cell;
    }

    private sealed class TraversalTestActionController : ActionController
    {
        /// <inheritdoc/>
        public override void EndTurn()
        {
            ResetEncounterTurnState();
        }
    }

    private sealed class RecordingExplorationPresentation
        : IDungeonExplorationPresentation,
            IDungeonStairTraversalPresentation
    {
        /// <inheritdoc/>
        public void ShowExploration(
            System.Collections.Generic.IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) { }

        /// <inheritdoc/>
        public void HideExploration() { }

        /// <inheritdoc/>
        public void PresentStairTraversal(
            DungeonStairTraversalPrompt prompt,
            Action<bool> respond
        ) { }

        /// <inheritdoc/>
        public void DismissStairTraversal() { }
    }
}
