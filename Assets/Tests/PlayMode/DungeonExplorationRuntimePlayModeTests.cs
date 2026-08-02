using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UniversalEvents;
using Object = UnityEngine.Object;

/// <summary>
/// Verifies leader-led exploration, immediate encounter boundaries, and generated-door policy
/// against the live grid and combat implementations.
/// </summary>
public sealed class DungeonExplorationRuntimePlayModeTests
{
    private const string GoblinJson = "DataFiles/pathfinder-monster-core/goblin-warrior";
    private readonly List<Object> cleanup = new();
    private RuntimeTestCombatLog combatLog;
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

        combatLog = Track(new GameObject("Exploration Test Combat Log"))
            .AddComponent<RuntimeTestCombatLog>();
        TeamRules teamRules = Track(new GameObject("Exploration Test Team Rules"))
            .AddComponent<TeamRules>();
        teamRules.AddHostileTeam("Players");
        teamRules.OneWayFriendly("Players", "Players");
        teamRules.AddHostileTeam("Enemies");
        teamRules.OneWayFriendly("Enemies", "Enemies");
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
        combatLog = null;
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
    /// Verifies exploration presentation advances separate token channels concurrently with
    /// linear horizontal speed and preserves per-token segment order and step notifications.
    /// </summary>
    [Test]
    public void ExplorationMovementChannelsAreIndependentConstantSpeedQueues()
    {
        movement.ConfigureForTimedTests(1.0f);
        GameObject leader = Track(new GameObject("Queued Presentation Leader"));
        GameObject follower = Track(new GameObject("Queued Presentation Follower"));
        follower.transform.position = new Vector3(-1.0f, 0.0f, 0.0f);
        int completedSteps = 0;
        void CountStep(Vector3 _) => completedSteps++;
        OnStepEnd.AddListener(CountStep);
        try
        {
            TokenMovement.ExplorationMovementOperation firstLeader = movement.QueueExplorationWalk(
                leader.transform,
                Vector3.right
            );
            TokenMovement.ExplorationMovementOperation followerStep = movement.QueueExplorationWalk(
                follower.transform,
                Vector3.zero
            );
            TokenMovement.ExplorationMovementOperation secondLeader = movement.QueueExplorationWalk(
                leader.transform,
                Vector3.right * 2.0f
            );

            movement.AdvanceTimedPresentation(0.25f);

            Assert.That(leader.transform.position.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(follower.transform.position.x, Is.EqualTo(-0.75f).Within(0.0001f));
            Assert.That(firstLeader.IsCompleted, Is.False);
            Assert.That(followerStep.IsCompleted, Is.False);
            Assert.That(secondLeader.IsCompleted, Is.False);

            movement.AdvanceTimedPresentation(0.75f);
            Assert.That(firstLeader.IsCompleted, Is.True);
            Assert.That(followerStep.IsCompleted, Is.True);
            Assert.That(secondLeader.IsCompleted, Is.False);
            Assert.That(completedSteps, Is.EqualTo(2));

            movement.AdvanceTimedPresentation(0.25f);
            Assert.That(leader.transform.position.x, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(secondLeader.IsCompleted, Is.False);
        }
        finally
        {
            OnStepEnd.RemoveListener(CountStep);
        }
    }

    /// <summary>
    /// Verifies the exploration action guard remains set after the leader arrives until the final
    /// follower presentation and tile-entry semantics have settled.
    /// </summary>
    [UnityTest]
    public IEnumerator ExplorationActionCompletesOnlyAfterFinalFollowerPresentation()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(2, 0, 2), new Vector3Int(1, 0, 2) },
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        movement.ConfigureForTimedTests(0.15f);
        Track(new GameObject("Follower Drain Test Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        TestActionController leader = fixture.Party[0].Controller;
        RulesStrideAction stride = leader.GetActions().OfType<RulesStrideAction>().Single();
        Vector3Int origin = Vector3Int.RoundToInt(leader.transform.position);
        Vector3Int destination = new(3, 0, 2);

        leader.TakeAction(
            stride,
            new FixedMovementPathResolver(
                new MovementPath(
                    new GridPosition(origin.x, origin.y, origin.z),
                    new[] { new GridPosition(destination.x, destination.y, destination.z) }
                )
            )
        );

        yield return null;
        movement.AdvanceTimedPresentation(0.15f);
        int remainingFrames = 10;
        Tile followerSource = fixture.Grid.GetTiles()[1, 2];
        while (
            remainingFrames-- > 0 && followerSource.Occupants.Contains(fixture.Party[1].GameObject)
        )
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "The follower was never queued.");
        Assert.That(
            leader.transform.position,
            Is.EqualTo((Vector3)destination),
            "The completed leader segment must precede the queued follower segment."
        );
        Assert.That(leader.IsTakingAction, Is.True);
        Assert.That(
            fixture.Party[1].GameObject.transform.position,
            Is.Not.EqualTo(new Vector3(2.0f, 0.0f, 2.0f))
        );

        movement.AdvanceTimedPresentation(0.075f);
        Assert.That(
            fixture.Party[1].GameObject.transform.position.x,
            Is.InRange(1.25f, 1.75f),
            "An explicitly half-advanced follower must remain in flight."
        );
        Assert.That(leader.IsTakingAction, Is.True);

        movement.AdvanceTimedPresentation(0.075f);
        remainingFrames = 10;
        while (remainingFrames-- > 0 && leader.IsTakingAction)
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "The follower presentation timed out.");
        Assert.That(leader.IsTakingAction, Is.False);
        AssertPartyCells(fixture, new DungeonCell(3, 2), new DungeonCell(2, 2));
    }

