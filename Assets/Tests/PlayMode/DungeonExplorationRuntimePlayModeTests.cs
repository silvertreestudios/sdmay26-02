using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.KayKit;
using Game.Rules;
using Game.Rules.Runtime;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Verifies leader-led exploration, immediate encounter boundaries, and generated-door policy
/// against the live grid and combat implementations.
/// </summary>
public sealed class DungeonExplorationRuntimePlayModeTests
{
    private const string GoblinJson = "DataFiles/pathfinder-monster-core/goblin-warrior";
    private readonly List<Object> cleanup = new();
    private CombatManager manager;
    private TestTokenMovement movement;
    private UnityEngine.Random.State randomState;

    /// <summary>Creates isolated runtime singletons and deterministic initiative randomness.</summary>
    [SetUp]
    public void SetUp()
    {
        DestroyExistingRuntime();
        randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(153);

        Track(new GameObject("Exploration Test Combat Log")).AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Exploration Test Team Rules")).AddComponent<TeamRules>();
        manager = Track(new GameObject("Exploration Test Combat Manager"))
            .AddComponent<CombatManager>();
        movement = Track(new GameObject("Exploration Test Token Movement"))
            .AddComponent<TestTokenMovement>();
        movement.ConfigureForSynchronousTests();
    }

    /// <summary>Destroys synthetic objects and restores the process-global random state.</summary>
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
        manager = null;
        movement = null;
        yield return null;
    }

    /// <summary>
    /// Verifies four living members follow through a ninety-degree turn in stable roster order.
    /// </summary>
    [UnityTest]
    public IEnumerator FourMemberPartyFollowsLeaderThroughCornerInStableRosterOrder()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[]
            {
                new Vector3Int(4, 0, 1),
                new Vector3Int(3, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(1, 0, 1),
            }
        );

        Ref<bool> firstStep = ExecuteExplorationStep(
            fixture,
            fixture.Party[0],
            new Vector3Int(4, 0, 2)
        );
        Assert.That(firstStep.Value, Is.True);
        AssertPartyCells(
            fixture,
            new DungeonCell(4, 2),
            new DungeonCell(4, 1),
            new DungeonCell(3, 1),
            new DungeonCell(2, 1)
        );

        Ref<bool> secondStep = ExecuteExplorationStep(
            fixture,
            fixture.Party[0],
            new Vector3Int(5, 0, 2)
        );

        Assert.That(secondStep.Value, Is.True);
        AssertPartyCells(
            fixture,
            new DungeonCell(5, 2),
            new DungeonCell(4, 2),
            new DungeonCell(4, 1),
            new DungeonCell(3, 1)
        );
        yield break;
    }

    /// <summary>
    /// Verifies the UI-provided selection callback changes authority without moving or stacking
    /// members, and the newly selected leader moves by only one cardinal cell.
    /// </summary>
    [UnityTest]
    public IEnumerator PresentationCallbackChangesLeaderWithoutTeleportOrOverlap()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[]
            {
                new Vector3Int(4, 0, 1),
                new Vector3Int(3, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(1, 0, 1),
            }
        );
        Vector3[] beforeSelection = fixture
            .Party.Select(member => member.GameObject.transform.position)
            .ToArray();

        Assert.That(fixture.Presentation.TrySelect(fixture.Party[2].Controller), Is.True);
        Assert.That(fixture.Presentation.Selected, Is.SameAs(fixture.Party[2].Controller));
        Assert.That(fixture.Party[2].Controller.IsInDungeonExploration, Is.True);
        Assert.That(
            fixture
                .Party.Where((_, index) => index != 2)
                .All(member => !member.Controller.IsInDungeonExploration),
            Is.True
        );
        Assert.That(
            fixture.Party.Select(member => member.GameObject.transform.position),
            Is.EqualTo(beforeSelection),
            "Changing leader must not rearrange the party."
        );

        Ref<bool> step = ExecuteExplorationStep(fixture, fixture.Party[2], new Vector3Int(2, 0, 2));

        Assert.That(step.Value, Is.True);
        AssertPartyCells(
            fixture,
            new DungeonCell(3, 1),
            new DungeonCell(2, 1),
            new DungeonCell(2, 2),
            new DungeonCell(1, 1)
        );
        for (int index = 0; index < fixture.Party.Count; index++)
        {
            int distance = ManhattanDistance(
                beforeSelection[index],
                fixture.Party[index].GameObject.transform.position
            );
            Assert.That(distance, Is.LessThanOrEqualTo(1), "A leader change cannot teleport a PC.");
        }
        yield break;
    }

    /// <summary>
    /// Verifies a follower whose exit event rejects movement remains in place and prevents every
    /// downstream follower from moving into an invalid partial chain.
    /// </summary>
    [UnityTest]
    public IEnumerator BlockedFollowerExitStopsItAndDownstreamSafely()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[]
            {
                new Vector3Int(4, 0, 1),
                new Vector3Int(3, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(1, 0, 1),
            }
        );
        GameObject blockedFollower = fixture.Party[1].GameObject;
        fixture
            .Grid.GetTiles()[3, 1]
            .OnExitTile.AddListener(data => PreventExitFor(data, blockedFollower));

        Ref<bool> step = ExecuteExplorationStep(fixture, fixture.Party[0], new Vector3Int(4, 0, 2));

        Assert.That(step.Value, Is.False);
        AssertPartyCells(
            fixture,
            new DungeonCell(4, 2),
            new DungeonCell(3, 1),
            new DungeonCell(2, 1),
            new DungeonCell(1, 1)
        );
        Assert.That(fixture.Grid.GetTiles()[4, 1].Occupants, Is.Empty);
        yield break;
    }

    /// <summary>Verifies a structurally closed generated door rejects leader movement.</summary>
    [UnityTest]
    public IEnumerator ClosedDoorCannotBeTraversed()
    {
        DoorSpec door = new("door-closed", new DungeonCell(2, 1));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 1) },
            doors: new[] { door }
        );

        Ref<bool> step = ExecuteExplorationStep(fixture, fixture.Party[0], new Vector3Int(2, 0, 1));

        Assert.That(step.Value, Is.False);
        AssertPartyCells(fixture, new DungeonCell(1, 1));
        Assert.That(fixture.Grid.GetTiles()[2, 1], Is.Null);
        Assert.That(fixture.Grid.GetLineOfSightBlocks()[2, 1], Is.True);
        Assert.That(fixture.Doors[door.Cell].Controller.IsOpen, Is.False);
        yield break;
    }

    /// <summary>
    /// Verifies exploration initialization, leader selection, and movement leave every combat
    /// economy field, combat turn owner, and unrelated enemy turn count unchanged.
    /// </summary>
    [UnityTest]
    public IEnumerator ExplorationLifecycleDoesNotMutateCombatTurnStateOrEnemyTurns()
    {
        Combatant enemy = CreateCombatant(
            "Uninvolved Enemy",
            "Enemies",
            new Vector3Int(7, 0, 7),
            initiative: 0,
            addToken: false
        );
        ControllerState enemyState = new(enemy.Controller);
        ControllerState[] expectedPartyStates = Array.Empty<ControllerState>();
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1) },
            configurePartyBeforeInitialization: party =>
            {
                party[0].SeedTurnState(true, 2, true, 2);
                party[1].SeedTurnState(false, 1, false, 1);
                expectedPartyStates = party
                    .Select(controller => new ControllerState(controller))
                    .ToArray();
            }
        );
        GameObject turnOwner = manager.WhosTurn();

        AssertControllerStates(expectedPartyStates, fixture.Party);
        enemyState.AssertMatches(enemy.Controller);
        Assert.That(enemy.Controller.StartTurnCount, Is.Zero);
        Assert.That(manager.WhosTurn(), Is.SameAs(turnOwner));

        Assert.That(fixture.Presentation.TrySelect(fixture.Party[1].Controller), Is.True);
        AssertControllerStates(expectedPartyStates, fixture.Party);
        enemyState.AssertMatches(enemy.Controller);
        Assert.That(enemy.Controller.StartTurnCount, Is.Zero);
        Assert.That(manager.WhosTurn(), Is.SameAs(turnOwner));

        Ref<bool> step = ExecuteExplorationStep(fixture, fixture.Party[1], new Vector3Int(0, 0, 2));

        Assert.That(step.Value, Is.True);
        AssertControllerStates(expectedPartyStates, fixture.Party);
        enemyState.AssertMatches(enemy.Controller);
        Assert.That(enemy.Controller.StartTurnCount, Is.Zero);
        Assert.That(manager.WhosTurn(), Is.SameAs(turnOwner));
        yield break;
    }

    /// <summary>
    /// Verifies the first leader cell inside an uncleared room starts initiative before any
    /// follower continuation and preserves every participant's actually committed grid cell.
    /// </summary>
    [UnityTest]
    public IEnumerator EncounterBoundaryStopsFollowersBeforeInitiativeUsesActualCells()
    {
        DungeonRoom room = new(1, 4, 2, 7, 4);
        DungeonEncounterPlan plan = new(
            "encounter-boundary",
            room.Id,
            DungeonEncounterThreat.Trivial,
            40,
            new[] { new DungeonCell(7, 3) },
            new[] { "goblin-warrior" }
        );
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[]
            {
                new Vector3Int(4, 0, 1),
                new Vector3Int(3, 0, 1),
                new Vector3Int(2, 0, 1),
                new Vector3Int(1, 0, 1),
            },
            width: 10,
            height: 10,
            rooms: new[] { room },
            encounterPlans: new[] { plan },
            configurePartyBeforeInitialization: party =>
                party[0].GetComponent<CreatureComponent>().initiative = 1000
        );

        Ref<bool> step = ExecuteExplorationStep(fixture, fixture.Party[0], new Vector3Int(4, 0, 2));
        yield return WaitForTurn();

        Assert.That(step.Value, Is.False);
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            fixture.Runtime.Lifecycle.GetRoomEncounter(room.Id).State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );
        AssertPartyCells(
            fixture,
            new DungeonCell(4, 2),
            new DungeonCell(3, 1),
            new DungeonCell(2, 1),
            new DungeonCell(1, 1)
        );
        DungeonEncounterMember enemy = fixture
            .Runtime.GetComponentsInChildren<DungeonEncounterMember>()
            .Single();
        Assert.That(enemy.transform.position, Is.EqualTo(new Vector3(7f, 0f, 3f)));
        Assert.That(manager.WhosTurn(), Is.Not.Null);
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(
                fixture.Party.Select(member => member.GameObject).Append(enemy.gameObject)
            )
        );
        Assert.That(
            manager.getPoistions(),
            Is.EquivalentTo(
                fixture
                    .Party.Select(member => member.GameObject.transform.position)
                    .Append(enemy.transform.position)
            ),
            "Initiative must observe committed cells, not the unexecuted follower plan."
        );
        yield break;
    }

    /// <summary>
    /// Verifies adjacent exploration doors open for free, immediately update topology and visuals,
    /// capture in ordinal order, and publish exactly one event per committed door.
    /// </summary>
    [UnityTest]
    public IEnumerator ExplorationDoorOpeningIsFreeObservableAndDeterministicallyCaptured()
    {
        DoorSpec zDoor = new("door-z", new DungeonCell(3, 2));
        DoorSpec aDoor = new("door-a", new DungeonCell(2, 3));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(2, 0, 2) },
            doors: new[] { zDoor, aDoor },
            configurePartyBeforeInitialization: party => party[0].SeedTurnState(false, 2, true, 2)
        );
        List<string> opened = new();
        fixture.Runtime.DoorOpened += opened.Add;
        uint actionPoints = fixture.Party[0].Controller.ActionPoints;

        CoroutineResult<bool> openedDoor = new CoroutineResult<bool>();
        yield return CoroutineRunner.Await(
            fixture.Runtime.TryOpenDoorAsync(zDoor.Cell),
            openedDoor
        );
        Assert.That(openedDoor.Value, Is.True);
        AssertDoorOpen(fixture, zDoor.Cell);
        Assert.That(fixture.Party[0].Controller.ActionPoints, Is.EqualTo(actionPoints));
        yield return CoroutineRunner.Await(
            fixture.Runtime.TryOpenDoorAsync(zDoor.Cell),
            openedDoor
        );
        Assert.That(openedDoor.Value, Is.False);
        Assert.That(opened, Is.EqualTo(new[] { zDoor.Id }));

        yield return CoroutineRunner.Await(
            fixture.Runtime.TryOpenDoorAsync(aDoor.Cell),
            openedDoor
        );
        Assert.That(openedDoor.Value, Is.True);
        AssertDoorOpen(fixture, aDoor.Cell);
        Assert.That(fixture.Party[0].Controller.ActionPoints, Is.EqualTo(actionPoints));
        Assert.That(opened, Is.EqualTo(new[] { zDoor.Id, aDoor.Id }));
        Assert.That(fixture.Runtime.CaptureOpenDoorIds(), Is.EqualTo(new[] { aDoor.Id, zDoor.Id }));
        yield break;
    }

    /// <summary>
    /// Verifies a living follower may open an adjacent exploration door when the selected leader
    /// is not adjacent, without granting the follower movement authority or spending actions.
    /// </summary>
    [UnityTest]
    public IEnumerator ExplorationDoorOpeningAllowsAdjacentFollower()
    {
        DoorSpec followerDoor = new("door-follower", new DungeonCell(4, 3));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 1), new Vector3Int(4, 0, 2) },
            doors: new[] { followerDoor },
            configurePartyBeforeInitialization: party =>
            {
                party[0].SeedTurnState(false, 2, true, 2);
                party[1].SeedTurnState(false, 1, true, 1);
            }
        );
        uint leaderActions = fixture.Party[0].Controller.ActionPoints;
        uint followerActions = fixture.Party[1].Controller.ActionPoints;

        Assert.That(fixture.Party[0].Controller.IsInDungeonExploration, Is.True);
        Assert.That(fixture.Party[1].Controller.IsInDungeonExploration, Is.False);
        CoroutineResult<bool> openedDoor = new CoroutineResult<bool>();
        yield return CoroutineRunner.Await(
            fixture.Runtime.TryOpenDoorAsync(followerDoor.Cell),
            openedDoor
        );
        Assert.That(openedDoor.Value, Is.True);

        AssertDoorOpen(fixture, followerDoor.Cell);
        Assert.That(fixture.Party[0].Controller.ActionPoints, Is.EqualTo(leaderActions));
        Assert.That(fixture.Party[1].Controller.ActionPoints, Is.EqualTo(followerActions));
        Assert.That(fixture.Party[0].Controller.IsInDungeonExploration, Is.True);
        Assert.That(fixture.Party[1].Controller.IsInDungeonExploration, Is.False);
        yield break;
    }

    /// <summary>
    /// Verifies only the current living adjacent PC can open a combat door and that a committed
    /// interaction spends exactly one action.
    /// </summary>
    [UnityTest]
    public IEnumerator CombatDoorOpeningRequiresCurrentLivingAdjacentPcAndCostsOneAction()
    {
        DoorSpec currentDoor = new("door-current", new DungeonCell(2, 1));
        DoorSpec noncurrentDoor = new("door-noncurrent", new DungeonCell(4, 4));
        DoorSpec deadActorDoor = new("door-dead-actor", new DungeonCell(1, 2));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 1), new Vector3Int(4, 0, 3) },
            doors: new[] { currentDoor, noncurrentDoor, deadActorDoor },
            configurePartyBeforeInitialization: party =>
            {
                party[0].GetComponent<CreatureComponent>().initiative = 1000;
                party[1].GetComponent<CreatureComponent>().initiative = 100;
            }
        );
        Combatant enemy = CreateCombatant(
            "Door Combat Enemy",
            "Enemies",
            new Vector3Int(7, 0, 7),
            initiative: 0,
            addToken: false
        );
        manager.StartDungeonCombat(
            fixture.Party.Select(member => member.Controller).Append(enemy.Controller).ToArray()
        );
        yield return WaitForTurn();
        Combatant current = fixture.Party[0];

        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));
        Assert.That(current.Controller.ActionPoints, Is.EqualTo(3u));
        CoroutineResult<bool> openedDoor = new CoroutineResult<bool>();
        yield return CoroutineRunner.Await(
            fixture.Runtime.TryOpenDoorAsync(currentDoor.Cell),
            openedDoor
        );
        Assert.That(openedDoor.Value, Is.True);
        Assert.That(current.Controller.ActionPoints, Is.EqualTo(2u));
        AssertDoorOpen(fixture, currentDoor.Cell);

        yield return CoroutineRunner.Await(
            fixture.Runtime.TryOpenDoorAsync(noncurrentDoor.Cell),
            openedDoor
        );
        Assert.That(
            openedDoor.Value,
            Is.False,
            "A door adjacent only to a noncurrent PC cannot be opened during another PC's turn."
        );
        Assert.That(fixture.Doors[noncurrentDoor.Cell].Controller.IsOpen, Is.False);
        Assert.That(current.Controller.ActionPoints, Is.EqualTo(2u));

        yield return CoroutineRunner.Await(
            current.Creature.ApplyFinalDamageAsync(100, RuleSource.FromSlug("test-dead-door-actor"))
        );
        Assert.That(current.Creature.IsDefeated, Is.True);
        yield return CoroutineRunner.Await(
            fixture.Runtime.TryOpenDoorAsync(deadActorDoor.Cell),
            openedDoor
        );
        Assert.That(openedDoor.Value, Is.False);
        Assert.That(fixture.Doors[deadActorDoor.Cell].Controller.IsOpen, Is.False);
        yield break;
    }

    /// <summary>
    /// Verifies a Stride that began in exploration remains free when its grid coroutine starts
    /// combat, clears exploration authority, and returns under normal initiative authority.
    /// </summary>
    [UnityTest]
    public IEnumerator ExplorationStrideThatStartsCombatBeforeReturningStillCostsZeroActions()
    {
        Combatant player = CreateCombatant(
            "Stride Boundary Player",
            "Players",
            Vector3Int.zero,
            initiative: 1000,
            addToken: false
        );
        Combatant enemy = CreateCombatant(
            "Stride Boundary Enemy",
            "Enemies",
            new Vector3Int(1, 0, 0),
            initiative: 0,
            addToken: false
        );
        CombatStartingGrid grid = Track(new GameObject("Combat Starting Grid"))
            .AddComponent<CombatStartingGrid>();
        grid.Configure(manager, new[] { player.Controller, enemy.Controller });
        Track(new GameObject("Exploration Test Coroutine Runner")).AddComponent<CoroutineRunner>();
        Stride stride = new(1);
        player.Controller.AddTestMovement(stride);
        player.Controller.SetDungeonExploration(true);
        Action<bool> clearExploration = active =>
        {
            if (active)
                player.Controller.SetDungeonExploration(false);
        };
        manager.CombatActivityChanged += clearExploration;

        try
        {
            player.Controller.TakeAction(stride);
            int remainingFrames = 30;
            while (player.Controller.IsTakingAction && remainingFrames-- > 0)
                yield return null;
            yield return WaitForTurn();

            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(grid.StrideCallCount, Is.EqualTo(1));
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(manager.WhosTurn(), Is.SameAs(player.GameObject));
            Assert.That(player.Controller.IsInDungeonExploration, Is.False);
            Assert.That(
                player.Controller.ActionPoints,
                Is.EqualTo(3u),
                "Combat granted three actions; the exploration-origin Stride must not subtract one."
            );
            Assert.That(enemy.Controller.StartTurnCount, Is.Zero);
        }
        finally
        {
            manager.CombatActivityChanged -= clearExploration;
            OnActionCancel.RemoveAllListeners();
        }
    }

    private RuntimeFixture CreateRuntimeFixture(
        IReadOnlyList<Vector3Int> partyCells,
        int width = 8,
        int height = 8,
        IReadOnlyList<DoorSpec> doors = null,
        IReadOnlyList<DungeonRoom> rooms = null,
        IReadOnlyList<DungeonEncounterPlan> encounterPlans = null,
        Action<IReadOnlyList<TestActionController>> configurePartyBeforeInitialization = null
    )
    {
        doors ??= Array.Empty<DoorSpec>();
        rooms ??= Array.Empty<DungeonRoom>();
        encounterPlans ??= Array.Empty<DungeonEncounterPlan>();
        if (partyCells == null || partyCells.Count == 0)
            throw new ArgumentException(
                "A synthetic exploration party is required.",
                nameof(partyCells)
            );

        TileType[,] gridData = GroundGrid(width, height);
        bool[,] lineOfSight = new bool[width, height];
        foreach (DoorSpec door in doors)
        {
            gridData[door.Cell.X, door.Cell.Z] = TileType.ClosedDoor;
            lineOfSight[door.Cell.X, door.Cell.Z] = true;
        }

        GameObject mapOwner = Track(new GameObject("Synthetic Exploration Map"));
        mapOwner.SetActive(false);
        SyntheticMap map = mapOwner.AddComponent<SyntheticMap>();
        map.ConfigureSynthetic(gridData, lineOfSight);
        GridBase grid = mapOwner.AddComponent<GridBase>();
        Assert.That(mapOwner.GetComponent<GridInput>(), Is.Not.Null);

        Dictionary<DungeonCell, DoorFixture> doorFixtures = new();
        foreach (DoorSpec door in doors)
            doorFixtures.Add(door.Cell, CreateDoor(mapOwner.transform, map, door));

        GameObject runtimeOwner = Track(new GameObject("Dungeon Exploration Runtime"));
        runtimeOwner.transform.SetParent(mapOwner.transform, false);
        DungeonEncounterRuntimeController runtime =
            runtimeOwner.AddComponent<DungeonEncounterRuntimeController>();

        List<Combatant> party = new(partyCells.Count);
        for (int index = 0; index < partyCells.Count; index++)
        {
            party.Add(
                CreateCombatant(
                    $"Exploration PC {index + 1}",
                    "Players",
                    partyCells[index],
                    initiative: 100 - index,
                    addToken: true
                )
            );
        }
        configurePartyBeforeInitialization?.Invoke(
            party.Select(member => member.Controller).ToArray()
        );

        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        if (encounterPlans.Count > 0)
        {
            GameObject prefab = CreateEncounterPrefab(new Vector3Int(width - 1, 0, height - 1));
            catalog.ReplaceEntries(
                new[]
                {
                    new DungeonEncounterCreatureCatalogEntry("goblin-warrior", GoblinJson, prefab),
                }
            );
        }

        mapOwner.SetActive(true);
        Assert.That(grid.IsInitialized, Is.True);
        foreach (Combatant member in party)
            Assert.That(member.Token.IsRegistered, Is.True, member.GameObject.name);

        DungeonLevelDocument document = new(
            new DungeonGenerationMetadata("exploration-runtime-test", 153, 0, 0),
            Rows(gridData),
            rooms,
            doors.Select(door => new DungeonDoor(door.Id, door.Cell)),
            Array.Empty<DungeonStair>(),
            new DungeonCell(partyCells[0].x, partyCells[0].z),
            partyCells.Select(cell => new DungeonCell(cell.x, cell.z)),
            Array.Empty<DungeonObjectPlacement>(),
            encounterPlans
        );
        RecordingExplorationPresentation presentation = new();
        runtime.InitializePristine(
            document,
            catalog,
            manager,
            party.Select(member => member.Controller),
            presentation
        );

        return new RuntimeFixture(map, grid, runtime, presentation, party, doorFixtures);
    }

    private Combatant CreateCombatant(
        string name,
        string teamName,
        Vector3Int cell,
        int initiative,
        bool addToken
    )
    {
        GameObject owner = Track(new GameObject(name));
        owner.transform.position = cell;
        CreatureComponent creature = owner.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.speed = 25;
        creature.initiative = initiative;
        creature.InitializeHealthBeforeEncounter(10, 10);
        owner.AddComponent<Conditions>();
        Team team = owner.AddComponent<Team>();
        team.Name = teamName;
        TestActionController controller = owner.AddComponent<TestActionController>();
        Token token = addToken ? owner.AddComponent<Token>() : null;
        manager.AddCombatant(controller);
        return new Combatant(owner, creature, controller, token);
    }

    private GameObject CreateEncounterPrefab(Vector3Int cell)
    {
        GameObject prefab = Track(new GameObject("Exploration Encounter Prefab"));
        prefab.transform.position = cell;
        prefab.AddComponent<TestActionController>();
        Team team = prefab.AddComponent<Team>();
        team.Name = "Enemies";
        prefab.AddComponent<Token>();
        return prefab;
    }

    private DoorFixture CreateDoor(Transform parent, SyntheticMap map, DoorSpec door)
    {
        GameObject owner = Track(new GameObject($"Door {door.Id}"));
        owner.transform.SetParent(parent, false);
        GameObject closed = Track(new GameObject("ClosedVisual"));
        closed.transform.SetParent(owner.transform, false);
        GameObject open = Track(new GameObject("OpenVisual"));
        open.transform.SetParent(owner.transform, false);
        DungeonDoorController controller = owner.AddComponent<DungeonDoorController>();
        controller.Configure(door.Id, door.Cell, false, map, closed, open);
        return new DoorFixture(controller, closed, open);
    }

    private Ref<bool> ExecuteExplorationStep(
        RuntimeFixture fixture,
        Combatant leader,
        Vector3Int destination
    )
    {
        IExplorationStrideCoordinator coordinator = fixture.Runtime;
        Assert.That(coordinator.Handles(leader.GameObject), Is.True);
        leader.Controller.IsTakingAction = true;
        Ref<bool> continuePath = new(true);
        RunToCompletion(
            coordinator.ExecuteStep(
                leader.GameObject,
                destination,
                fixture.Grid.GetTiles(),
                movement,
                continuePath
            )
        );
        if (!manager.IsCombatActive)
            leader.Controller.IsTakingAction = false;
        return continuePath;
    }

    private void RunToCompletion(IEnumerator root)
    {
        Stack<IEnumerator> routines = new();
        routines.Push(root ?? throw new ArgumentNullException(nameof(root)));
        int remainingOperations = 10000;
        while (routines.Count > 0 && remainingOperations-- > 0)
        {
            IEnumerator current = routines.Peek();
            if (!current.MoveNext())
            {
                routines.Pop();
                continue;
            }

            if (current.Current is CustomYieldInstruction instruction)
            {
                if (instruction.keepWaiting)
                    movement.CompletePendingMovement();
                Assert.That(instruction.keepWaiting, Is.False);
                continue;
            }
            if (current.Current is IEnumerator nested)
            {
                routines.Push(nested);
                continue;
            }
            Assert.That(
                current.Current,
                Is.Null,
                "Synthetic steps yielded an unsupported operation."
            );
        }
        Assert.That(
            remainingOperations,
            Is.GreaterThan(0),
            "Synthetic movement did not terminate."
        );
    }

    private static IEnumerator PreventExitFor(
        (GameObject Token, Vector3Int Cell, Ref<bool> Prevented) exit,
        GameObject blockedFollower
    )
    {
        if (exit.Token == blockedFollower)
            exit.Prevented.Value = true;
        yield break;
    }

    private static void AssertPartyCells(RuntimeFixture fixture, params DungeonCell[] expected)
    {
        Assert.That(expected, Has.Length.EqualTo(fixture.Party.Count));
        DungeonCell[] actual = fixture.Party.Select(member => CellOf(member.GameObject)).ToArray();
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(actual.Distinct().Count(), Is.EqualTo(actual.Length));
        for (int index = 0; index < expected.Length; index++)
        {
            Tile tile = fixture.Grid.GetTiles()[expected[index].X, expected[index].Z];
            Assert.That(tile, Is.Not.Null);
            Assert.That(tile.Occupants, Is.EqualTo(new[] { fixture.Party[index].GameObject }));
        }
    }

    private static void AssertControllerStates(
        IReadOnlyList<ControllerState> expected,
        IReadOnlyList<Combatant> party
    )
    {
        Assert.That(expected.Count, Is.EqualTo(party.Count));
        for (int index = 0; index < party.Count; index++)
            expected[index].AssertMatches(party[index].Controller);
    }

    private static void AssertDoorOpen(RuntimeFixture fixture, DungeonCell cell)
    {
        DoorFixture door = fixture.Doors[cell];
        Assert.That(door.Controller.IsOpen, Is.True);
        Assert.That(door.ClosedVisual.activeSelf, Is.False);
        Assert.That(door.OpenVisual.activeSelf, Is.True);
        Assert.That(fixture.Map.GetMapData()[cell.X, cell.Z], Is.EqualTo(TileType.Door));
        Assert.That(fixture.Grid.GetTiles()[cell.X, cell.Z], Is.Not.Null);
        Assert.That(fixture.Grid.GetLineOfSightBlocks()[cell.X, cell.Z], Is.False);
    }

    private static DungeonCell CellOf(GameObject owner)
    {
        Vector3Int position = Vector3Int.RoundToInt(owner.transform.position);
        return new DungeonCell(position.x, position.z);
    }

    private static int ManhattanDistance(Vector3 first, Vector3 second)
    {
        Vector3Int firstCell = Vector3Int.RoundToInt(first);
        Vector3Int secondCell = Vector3Int.RoundToInt(second);
        return Math.Abs(firstCell.x - secondCell.x) + Math.Abs(firstCell.z - secondCell.z);
    }

    private static TileType[,] GroundGrid(int width, int height)
    {
        TileType[,] data = new TileType[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
                data[x, z] = TileType.Ground;
        }
        return data;
    }

    private static IReadOnlyList<string> Rows(TileType[,] grid)
    {
        string[] rows = new string[grid.GetLength(1)];
        for (int z = grid.GetLength(1) - 1; z >= 0; z--)
        {
            char[] cells = new char[grid.GetLength(0)];
            for (int x = 0; x < grid.GetLength(0); x++)
            {
                cells[x] = grid[x, z] switch
                {
                    TileType.Ground => '.',
                    TileType.Door or TileType.ClosedDoor => 'D',
                    _ => '#',
                };
            }
            rows[grid.GetLength(1) - 1 - z] = new string(cells);
        }
        return rows;
    }

    private IEnumerator WaitForTurn()
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (manager.WhosTurn() == null && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(manager.WhosTurn(), Is.Not.Null);
    }

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
            GridAPI component in Object.FindObjectsByType<GridAPI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            Token component in Object.FindObjectsByType<Token>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            TokenMovement component in Object.FindObjectsByType<TokenMovement>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            CoroutineRunner component in Object.FindObjectsByType<CoroutineRunner>(
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

        foreach (GameObject owner in owners)
            Object.DestroyImmediate(owner);
    }

    private readonly struct DoorSpec
    {
        internal DoorSpec(string id, DungeonCell cell)
        {
            Id = id;
            Cell = cell;
        }

        internal string Id { get; }
        internal DungeonCell Cell { get; }
    }

    private readonly struct ControllerState
    {
        private readonly uint actionPoints;
        private readonly bool reacted;
        private readonly uint strikePenalty;
        private readonly bool hasTurnAuthority;
        private readonly bool isTakingAction;

        internal ControllerState(ActionController controller)
        {
            actionPoints = controller.ActionPoints;
            reacted = controller.Reacted;
            strikePenalty = controller.StrikePenalty;
            hasTurnAuthority = controller.HasTurnAuthority;
            isTakingAction = controller.IsTakingAction;
        }

        internal void AssertMatches(ActionController controller)
        {
            Assert.That(controller.ActionPoints, Is.EqualTo(actionPoints));
            Assert.That(controller.Reacted, Is.EqualTo(reacted));
            Assert.That(controller.StrikePenalty, Is.EqualTo(strikePenalty));
            Assert.That(controller.HasTurnAuthority, Is.EqualTo(hasTurnAuthority));
            Assert.That(controller.IsTakingAction, Is.EqualTo(isTakingAction));
        }
    }

    private sealed class RuntimeFixture
    {
        internal RuntimeFixture(
            SyntheticMap map,
            GridBase grid,
            DungeonEncounterRuntimeController runtime,
            RecordingExplorationPresentation presentation,
            IReadOnlyList<Combatant> party,
            IReadOnlyDictionary<DungeonCell, DoorFixture> doors
        )
        {
            Map = map;
            Grid = grid;
            Runtime = runtime;
            Presentation = presentation;
            Party = party;
            Doors = doors;
        }

        internal SyntheticMap Map { get; }
        internal GridBase Grid { get; }
        internal DungeonEncounterRuntimeController Runtime { get; }
        internal RecordingExplorationPresentation Presentation { get; }
        internal IReadOnlyList<Combatant> Party { get; }
        internal IReadOnlyDictionary<DungeonCell, DoorFixture> Doors { get; }
    }

    private sealed class DoorFixture
    {
        internal DoorFixture(
            DungeonDoorController controller,
            GameObject closedVisual,
            GameObject openVisual
        )
        {
            Controller = controller;
            ClosedVisual = closedVisual;
            OpenVisual = openVisual;
        }

        internal DungeonDoorController Controller { get; }
        internal GameObject ClosedVisual { get; }
        internal GameObject OpenVisual { get; }
    }

    private sealed class Combatant
    {
        internal Combatant(
            GameObject gameObject,
            CreatureComponent creature,
            TestActionController controller,
            Token token
        )
        {
            GameObject = gameObject;
            Creature = creature;
            Controller = controller;
            Token = token;
        }

        internal GameObject GameObject { get; }
        internal CreatureComponent Creature { get; }
        internal TestActionController Controller { get; }
        internal Token Token { get; }
    }

    private sealed class SyntheticMap : Map
    {
        internal void ConfigureSynthetic(TileType[,] gridData, bool[,] lineOfSightBlocks)
        {
            GridData = gridData;
            LineOfSightBlocks = lineOfSightBlocks;
        }
    }

    private sealed class TestTokenMovement : TokenMovement
    {
        internal void ConfigureForSynchronousTests()
        {
            StepHeight = 0f;
            MaxRotation = 0f;
            JumpTime = 0.001f;
            PtLerp = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            YLerp = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        }

        internal void CompletePendingMovement()
        {
            if (Token == null)
                return;
            Token.position = EndPoint;
            IsMoving = false;
            CurrentTime = JumpTime;
            Token = null;
        }
    }

    private sealed class TestActionController : ActionController
    {
        internal int StartTurnCount { get; private set; }

        internal void SeedTurnState(
            bool hasTurnAuthority,
            uint actionPoints,
            bool reacted,
            uint strikePenalty
        )
        {
            if (hasTurnAuthority)
                base.StartTurn();
            else
                base.ResetEncounterTurnState();
            ActionPoints = actionPoints;
            Reacted = reacted;
            StrikePenalty = strikePenalty;
        }

        internal void AddTestMovement(EntityAction action) => Movements.Add(action);

        /// <inheritdoc/>
        public override void StartTurn()
        {
            StartTurnCount++;
            base.StartTurn();
        }

        /// <inheritdoc/>
        public override void EndTurn()
        {
            if (!HasTurnAuthority)
                return;
            ResetEncounterTurnState();
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    private sealed class RecordingExplorationPresentation : IDungeonExplorationPresentation
    {
        private Func<ActionController, bool> trySelectLeader = _ => false;

        internal ActionController Selected { get; private set; }

        internal bool TrySelect(ActionController candidate)
        {
            bool selected = trySelectLeader(candidate);
            if (selected)
                Selected = candidate;
            return selected;
        }

        /// <inheritdoc/>
        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        )
        {
            Selected = selected;
            this.trySelectLeader = trySelectLeader;
        }

        /// <inheritdoc/>
        public void HideExploration() { }
    }

    private sealed class CombatStartingGrid : GridAPI
    {
        private CombatManager manager;
        private IReadOnlyList<ActionController> participants = Array.Empty<ActionController>();

        internal int StrideCallCount { get; private set; }

        internal void Configure(CombatManager manager, IReadOnlyList<ActionController> participants)
        {
            this.manager = manager;
            this.participants = participants;
        }

        /// <inheritdoc/>
        public override IEnumerator Stride(GameObject character)
        {
            StrideCallCount++;
            manager.StartDungeonCombat(participants);
            yield break;
        }

        /// <inheritdoc/>
        public override IEnumerator GetStrikeTarget(
            GameObject attacker,
            StrikeTargetRequest request,
            CoroutineResult<StrikeTargetResult> target
        )
        {
            yield break;
        }

        /// <inheritdoc/>
        public override IEnumerator GetAreaTarget(
            AreaTargetSource source,
            AreaTargetRequest request,
            CoroutineResult<AreaTargetResult> target
        )
        {
            yield break;
        }

        /// <inheritdoc/>
        public override bool DestroyToken(GameObject token) => false;
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
