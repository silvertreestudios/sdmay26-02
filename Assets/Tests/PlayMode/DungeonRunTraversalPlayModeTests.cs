using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Actors;
using Game.KayKit;
using GridPrivate;
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
                DungeonLevelDocument currentFloor = map.ValidateSource().JsonMap.LevelDocument;
                int? arrivalRoomId = DungeonEncounterPlanner.FindArrivalRoomId(
                    currentFloor,
                    upArrival.ArrivalCell
                );
                Assert.That(arrivalRoomId, Is.Not.Null);
                TileType[,] topology = map.GetMapData();
                DungeonDoorController door = Object
                    .FindObjectsByType<DungeonDoorController>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None
                    )
                    .Where(candidate =>
                        DungeonEncounterPlanner.FindArrivalRoomId(currentFloor, candidate.Cell)
                        == arrivalRoomId
                    )
                    .OrderBy(item => item.StableId, StringComparer.Ordinal)
                    .First();
                DungeonCell openerCell = CardinalNeighbors(door.Cell)
                    .First(cell =>
                        cell.X >= 0
                        && cell.Z >= 0
                        && cell.X < topology.GetLength(0)
                        && cell.Z < topology.GetLength(1)
                        && GridBase.IsWalkableTile(topology[cell.X, cell.Z])
                    );
                ActionController opener = LivingParty().First();
                opener.transform.position = new Vector3(
                    openerCell.X,
                    opener.transform.position.y,
                    openerCell.Z
                );
                DungeonEncounterRuntimeController runtime =
                    Object.FindFirstObjectByType<DungeonEncounterRuntimeController>();

                Assert.That(runtime.TryOpenDoor(door.Cell), Is.True);
                Assert.That(door.IsOpen, Is.True);
                Assert.That(
                    manager.IsCombatActive,
                    Is.False,
                    "Opening the encounter-free arrival room's door must preserve traversal focus."
                );
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
    /// Verifies an exploration stair click does not require the party to gather first, then places
    /// the full four-PC party in unique cells at the paired encounter-free arrival stair.
    /// </summary>
    [UnityTest]
    public IEnumerator ExplorationStairClickIgnoresPartyProximityAndPlacesFullPartyAtArrival()
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
        party[0].AddAction(new RulesStrideAction());
        GameObject runtimeRoot = new("Four-PC Dungeon Traversal Runtime");
        runtimeRoot.transform.SetParent(map.transform, false);
        string autosaveDirectory = Path.Combine(
            Application.temporaryCachePath,
            "issue-157-four-party-" + Guid.NewGuid().ToString("N")
        );
        autosaveDirectories.Add(autosaveDirectory);
        RecordingExplorationPresentation presentation = new();
        DungeonRunPersistenceBootstrapResult bootstrap = DungeonRunPersistenceBootstrap.StartNewRun(
            map,
            initialTemplate,
            DungeonEncounterCreatureCatalog.LoadDefaultOrThrow(),
            manager,
            party,
            presentation,
            runtimeRoot,
            autosaveDirectory
        );
        Assert.That(
            bootstrap.IsSuccess,
            Is.True,
            string.Join(" ", bootstrap.Diagnostics.Select(item => item.Message))
        );

        DungeonStairMarker down = RequireStair(DungeonStairKind.Down);
        Vector3[] positionsBeforeClick = party
            .Select(member => member.transform.position)
            .ToArray();
        Vector3Int leaderCell = Vector3Int.RoundToInt(party[0].transform.position);
        Assert.That(
            Math.Abs(leaderCell.x - down.Cell.X) + Math.Abs(leaderCell.z - down.Cell.Z),
            Is.GreaterThan(1),
            "The stair-claim regression requires a non-adjacent destination."
        );

        RaiseGridCellClick(map.GetComponent<GridInput>(), down.Cell);

        Assert.That(presentation.PresentCount, Is.EqualTo(1));
        Assert.That(presentation.LastCanConfirm, Is.True);
        Assert.That(bootstrap.Controller.CurrentDepth, Is.Zero);
        Assert.That(bootstrap.Runtime.HasActionInProgress, Is.False);
        Assert.That(HasActiveDestinationTravel(bootstrap.Runtime), Is.False);
        Assert.That(party.Any(member => member.IsTakingAction), Is.False);
        Assert.That(
            party.Select(member => member.transform.position),
            Is.EqualTo(positionsBeforeClick)
        );

        presentation.Respond(confirmed: false);
        yield return null;

        Assert.That(bootstrap.Controller.CurrentDepth, Is.Zero);
        Assert.That(bootstrap.Runtime.HasActionInProgress, Is.False);
        Assert.That(HasActiveDestinationTravel(bootstrap.Runtime), Is.False);
        Assert.That(party.Any(member => member.IsTakingAction), Is.False);
        Assert.That(
            party.Select(member => member.transform.position),
            Is.EqualTo(positionsBeforeClick)
        );
        Assert.That(
            bootstrap.Controller.LastDiagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.ConfirmationRequired)
        );

        DungeonTravelResult unconfirmed = bootstrap.Controller.TryUseStair(down, confirmed: false);
        Assert.That(
            unconfirmed.Diagnostics.Single().Code,
            Is.EqualTo(DungeonTravelDiagnosticCode.ConfirmationRequired),
            "Exploration travel should depend on the leader's stair click, not each PC's distance."
        );

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
                FindObjectsInactive.Exclude,
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

    private static bool HasActiveDestinationTravel(DungeonEncounterRuntimeController runtime)
    {
        PropertyInfo property = typeof(DungeonEncounterRuntimeController).GetProperty(
            "HasActiveDestinationTravel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(property, Is.Not.Null);
        return (bool)property.GetValue(runtime);
    }

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
        private Action<bool> respond = delegate { };
        private bool hasPendingResponse;

        internal int PresentCount { get; private set; }

        internal bool LastCanConfirm { get; private set; }

        internal void Respond(bool confirmed)
        {
            Assert.That(hasPendingResponse, Is.True);
            Action<bool> pending = respond;
            DismissStairTraversal();
            pending(confirmed);
        }

        /// <inheritdoc/>
        public void ShowExploration(
            System.Collections.Generic.IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) { }

        /// <inheritdoc/>
        public void HideExploration() { }

        /// <inheritdoc/>
        public void PresentStairTraversal(DungeonStairTraversalPrompt prompt, Action<bool> respond)
        {
            PresentCount++;
            LastCanConfirm = prompt.CanConfirm;
            this.respond = respond;
            hasPendingResponse = true;
        }

        /// <inheritdoc/>
        public void DismissStairTraversal()
        {
            respond = delegate { };
            hasPendingResponse = false;
        }
    }

    private static IEnumerable<DungeonCell> CardinalNeighbors(DungeonCell cell)
    {
        yield return new DungeonCell(cell.X, cell.Z + 1);
        yield return new DungeonCell(cell.X + 1, cell.Z);
        yield return new DungeonCell(cell.X, cell.Z - 1);
        yield return new DungeonCell(cell.X - 1, cell.Z);
    }
}