    /// <summary>
    /// Verifies destroying a token during its queued follower segment releases the presentation
    /// wait and does not place a destroyed object into its planned destination.
    /// </summary>
    [UnityTest]
    public IEnumerator DestroyedQueuedFollowerDoesNotHangOrReenterGrid()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(2, 0, 2), new Vector3Int(1, 0, 2) },
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        movement.ConfigureForTimedTests(0.15f);
        Track(new GameObject("Destroyed Follower Test Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        TestActionController leader = fixture.Party[0].Controller;
        GameObject follower = fixture.Party[1].GameObject;
        RulesStrideAction stride = leader.GetActions().OfType<RulesStrideAction>().Single();
        Vector3Int origin = Vector3Int.RoundToInt(leader.transform.position);
        Vector3Int destination = new(3, 0, 2);

        leader.TakeAction(
            stride,
            new FixedMovementPathResolver(
                new MovementPath(
                    new GridPosition(origin.x, origin.y, origin.z),
                    new[] { new GridPosition(destination.x, destination.y, destination.z) }
                )
            )
        );

        yield return null;
        movement.AdvanceTimedPresentation(0.15f);
        int remainingFrames = 10;
        Tile followerSource = fixture.Grid.GetTiles()[1, 2];
        while (remainingFrames-- > 0 && followerSource.Occupants.Contains(follower))
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "The follower was never queued.");
        Assert.That(leader.IsTakingAction, Is.True);
        Object.DestroyImmediate(follower);
        movement.AdvanceTimedPresentation(0.0f);
        remainingFrames = 10;
        while (remainingFrames-- > 0 && leader.IsTakingAction)
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "Destroyed follower cleanup timed out.");
        Assert.That(leader.IsTakingAction, Is.False);
        Assert.That(fixture.Grid.GetTiles()[2, 2].Occupants, Is.Empty);
        Assert.That(
            fixture.Grid.GetTiles()[3, 2].Occupants,
            Is.EqualTo(new[] { leader.gameObject })
        );
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
            new DungeonCell(2, 0)
        );
        for (int index = 0; index < fixture.Party.Count; index++)
        {
            int distance = ChebyshevDistance(
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
    /// Verifies a separated follower entering a dormant encounter room settles and starts Tactics
    /// before the temporary exploration root can commit its next leader cell.
    /// </summary>
    [UnityTest]
    public IEnumerator FollowerEncounterBoundaryInterruptsBeforeNextLeaderStep()
    {
        RuntimeFixture fixture = CreateSeparatedFollowerEncounterFixture();
        Track(new GameObject("Follower Boundary Suffix Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        Combatant leader = fixture.Party[0];
        RulesStrideAction stride = leader
            .Controller.GetActions()
            .OfType<RulesStrideAction>()
            .Single();
        Vector3Int from = Vector3Int.RoundToInt(leader.GameObject.transform.position);

        leader.Controller.TakeAction(
            stride,
            new FixedMovementPathResolver(
                new MovementPath(
                    new GridPosition(from.x, from.y, from.z),
                    new[] { new GridPosition(5, 0, 2), new GridPosition(6, 0, 2) }
                )
            )
        );
        int remainingFrames = 240;
        while (leader.Controller.IsTakingAction && remainingFrames-- > 0)
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "The follower boundary Stride timed out.");
        Assert.That(manager.IsCombatActive, Is.True);
        AssertPartyCells(fixture, new DungeonCell(5, 2), new DungeonCell(2, 2));
        Assert.That(
            CellOf(leader.GameObject),
            Is.Not.EqualTo(new DungeonCell(6, 2)),
            "The uncommitted leader suffix must not project after the follower starts Tactics."
        );
        Assert.That(
            manager.getPoistions(),
            Is.EquivalentTo(
                manager.GetCombatants().Select(combatant => combatant.transform.position)
            ),
            "Encounter construction must preserve every actually committed Unity cell."
        );
    }

    /// <summary>
    /// Verifies the final queued follower batch starts Tactics before the exploration action emits
    /// completion, which is also the persistent gameplay-state/autosave boundary.
    /// </summary>
    [UnityTest]
    public IEnumerator FinalFollowerEncounterBoundaryStartsTacticsBeforeActionCompletion()
    {
        RuntimeFixture fixture = CreateSeparatedFollowerEncounterFixture();
        Track(new GameObject("Final Follower Boundary Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        Combatant leader = fixture.Party[0];
        RulesStrideAction stride = leader
            .Controller.GetActions()
            .OfType<RulesStrideAction>()
            .Single();
        Vector3Int from = Vector3Int.RoundToInt(leader.GameObject.transform.position);
        bool actionCompleted = false;
        bool combatWasActiveAtCompletion = false;
        DungeonCell leaderCellAtCompletion = default;
        DungeonCell followerCellAtCompletion = default;

        void RecordActionCompletion()
        {
            actionCompleted = true;
            combatWasActiveAtCompletion = manager.IsCombatActive;
            leaderCellAtCompletion = CellOf(leader.GameObject);
            followerCellAtCompletion = CellOf(fixture.Party[1].GameObject);
        }

        OnActionComplete.AddListener(RecordActionCompletion);
        int remainingFrames = 240;
        try
        {
            leader.Controller.TakeAction(
                stride,
                new FixedMovementPathResolver(
                    new MovementPath(
                        new GridPosition(from.x, from.y, from.z),
                        new[] { new GridPosition(5, 0, 2) }
                    )
                )
            );
            while (leader.Controller.IsTakingAction && remainingFrames-- > 0)
                yield return null;
        }
        finally
        {
            OnActionComplete.RemoveListener(RecordActionCompletion);
        }

        Assert.That(remainingFrames, Is.GreaterThan(0), "The final follower boundary timed out.");
        Assert.That(actionCompleted, Is.True);
        Assert.That(combatWasActiveAtCompletion, Is.True);
        Assert.That(leaderCellAtCompletion, Is.EqualTo(new DungeonCell(5, 2)));
        Assert.That(followerCellAtCompletion, Is.EqualTo(new DungeonCell(2, 2)));
        Assert.That(manager.IsCombatActive, Is.True);
        AssertPartyCells(fixture, new DungeonCell(5, 2), new DungeonCell(2, 2));
    }

    /// <summary>
    /// Verifies the real rules-backed action commits the boundary step, abandons the obsolete
    /// exploration root, and preserves the newly granted combat actions.
    /// </summary>
    [UnityTest]
    public IEnumerator RulesBackedExplorationStrideHandsBoundaryCellToCombatAuthority()
    {
        DungeonRoom room = new(1, 4, 2, 7, 4);
        DungeonEncounterPlan plan = new(
            "rules-stride-boundary",
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
        Track(new GameObject("Rules Stride Test Coroutine Runner")).AddComponent<CoroutineRunner>();
        TestActionController leader = fixture.Party[0].Controller;
        RulesStrideAction stride = new RulesStrideAction();
        leader.AddAction(stride);
        Vector3Int from = Vector3Int.RoundToInt(leader.transform.position);
        Vector3Int destination = new Vector3Int(4, 0, 2);

        leader.TakeAction(
            stride,
            new FixedMovementPathResolver(
                new MovementPath(
                    new GridPosition(from.x, from.y, from.z),
                    new[] { new GridPosition(destination.x, destination.y, destination.z) }
                )
            )
        );
        int remainingFrames = 120;
        while (leader.IsTakingAction && remainingFrames-- > 0)
            yield return null;

        Assert.That(leader.IsTakingAction, Is.False, "The boundary Stride did not finish.");
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(manager.WhosTurn(), Is.SameAs(leader.gameObject));
        Assert.That(leader.ActionPoints, Is.EqualTo(3u));
        AssertPartyCells(
            fixture,
            new DungeonCell(4, 2),
            new DungeonCell(3, 1),
            new DungeonCell(2, 1),
            new DungeonCell(1, 1)
        );
    }

    /// <summary>
    /// Verifies exploration selection and projection swap the leader with an adjacent ally.
    /// </summary>
    [UnityTest]
    public IEnumerator RulesBackedExplorationStrideSwapsWithFollowerAtDestination()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(2, 0, 2), new Vector3Int(2, 0, 1) }
        );
        Track(new GameObject("Rules Stride Occupancy Test Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        TestActionController leader = fixture.Party[0].Controller;
        RulesStrideAction stride = new RulesStrideAction();
        leader.AddAction(stride);
        Vector3Int from = Vector3Int.RoundToInt(leader.transform.position);
        Vector3Int destination = Vector3Int.RoundToInt(
            fixture.Party[1].GameObject.transform.position
        );

        leader.TakeAction(
            stride,
            new FixedMovementPathResolver(
                new MovementPath(
                    new GridPosition(from.x, from.y, from.z),
                    new[] { new GridPosition(destination.x, destination.y, destination.z) }
                )
            )
        );
        int remainingFrames = 120;
        while (leader.IsTakingAction && remainingFrames-- > 0)
            yield return null;

        Assert.That(leader.IsTakingAction, Is.False, "The exploration swap did not finish.");
        Assert.That(manager.IsCombatActive, Is.False);
        AssertPartyCells(fixture, new DungeonCell(2, 1), new DungeonCell(2, 2));
    }

    /// <summary>Verifies destination travel crosses an ally in a one-cell-wide hallway.</summary>
    [UnityTest]
    public IEnumerator DestinationTravelCrossesAllyInNarrowHallway()
    {
        const int width = 12;
        const int height = 5;
        const int hallwayZ = 2;
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, hallwayZ), new Vector3Int(2, 0, hallwayZ) },
            width,
            height,
            customGridData: CorridorGrid(width, height, hallwayZ),
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        Track(new GameObject("Ally Hallway Travel Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(click, Is.Not.Null);

        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(10, 0, hallwayZ) });

        int remainingFrames = 900;
        while (
            remainingFrames-- > 0
            && (
                CellOf(fixture.Party[0].GameObject) != new DungeonCell(10, hallwayZ)
                || fixture.Party[0].Controller.IsTakingAction
            )
        )
        {
            yield return null;
        }

        Assert.That(remainingFrames, Is.GreaterThan(0), "Hallway destination travel timed out.");
        Assert.That(manager.IsCombatActive, Is.False);
        AssertPartyCells(fixture, new DungeonCell(10, hallwayZ), new DungeonCell(9, hallwayZ));
    }

    [UnityTest]
    public IEnumerator SynchronouslyCompletedDestinationTravelDoesNotBlockLaterTravel()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(new[] { new Vector3Int(1, 0, 2) }, width: 10);
        Track(new GameObject("Synchronous Destination Travel Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(click, Is.Not.Null);

        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(8, 0, 2) });
        Assert.That(fixture.Party[0].Controller.IsTakingAction, Is.False);
        Assert.That(
            Vector3Int.RoundToInt(fixture.Party[0].GameObject.transform.position),
            Is.EqualTo(new Vector3Int(1, 0, 2))
        );

        fixture.Party[0].Controller.AddAction(new RulesStrideAction());
        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(8, 0, 2) });

        int remainingFrames = 600;
        while (
            remainingFrames-- > 0
            && (
                Vector3Int.RoundToInt(fixture.Party[0].GameObject.transform.position)
                    != new Vector3Int(8, 0, 2)
                || fixture.Party[0].Controller.IsTakingAction
            )
        )
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "Later destination travel timed out.");
        AssertPartyCells(fixture, new DungeonCell(8, 2));
        Assert.That(fixture.Party[0].Controller.IsTakingAction, Is.False);
    }

    /// <summary>Verifies documented stair endpoints never become travel destinations.</summary>
    [Test]
    public void DocumentedStairEndpointIsClaimedBeforeDestinationPlanning()
    {
        DungeonStair stair = new(
            "down-stair",
            DungeonStairKind.Down,
            new DungeonCell(8, 2),
            new DungeonCell(7, 2)
        );
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            width: 10,
            stairs: new[] { stair },
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        PropertyInfo activeTravel = typeof(DungeonEncounterRuntimeController).GetProperty(
            "HasActiveDestinationTravel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(activeTravel, Is.Not.Null);

        RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), stair.Cell);

        AssertPartyCells(fixture, new DungeonCell(1, 2));
        Assert.That(fixture.Party[0].Controller.IsTakingAction, Is.False);
        Assert.That(activeTravel.GetValue(fixture.Runtime), Is.False);
    }

    /// <summary>
    /// Verifies active destination travel rejects a leader change between Strides, keeps route
    /// ownership stable, and restores leader selection after the full formation arrives.
    /// </summary>
    [UnityTest]
    public IEnumerator DestinationTravelRejectsLeaderChangeBetweenStridesAndPreservesFormation()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2), new Vector3Int(0, 0, 2) },
            width: 10,
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        Track(new GameObject("Destination Travel Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        PropertyInfo activeTravel = typeof(DungeonEncounterRuntimeController).GetProperty(
            "HasActiveDestinationTravel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(click, Is.Not.Null);
        Assert.That(activeTravel, Is.Not.Null);
        TestActionController initiatingLeader = fixture.Party[0].Controller;
        TestActionController follower = fixture.Party[1].Controller;
        bool selectionAttempted = false;
        bool routeWasActiveDuringSelection = false;
        bool leaderWasTakingActionDuringSelection = true;
        bool selectionAccepted = true;

        void AttemptLeaderChangeBetweenStrides()
        {
            if (selectionAttempted)
                return;

            selectionAttempted = true;
            routeWasActiveDuringSelection = (bool)activeTravel.GetValue(fixture.Runtime);
            leaderWasTakingActionDuringSelection = initiatingLeader.IsTakingAction;
            selectionAccepted = fixture.Presentation.TrySelect(follower);
        }

        OnActionComplete.AddListener(AttemptLeaderChangeBetweenStrides);
        int remainingFrames = 600;
        try
        {
            click.Invoke(fixture.Runtime, new object[] { new Vector3Int(8, 0, 2) });

            while (remainingFrames-- > 0 && (bool)activeTravel.GetValue(fixture.Runtime))
                yield return null;
        }
        finally
        {
            OnActionComplete.RemoveListener(AttemptLeaderChangeBetweenStrides);
        }

        Assert.That(remainingFrames, Is.GreaterThan(0), "Destination travel timed out.");
        Assert.That(selectionAttempted, Is.True);
        Assert.That(routeWasActiveDuringSelection, Is.True);
        Assert.That(leaderWasTakingActionDuringSelection, Is.False);
        Assert.That(selectionAccepted, Is.False);
        AssertPartyCells(fixture, new DungeonCell(8, 2), new DungeonCell(7, 2));
        Assert.That(initiatingLeader.IsTakingAction, Is.False);
        Assert.That(initiatingLeader.IsInDungeonExploration, Is.True);
        Assert.That(follower.IsInDungeonExploration, Is.False);
        Assert.That(
            combatLog.Messages.Count(message =>
                message == $"- {fixture.Party[0].GameObject.name} used Stride"
            ),
            Is.GreaterThan(1),
            "The initiating leader must execute the route as multiple Strides."
        );
        Assert.That(
            combatLog.Messages,
            Has.None.EqualTo($"- {fixture.Party[1].GameObject.name} used Stride")
        );
        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(fixture.Presentation.TrySelect(follower), Is.True);
        Assert.That(initiatingLeader.IsInDungeonExploration, Is.False);
        Assert.That(follower.IsInDungeonExploration, Is.True);
        AssertPartyCells(fixture, new DungeonCell(8, 2), new DungeonCell(7, 2));
    }

    /// <summary>
    /// Verifies the Tactics control cannot interrupt route ownership between Stride segments.
    /// </summary>
    [UnityTest]
    public IEnumerator DestinationTravelRejectsTacticsEntryBetweenStrides()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            width: 10,
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        Track(new GameObject("Destination Travel Tactics Gate Runner"))
            .AddComponent<CoroutineRunner>();
        PropertyInfo activeTravel = typeof(DungeonEncounterRuntimeController).GetProperty(
            "HasActiveDestinationTravel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(activeTravel, Is.Not.Null);
        TestActionController leader = fixture.Party[0].Controller;
        bool requestAttempted = false;
        bool routeWasActiveDuringRequest = false;
        bool leaderWasTakingActionDuringRequest = true;
        bool tacticsEntered = true;

        void AttemptTacticsBetweenStrides()
        {
            if (requestAttempted)
                return;

            requestAttempted = true;
            routeWasActiveDuringRequest = (bool)activeTravel.GetValue(fixture.Runtime);
            leaderWasTakingActionDuringRequest = leader.IsTakingAction;
            fixture.Presentation.RequestTactics();
            tacticsEntered = manager.IsCombatActive;
        }

        OnActionComplete.AddListener(AttemptTacticsBetweenStrides);
        int remainingFrames = 600;
        try
        {
            RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), new DungeonCell(8, 2));
            while (remainingFrames-- > 0 && (bool)activeTravel.GetValue(fixture.Runtime))
                yield return null;
        }
        finally
        {
            OnActionComplete.RemoveListener(AttemptTacticsBetweenStrides);
        }

        Assert.That(remainingFrames, Is.GreaterThan(0), "Destination travel timed out.");
        Assert.That(requestAttempted, Is.True);
        Assert.That(routeWasActiveDuringRequest, Is.True);
        Assert.That(leaderWasTakingActionDuringRequest, Is.False);
        Assert.That(tacticsEntered, Is.False);
        Assert.That(manager.IsCombatActive, Is.False);
        AssertPartyCells(fixture, new DungeonCell(8, 2));
    }

    [UnityTest]
    public IEnumerator DestinationTravelClickReplacesRouteAtCommittedBoundary()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            width: 12,
            height: 8,
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        Track(new GameObject("Replacement Destination Travel Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        PropertyInfo activeTravel = typeof(DungeonEncounterRuntimeController).GetProperty(
            "HasActiveDestinationTravel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(activeTravel, Is.Not.Null);
        bool replacementRequested = false;

        void ReplaceRouteAfterFirstStride()
        {
            if (replacementRequested)
                return;

            replacementRequested = true;
            RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), new DungeonCell(2, 6));
        }

        OnActionComplete.AddListener(ReplaceRouteAfterFirstStride);
        int remainingFrames = 600;
        try
        {
            RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), new DungeonCell(10, 2));
            while (remainingFrames-- > 0 && (bool)activeTravel.GetValue(fixture.Runtime))
                yield return null;
        }
        finally
        {
            OnActionComplete.RemoveListener(ReplaceRouteAfterFirstStride);
        }

        Assert.That(
            remainingFrames,
            Is.GreaterThan(0),
            "Replacement destination travel timed out."
        );
        Assert.That(replacementRequested, Is.True);
        AssertPartyCells(fixture, new DungeonCell(2, 6));
        Assert.That(manager.IsCombatActive, Is.False);
    }

    [UnityTest]
    public IEnumerator DestinationTravelRightClickInputCancelsQueuedStridesFromIdleState()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            width: 12,
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        Track(new GameObject("Cancelled Travel Coroutine Runner")).AddComponent<CoroutineRunner>();
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Mouse previousMouse = Mouse.current;
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        int globalCancelRequests = 0;
        void RecordGlobalCancel() => globalCancelRequests++;
        OnCancel.AddListener(RecordGlobalCancel);
        try
        {
            mouse.MakeCurrent();
            Assert.That(InputCompat.RightClickDown(), Is.False);
            Assert.That(fixture.Grid.Fsm.CurrentState, Is.TypeOf<StateIdle>());

            click.Invoke(fixture.Runtime, new object[] { new Vector3Int(10, 0, 2) });
            InputState.Change(
                mouse,
                new MouseState().WithButton(MouseButton.Right),
                InputUpdateType.Dynamic
            );
            mouse.MakeCurrent();
            Assert.That(InputCompat.RightClickDown(), Is.True);
            fixture.Grid.Fsm.InputUpdate();
        }
        finally
        {
            OnCancel.RemoveListener(RecordGlobalCancel);
            InputState.Change(mouse, new MouseState(), InputUpdateType.Dynamic);
            InputSystem.RemoveDevice(mouse);
            if (previousMouse != null && previousMouse.added)
                previousMouse.MakeCurrent();
        }

        Assert.That(globalCancelRequests, Is.Zero);
        int remainingFrames = 300;
        while (remainingFrames-- > 0 && fixture.Party[0].Controller.IsTakingAction)
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "Cancelled destination travel timed out.");
        Assert.That(
            Vector3Int.RoundToInt(fixture.Party[0].GameObject.transform.position),
            Is.Not.EqualTo(new Vector3Int(10, 0, 2))
        );
    }

    [UnityTest]
    public IEnumerator PartialFollowerProjectionFailureStopsDestinationRouteAfterOneStride()
    {
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2), new Vector3Int(0, 0, 2) },
            width: 12,
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        Track(new GameObject("Partial Follower Failure Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        GameObject blockedFollower = fixture.Party[1].GameObject;
        fixture
            .Grid.GetTiles()[0, 2]
            .OnExitTile.AddListener(data => PreventExitFor(data, blockedFollower));
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        PropertyInfo activeTravel = typeof(DungeonEncounterRuntimeController).GetProperty(
            "HasActiveDestinationTravel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(click, Is.Not.Null);
        Assert.That(activeTravel, Is.Not.Null);

        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(10, 0, 2) });
        int remainingFrames = 300;
        while (remainingFrames-- > 0 && (bool)activeTravel.GetValue(fixture.Runtime))
            yield return null;

        Assert.That(
            remainingFrames,
            Is.GreaterThan(0),
            "Destination travel did not terminate after the partial follower failure."
        );
        Assert.That(fixture.Party[0].Controller.IsTakingAction, Is.False);
        AssertPartyCells(fixture, new DungeonCell(2, 2), new DungeonCell(0, 2));
        Assert.That(fixture.Grid.GetTiles()[1, 2].Occupants, Is.Empty);
        Assert.That(
            combatLog.Messages.Count(message =>
                message == $"- {fixture.Party[0].GameObject.name} used Stride"
            ),
            Is.EqualTo(1),
            "A partial follower failure must not start a later Stride from the moved leader cell."
        );
        Assert.That(manager.IsCombatActive, Is.False);
    }

    [UnityTest]
    public IEnumerator ClosedDoorClickDuringDestinationTravelStopsAndOpensDoor()
    {
        DoorSpec door = new("travel-priority-door", new DungeonCell(2, 3));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            width: 10,
            height: 10,
            doors: new[] { door },
            configurePartyBeforeInitialization: controllers =>
            {
                controllers[0].AddAction(new RulesStrideAction());
                controllers[0].GetComponent<CreatureComponent>().speed = 25;
            }
        );
        Track(new GameObject("Door Destination Travel Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(click, Is.Not.Null);

        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(1, 0, 8) });
        Assert.That(
            fixture.Party[0].Controller.IsTakingAction,
            Is.True,
            "The destination click must start its rules-backed Stride before a door can queue."
        );
        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(2, 0, 3) });

        int remainingFrames = 300;
        while (
            remainingFrames-- > 0
            && (
                fixture.Party[0].Controller.IsTakingAction
                || !fixture.Doors[door.Cell].Controller.IsOpen
            )
        )
            yield return null;

        Assert.That(
            remainingFrames,
            Is.GreaterThan(0),
            $"Door interruption timed out at "
                + $"{Vector3Int.RoundToInt(fixture.Party[0].GameObject.transform.position)}; "
                + $"action active: {fixture.Party[0].Controller.IsTakingAction}; "
                + $"door open: {fixture.Doors[door.Cell].Controller.IsOpen}."
        );
        Assert.That(fixture.Doors[door.Cell].Controller.IsOpen, Is.True);
        Assert.That(
            Vector3Int.RoundToInt(fixture.Party[0].GameObject.transform.position),
            Is.EqualTo(new Vector3Int(1, 0, 3)),
            "The queued door must interrupt the temporary Stride suffix after its first committed step."
        );
        Assert.That(manager.IsCombatActive, Is.False);
    }

    /// <summary>
    /// Verifies a directly clicked same-room door uses repeated rules-backed Strides to reach its
    /// closest accessible side, then opens without requiring a second click.
    /// </summary>
    [UnityTest]
    public IEnumerator ReachableClosedDoorClickTravelsAdjacentAndOpens()
    {
        DoorSpec door = new("reachable-door", new DungeonCell(9, 2));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            width: 12,
            height: 6,
            doors: new[] { door },
            configurePartyBeforeInitialization: controllers =>
            {
                controllers[0].AddAction(new RulesStrideAction());
                controllers[0].GetComponent<CreatureComponent>().speed = 10;
            }
        );
        Track(new GameObject("Reachable Door Coroutine Runner")).AddComponent<CoroutineRunner>();

        RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), door.Cell);

        int remainingFrames = 600;
        while (
            remainingFrames-- > 0
            && (
                HasActiveDestinationTravel(fixture.Runtime)
                || fixture.Party[0].Controller.IsTakingAction
                || !fixture.Doors[door.Cell].Controller.IsOpen
            )
        )
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "Door interaction travel timed out.");
        AssertDoorOpen(fixture, door.Cell);
        DungeonCell leaderCell = CellOf(fixture.Party[0].GameObject);
        Assert.That(
            Math.Abs(leaderCell.X - door.Cell.X) + Math.Abs(leaderCell.Z - door.Cell.Z),
            Is.EqualTo(1),
            "The leader must stop cardinally adjacent before the door interaction runs."
        );
        Assert.That(manager.IsCombatActive, Is.False);
    }

    /// <summary>
    /// Verifies stair-style interactions use the same reachable-neighbor travel contract and do
    /// not invoke their original click behavior until movement has completely settled.
    /// </summary>
    [UnityTest]
    public IEnumerator ReachableStairInteractionInvokesCallbackAfterArrival()
    {
        DungeonStair stair = new(
            "reachable-stair",
            DungeonStairKind.Down,
            new DungeonCell(9, 2),
            new DungeonCell(8, 2)
        );
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            width: 12,
            height: 6,
            stairs: new[] { stair },
            configurePartyBeforeInitialization: controllers =>
            {
                controllers[0].AddAction(new RulesStrideAction());
                controllers[0].GetComponent<CreatureComponent>().speed = 10;
            }
        );
        Track(new GameObject("Reachable Stair Coroutine Runner")).AddComponent<CoroutineRunner>();
        bool interactionInvoked = false;

        bool accepted = fixture.Runtime.TryTravelToInteraction(
            stair.Cell,
            () => interactionInvoked = true
        );

        Assert.That(accepted, Is.True);
        Assert.That(
            interactionInvoked,
            Is.False,
            "A non-adjacent stair interaction must wait for travel to finish."
        );
        int remainingFrames = 600;
        while (remainingFrames-- > 0 && !interactionInvoked)
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "Stair interaction travel timed out.");
        Assert.That(HasActiveDestinationTravel(fixture.Runtime), Is.False);
        Assert.That(fixture.Party[0].Controller.IsTakingAction, Is.False);
        DungeonCell leaderCell = CellOf(fixture.Party[0].GameObject);
        Assert.That(
            Math.Abs(leaderCell.X - stair.Cell.X) + Math.Abs(leaderCell.Z - stair.Cell.Z),
            Is.EqualTo(1),
            "The stair callback must run from a cardinally adjacent cell."
        );
    }

    /// <summary>
    /// Verifies direct interaction travel never opens an intermediate door to reach a later room.
    /// </summary>
    [Test]
    public void ClosedIntermediateDoorBlocksDirectDoorInteraction()
    {
        DoorSpec firstDoor = new("first-room-door", new DungeonCell(5, 3));
        DoorSpec targetDoor = new("target-room-door", new DungeonCell(10, 3));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(2, 0, 3) },
            doors: new[] { firstDoor, targetDoor },
            customGridData: ThreeRoomGrid(),
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );

        RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), targetDoor.Cell);

        Assert.That(fixture.Doors[firstDoor.Cell].Controller.IsOpen, Is.False);
        Assert.That(fixture.Doors[targetDoor.Cell].Controller.IsOpen, Is.False);
        Assert.That(HasActiveDestinationTravel(fixture.Runtime), Is.False);
        Assert.That(fixture.Party[0].Controller.IsTakingAction, Is.False);
        AssertPartyCells(fixture, new DungeonCell(2, 3));
    }

    [UnityTest]
    public IEnumerator DestinationTravelStopsAtImmediateEncounterBoundary()
    {
        DungeonRoom room = new(1, 4, 2, 7, 7);
        DungeonEncounterPlan encounter = new(
            "destination-boundary",
            room.Id,
            DungeonEncounterThreat.Trivial,
            40,
            new[] { new DungeonCell(7, 6) },
            new[] { "goblin-warrior" }
        );
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(4, 0, 1), new Vector3Int(3, 0, 1) },
            width: 10,
            height: 10,
            rooms: new[] { room },
            encounterPlans: new[] { encounter },
            configurePartyBeforeInitialization: controllers =>
            {
                controllers[0].AddAction(new RulesStrideAction());
                controllers[0].GetComponent<CreatureComponent>().initiative = 1000;
            }
        );
        Track(new GameObject("Boundary Destination Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(4, 0, 6) });
        int remainingFrames = 300;
        while (remainingFrames-- > 0 && !manager.IsCombatActive)
            yield return null;
        while (remainingFrames-- > 0 && fixture.Party[0].Controller.IsTakingAction)
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "Encounter interruption timed out.");
        Assert.That(manager.IsCombatActive, Is.True);
        AssertPartyCells(fixture, new DungeonCell(4, 2), new DungeonCell(3, 1));
        Assert.That(
            Vector3Int.RoundToInt(fixture.Party[0].GameObject.transform.position),
            Is.Not.EqualTo(new Vector3Int(4, 0, 6))
        );
    }

    /// <summary>
    /// Verifies repeated rules-backed Strides cross a generated two-room floor, open its connecting
    /// door, and continue to the center of the second room without losing exploration authority.
    /// </summary>
    [UnityTest]
    public IEnumerator RulesBackedExplorationCrossesTwoRoomsThroughDoorAcrossRepeatedStrides()
    {
        DungeonRoom firstRoom = new(1, 1, 1, 9, 9);
        DungeonRoom secondRoom = new(2, 11, 1, 21, 9);
        DoorSpec door = new("two-room-door", new DungeonCell(10, 5));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(2, 0, 2), new Vector3Int(1, 0, 2) },
            doors: new[] { door },
            rooms: new[] { firstRoom, secondRoom },
            customGridData: TwoRoomGrid()
        );
        Track(new GameObject("Two Room Stride Test Coroutine Runner"))
            .AddComponent<CoroutineRunner>();
        Combatant leader = fixture.Party[0];
        RulesStrideAction stride = new RulesStrideAction();
        leader.Controller.AddAction(stride);

        yield return ExecuteRulesStride(
            leader,
            stride,
            new DungeonCell(3, 2),
            new DungeonCell(4, 2),
            new DungeonCell(5, 3)
        );
        AssertPartyCells(fixture, new DungeonCell(5, 3), new DungeonCell(4, 2));

        yield return ExecuteRulesStride(
            leader,
            stride,
            new DungeonCell(6, 4),
            new DungeonCell(7, 5),
            new DungeonCell(8, 5),
            new DungeonCell(9, 5)
        );
        AssertPartyCells(fixture, new DungeonCell(9, 5), new DungeonCell(8, 5));

        Assert.That(fixture.Runtime.TryOpenDoor(door.Cell), Is.True);
        AssertDoorOpen(fixture, door.Cell);

        yield return ExecuteRulesStride(
            leader,
            stride,
            new DungeonCell(10, 5),
            new DungeonCell(11, 5),
            new DungeonCell(12, 5),
            new DungeonCell(13, 5),
            new DungeonCell(14, 5)
        );
        AssertPartyCells(fixture, new DungeonCell(14, 5), new DungeonCell(13, 5));

        yield return ExecuteRulesStride(
            leader,
            stride,
            new DungeonCell(15, 5),
            new DungeonCell(16, 5)
        );

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(leader.Controller.IsInDungeonExploration, Is.True);
        AssertPartyCells(fixture, new DungeonCell(16, 5), new DungeonCell(15, 5));
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
            configurePartyBeforeInitialization: _ => { }
        );
        List<string> opened = new();
        fixture.Runtime.DoorOpened += opened.Add;
        uint actionPoints = fixture.Party[0].Controller.ActionPoints;

        Assert.That(fixture.Runtime.TryOpenDoor(zDoor.Cell), Is.True);
        AssertDoorOpen(fixture, zDoor.Cell);
        Assert.That(fixture.Party[0].Controller.ActionPoints, Is.EqualTo(actionPoints));
        Assert.That(fixture.Runtime.TryOpenDoor(zDoor.Cell), Is.False);
        Assert.That(opened, Is.EqualTo(new[] { zDoor.Id }));

        Assert.That(fixture.Runtime.TryOpenDoor(aDoor.Cell), Is.True);
        AssertDoorOpen(fixture, aDoor.Cell);
        Assert.That(fixture.Party[0].Controller.ActionPoints, Is.EqualTo(actionPoints));
        Assert.That(opened, Is.EqualTo(new[] { zDoor.Id, aDoor.Id }));
        Assert.That(fixture.Runtime.CaptureOpenDoorIds(), Is.EqualTo(new[] { aDoor.Id, zDoor.Id }));
        yield break;
    }

    [Test]
    public void ClosedDoorClickTakesPriorityOverDestinationTravel()
    {
        DoorSpec door = new("priority-door", new DungeonCell(2, 2));
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(1, 0, 2) },
            doors: new[] { door },
            configurePartyBeforeInitialization: controllers =>
                controllers[0].AddAction(new RulesStrideAction())
        );
        MethodInfo click = typeof(DungeonEncounterRuntimeController).GetMethod(
            "OnGridCellClicked",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        click.Invoke(fixture.Runtime, new object[] { new Vector3Int(2, 0, 2) });

        Assert.That(fixture.Doors[door.Cell].Controller.IsOpen, Is.True);
        AssertPartyCells(fixture, new DungeonCell(1, 2));
        Assert.That(fixture.Party[0].Controller.IsTakingAction, Is.False);
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
            configurePartyBeforeInitialization: _ => { }
        );
        uint leaderActions = fixture.Party[0].Controller.ActionPoints;
        uint followerActions = fixture.Party[1].Controller.ActionPoints;

        Assert.That(fixture.Party[0].Controller.IsInDungeonExploration, Is.True);
        Assert.That(fixture.Party[1].Controller.IsInDungeonExploration, Is.False);
        Assert.That(fixture.Runtime.TryOpenDoor(followerDoor.Cell), Is.True);

        AssertDoorOpen(fixture, followerDoor.Cell);
        Assert.That(fixture.Party[0].Controller.ActionPoints, Is.EqualTo(leaderActions));
        Assert.That(fixture.Party[1].Controller.ActionPoints, Is.EqualTo(followerActions));
        Assert.That(fixture.Party[0].Controller.IsInDungeonExploration, Is.True);
        Assert.That(fixture.Party[1].Controller.IsInDungeonExploration, Is.False);
        yield break;
    }

    /// <summary>
    /// Verifies opening the separating door materializes and starts the connected room encounter
    /// before any party member enters that room.
    /// </summary>
    [UnityTest]
    public IEnumerator OpeningDoorImmediatelyStartsEncounterInConnectedRoom()
    {
        DungeonRoom firstRoom = new(1, 1, 1, 9, 9);
        DungeonRoom secondRoom = new(2, 11, 1, 21, 9);
        DoorSpec door = new("reveal-door", new DungeonCell(10, 5));
        DungeonEncounterPlan encounter = new(
            "revealed-encounter",
            secondRoom.Id,
            DungeonEncounterThreat.Low,
            40,
            new[] { new DungeonCell(15, 5) },
            new[] { "goblin-warrior" }
        );
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(9, 0, 5), new Vector3Int(7, 0, 5) },
            doors: new[] { door },
            rooms: new[] { firstRoom, secondRoom },
            encounterPlans: new[] { encounter },
            customGridData: TwoRoomEncounterGrid()
        );

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(
            fixture.Runtime.GetComponentsInChildren<DungeonEncounterMember>(true),
            Is.Empty
        );

        RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), door.Cell);

        DungeonEncounterMember enemy = fixture
            .Runtime.GetComponentsInChildren<DungeonEncounterMember>(true)
            .Single(member => member.IsConfigured);
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(CellOf(enemy.gameObject), Is.EqualTo(new DungeonCell(15, 5)));
        AssertPartyCells(fixture, new DungeonCell(9, 5), new DungeonCell(7, 5));
        yield return null;

        Assert.That(
            manager.IsCombatActive,
            Is.True,
            "Door-revealed combat must remain active before a PC crosses the room boundary."
        );
    }

    /// <summary>
    /// Verifies a door opened during combat immediately adds the connected room's living enemies
    /// to the running encounter before a PC crosses the room boundary.
    /// </summary>
    [UnityTest]
    public IEnumerator OpeningDoorDuringCombatAddsConnectedRoomReinforcements()
    {
        DungeonRoom firstRoom = new(1, 1, 1, 9, 9);
        DungeonRoom secondRoom = new(2, 11, 1, 21, 9);
        DoorSpec door = new("reinforcement-door", new DungeonCell(10, 5));
        DungeonEncounterPlan firstEncounter = new(
            "active-encounter",
            firstRoom.Id,
            DungeonEncounterThreat.Low,
            40,
            new[] { new DungeonCell(5, 5) },
            new[] { "goblin-warrior" }
        );
        DungeonEncounterPlan secondEncounter = new(
            "reinforcement-encounter",
            secondRoom.Id,
            DungeonEncounterThreat.Low,
            40,
            new[] { new DungeonCell(15, 5) },
            new[] { "goblin-warrior" }
        );
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(9, 0, 5), new Vector3Int(7, 0, 5) },
            doors: new[] { door },
            rooms: new[] { firstRoom, secondRoom },
            encounterPlans: new[] { firstEncounter, secondEncounter },
            configurePartyBeforeInitialization: party =>
                party[0].GetComponent<CreatureComponent>().initiative = 1000,
            customGridData: TwoRoomEncounterGrid()
        );
        yield return null;

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            fixture.Runtime.Lifecycle.GetRoomEncounter(secondRoom.Id).State,
            Is.EqualTo(DungeonEncounterGroupState.Dormant)
        );

        RaiseGridCellClick(fixture.Map.GetComponent<GridInput>(), door.Cell);

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            fixture.Runtime.Lifecycle.GetRoomEncounter(secondRoom.Id).State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );
        Assert.That(
            fixture
                .Runtime.GetComponentsInChildren<DungeonEncounterMember>(true)
                .Count(member => member.IsConfigured),
            Is.EqualTo(2)
        );
    }

    /// <summary>
    /// Verifies a follower separated by combat resumes advancing toward the leader's trail on the
    /// first exploration step after the connected encounter is defeated.
    /// </summary>
    [UnityTest]
    public IEnumerator CombatCompletionRestoresSeparatedFollowerCatchUp()
    {
        DungeonRoom firstRoom = new(1, 1, 1, 9, 9);
        DungeonRoom secondRoom = new(2, 11, 1, 21, 9);
        DoorSpec door = new("recovery-door", new DungeonCell(10, 5));
        DungeonEncounterPlan encounter = new(
            "recovery-encounter",
            secondRoom.Id,
            DungeonEncounterThreat.Low,
            40,
            new[] { new DungeonCell(15, 5) },
            new[] { "goblin-warrior" }
        );
        RuntimeFixture fixture = CreateRuntimeFixture(
            new[] { new Vector3Int(9, 0, 5), new Vector3Int(5, 0, 5) },
            doors: new[] { door },
            rooms: new[] { firstRoom, secondRoom },
            encounterPlans: new[] { encounter },
            customGridData: TwoRoomEncounterGrid()
        );
        Combatant leader = fixture.Party[0];

        Assert.That(fixture.Runtime.TryOpenDoor(door.Cell), Is.True);
        DungeonEncounterMember enemy = fixture
            .Runtime.GetComponentsInChildren<DungeonEncounterMember>(true)
            .Single(member => member.IsConfigured);
        Assert.That(manager.IsCombatActive, Is.True);

        CreatureComponent enemyCreature = enemy.GetComponent<CreatureComponent>();
        enemyCreature.ApplyFinalDamage(
            enemyCreature.hp,
            RuleSource.FromSlug("test-follower-recovery-defeat")
        );

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(fixture.Runtime.IsExplorationActive, Is.True);
        Ref<bool> continuePath = ExecuteExplorationStep(fixture, leader, new Vector3Int(8, 0, 5));

        Assert.That(continuePath.Value, Is.True);
        AssertPartyCells(fixture, new DungeonCell(8, 5), new DungeonCell(6, 5));
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
        Combatant current = fixture.Party[0];

        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));
        Assert.That(current.Controller.ActionPoints, Is.EqualTo(3u));
        Assert.That(fixture.Runtime.TryOpenDoor(currentDoor.Cell), Is.True);
        Assert.That(current.Controller.ActionPoints, Is.EqualTo(2u));
        AssertDoorOpen(fixture, currentDoor.Cell);

        Assert.That(
            fixture.Runtime.TryOpenDoor(noncurrentDoor.Cell),
            Is.False,
            "A door adjacent only to a noncurrent PC cannot be opened during another PC's turn."
        );
        Assert.That(fixture.Doors[noncurrentDoor.Cell].Controller.IsOpen, Is.False);
        Assert.That(current.Controller.ActionPoints, Is.EqualTo(2u));

        current.Creature.ApplyFinalDamage(100, RuleSource.FromSlug("test-dead-door-actor"));
        Assert.That(current.Creature.IsDefeated, Is.True);
        Assert.That(fixture.Runtime.TryOpenDoor(deadActorDoor.Cell), Is.False);
        Assert.That(fixture.Doors[deadActorDoor.Cell].Controller.IsOpen, Is.False);
        yield break;
    }

    private RuntimeFixture CreateRuntimeFixture(
        IReadOnlyList<Vector3Int> partyCells,
        int width = 8,
        int height = 8,
        IReadOnlyList<DoorSpec> doors = null,
        IReadOnlyList<DungeonStair> stairs = null,
        IReadOnlyList<DungeonRoom> rooms = null,
        IReadOnlyList<DungeonEncounterPlan> encounterPlans = null,
        Action<IReadOnlyList<TestActionController>> configurePartyBeforeInitialization = null,
        TileType[,] customGridData = null
    )
    {
        doors ??= Array.Empty<DoorSpec>();
        stairs ??= Array.Empty<DungeonStair>();
        rooms ??= Array.Empty<DungeonRoom>();
        encounterPlans ??= Array.Empty<DungeonEncounterPlan>();
        if (partyCells == null || partyCells.Count == 0)
            throw new ArgumentException(
                "A synthetic exploration party is required.",
                nameof(partyCells)
            );

        TileType[,] gridData = customGridData ?? GroundGrid(width, height);
        width = gridData.GetLength(0);
        height = gridData.GetLength(1);
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
            stairs,
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

    private RuntimeFixture CreateSeparatedFollowerEncounterFixture()
    {
        DungeonRoom room = new(1, 2, 2, 3, 3);
        DungeonEncounterPlan encounter = new(
            "separated-follower-boundary",
            room.Id,
            DungeonEncounterThreat.Trivial,
            40,
            new[] { new DungeonCell(3, 3) },
            new[] { "goblin-warrior" }
        );
        return CreateRuntimeFixture(
            new[] { new Vector3Int(4, 0, 2), new Vector3Int(1, 0, 2) },
            rooms: new[] { room },
            encounterPlans: new[] { encounter },
            configurePartyBeforeInitialization: controllers =>
            {
                controllers[0].AddAction(new RulesStrideAction());
                controllers[0].GetComponent<CreatureComponent>().initiative = 1000;
            }
        );
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
        Ref<bool> pathInterrupted = new(false);
        RunToCompletion(
            coordinator.ProjectCommittedStep(
                leader.GameObject,
                Vector3Int.RoundToInt(leader.GameObject.transform.position),
                destination,
                fixture.Grid.GetTiles(),
                movement,
                continuePath,
                pathInterrupted
            )
        );
        if (coordinator is IExplorationPresentationDrain drain)
            RunToCompletion(drain.DrainPresentation(leader.GameObject));
        if (!manager.IsCombatActive)
            leader.Controller.IsTakingAction = false;
        return continuePath;
    }

    private static IEnumerator ExecuteRulesStride(
        Combatant leader,
        RulesStrideAction stride,
        params DungeonCell[] destinations
    )
    {
        Assert.That(destinations, Is.Not.Empty);
        Assert.That(stride.IsAvailable(leader.Controller), Is.True);
        Vector3Int from = Vector3Int.RoundToInt(leader.GameObject.transform.position);
        leader.Controller.TakeAction(
            stride,
            new FixedMovementPathResolver(
                new MovementPath(
                    new GridPosition(from.x, from.y, from.z),
                    destinations.Select(cell => new GridPosition(cell.X, from.y, cell.Z))
                )
            )
        );

        int remainingFrames = 240;
        while (leader.Controller.IsTakingAction && remainingFrames-- > 0)
            yield return null;

        Assert.That(remainingFrames, Is.GreaterThan(0), "The exploration Stride timed out.");
        Assert.That(leader.Controller.IsTakingAction, Is.False);
        DungeonCell expected = destinations[^1];
        Assert.That(CellOf(leader.GameObject), Is.EqualTo(expected));
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

    private static void RaiseGridCellClick(GridInput input, DungeonCell cell)
    {
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

    private static int ChebyshevDistance(Vector3 first, Vector3 second)
    {
        Vector3Int firstCell = Vector3Int.RoundToInt(first);
        Vector3Int secondCell = Vector3Int.RoundToInt(second);
        return Math.Max(Math.Abs(firstCell.x - secondCell.x), Math.Abs(firstCell.z - secondCell.z));
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

    private static TileType[,] CorridorGrid(int width, int height, int corridorZ)
    {
        TileType[,] data = new TileType[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
                data[x, z] = z == corridorZ ? TileType.Ground : TileType.Wall;
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

        internal void ConfigureForTimedTests(float duration)
        {
            ConfigureForSynchronousTests();
            JumpTime = duration;
            PtLerp = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);
        }

        internal void AdvanceTimedPresentation(float deltaTime) =>
            AdvanceExplorationMovements(deltaTime);

        internal void CompletePendingMovement()
        {
            if (Token != null)
            {
                Token.position = EndPoint;
                IsMoving = false;
                CurrentTime = JumpTime;
                Token = null;
            }
            AdvanceExplorationMovements(JumpTime);
        }
    }

    private sealed class TestActionController : ActionController
    {
        internal int StartTurnCount { get; private set; }

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

    private sealed class FixedMovementPathResolver : ISelectionResolver
    {
        private readonly MovementPath path;

        internal FixedMovementPathResolver(MovementPath path) => this.path = path;

        public ValueTask<SelectionOutcome<TSelection>> Select<TSelection>(
            ActionSelectionRequest<TSelection> request,
            CancellationToken cancellationToken
        )
        {
            if (cancellationToken.IsCancellationRequested)
                return new ValueTask<SelectionOutcome<TSelection>>(
                    SelectionOutcome<TSelection>.Cancelled
                );
            if (
                request is not StridePathSelectionRequest
                || typeof(TSelection) != typeof(MovementPath)
            )
            {
                return new ValueTask<SelectionOutcome<TSelection>>(
                    SelectionOutcome<TSelection>.Invalid("Expected a Stride path request.")
                );
            }

            SelectionOutcome<MovementPath> completed = SelectionOutcome<MovementPath>.Completed(
                path
            );
            return new ValueTask<SelectionOutcome<TSelection>>(
                (SelectionOutcome<TSelection>)(object)completed
            );
        }
    }

    private sealed class RecordingExplorationPresentation
        : IDungeonExplorationPresentation,
            IDungeonTacticsPresentation
    {
        private Func<ActionController, bool> trySelectLeader = _ => false;
        private Action enterTactics = delegate { };

        internal ActionController Selected { get; private set; }

        internal bool TrySelect(ActionController candidate)
        {
            bool selected = trySelectLeader(candidate);
            if (selected)
                Selected = candidate;
            return selected;
        }

        internal void RequestTactics() => enterTactics.Invoke();

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

        /// <inheritdoc/>
        public void ConfigureTacticsControl(Action enterTactics, Func<bool> returnToExploration) =>
            this.enterTactics = enterTactics;

        /// <inheritdoc/>
        public void ShowTactics() { }

        /// <inheritdoc/>
        public void ShowTacticsUnavailable() => enterTactics = delegate { };
    }

    private sealed class RuntimeTestCombatLog : CombatLogInterface
    {
        internal List<string> Messages { get; } = new();

        /// <inheritdoc/>
        public override void DevMode() { }

        /// <inheritdoc/>
        public override void ReleaseMode() { }

        /// <inheritdoc/>
        public override void AddWhiteList(string tag) { }

        /// <inheritdoc/>
        public override void AddBlackList(string tag) { }

        /// <inheritdoc/>
        public override void Log(string msg) => Messages.Add(msg);

        /// <inheritdoc/>
        public override void DevLog(string msg) { }

        /// <inheritdoc/>
        public override void DevLog(string msg, string tag) { }

        /// <inheritdoc/>
        public override void DevLog(string msg, List<string> tags) { }

        /// <inheritdoc/>
        public override void Log(string msg, string tag) => Messages.Add(msg);

        /// <inheritdoc/>
        public override void Log(string msg, List<string> tags) => Messages.Add(msg);

        /// <inheritdoc/>
        public override List<string> GetMessages() => new();
    }

    private static TileType[,] TwoRoomGrid()
    {
        TileType[,] data = new TileType[23, 11];
        for (int x = 0; x < data.GetLength(0); x++)
        {
            for (int z = 0; z < data.GetLength(1); z++)
                data[x, z] = TileType.Wall;
        }
        FillRoom(1, 9, 1, 9);
        FillRoom(11, 21, 1, 9);
        data[10, 5] = TileType.ClosedDoor;
        return data;

        void FillRoom(int minimumX, int maximumX, int minimumZ, int maximumZ)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                    data[x, z] = TileType.Ground;
            }
        }
    }

    private static TileType[,] ThreeRoomGrid()
    {
        TileType[,] data = new TileType[16, 7];
        for (int x = 0; x < data.GetLength(0); x++)
        {
            for (int z = 0; z < data.GetLength(1); z++)
                data[x, z] = TileType.Wall;
        }
        FillRoom(1, 4);
        FillRoom(6, 9);
        FillRoom(11, 14);
        data[5, 3] = TileType.ClosedDoor;
        data[10, 3] = TileType.ClosedDoor;
        return data;

        void FillRoom(int minimumX, int maximumX)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                for (int z = 1; z <= 5; z++)
                    data[x, z] = TileType.Ground;
            }
        }
    }

    private static TileType[,] TwoRoomEncounterGrid()
    {
        TileType[,] data = TwoRoomGrid();
        data[data.GetLength(0) - 1, data.GetLength(1) - 1] = TileType.Ground;
        return data;
    }
}
