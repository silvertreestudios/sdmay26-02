using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using NUnit.Framework;

/// <summary>Verifies deterministic room-encounter lifecycle and snapshot rules.</summary>
public sealed class DungeonEncounterStateMachineTests
{
    /// <summary>Verifies pristine construction, stable ordering, and resolved-plan handling.</summary>
    [Test]
    public void ConstructionCreatesStablePlanOrderedInstancesAndHonorsResolvedPlans()
    {
        DungeonEncounterPlan unresolved = Plan("encounter-b", 2, "goblin", "goblin");
        DungeonEncounterPlan resolved = Plan("encounter-a", 1, true, "skeleton");

        DungeonEncounterStateMachine machine = new(new[] { unresolved, resolved });

        Assert.That(
            machine.Encounters.Select(group => group.Plan.Id),
            Is.EqualTo(new[] { "encounter-a", "encounter-b" })
        );
        Assert.That(
            machine.GetEncounter("encounter-a").State,
            Is.EqualTo(DungeonEncounterGroupState.Cleared)
        );
        Assert.That(machine.GetEncounter("encounter-a").Creatures.Single().IsDefeated, Is.True);
        Assert.That(
            machine.GetEncounter("encounter-b").State,
            Is.EqualTo(DungeonEncounterGroupState.Dormant)
        );
        Assert.That(
            machine.GetEncounter("encounter-b").Creatures.Select(creature => creature.InstanceId),
            Is.EqualTo(new[] { "encounter-b/creature-0000", "encounter-b/creature-0001" })
        );
        Assert.That(machine.HasActiveEncounters, Is.False);
    }

    /// <summary>Verifies first activation and repeated-entry no-op behavior.</summary>
    [Test]
    public void FirstRoomEntryStartsCombatAndRepeatedEntryIsExplicitNoOp()
    {
        DungeonEncounterStateMachine machine = new(new[] { Plan("encounter-1", 1, "goblin") });

        DungeonRoomEntryResult first = machine.EnterRoom(1);
        DungeonRoomEntryResult repeated = machine.EnterRoom(1);

        Assert.That(first.Transition, Is.EqualTo(DungeonRoomEntryTransition.FirstActivation));
        Assert.That(first.StartsCombat, Is.True);
        Assert.That(first.JoinsRunningCombat, Is.False);
        Assert.That(first.CompletedImmediately, Is.False);
        Assert.That(first.Encounter.State, Is.EqualTo(DungeonEncounterGroupState.Active));
        Assert.That(repeated.Transition, Is.EqualTo(DungeonRoomEntryTransition.AlreadyActive));
        Assert.That(repeated.StartsCombat, Is.False);
        Assert.That(repeated.JoinsRunningCombat, Is.False);
        Assert.That(machine.ActiveEncounterIds, Is.EqualTo(new[] { "encounter-1" }));
    }

    /// <summary>Verifies dormant rooms reinforce an already active encounter.</summary>
    [Test]
    public void DormantRoomEnteredDuringCombatBecomesReinforcement()
    {
        DungeonEncounterStateMachine machine = new(
            new[] { Plan("encounter-1", 1, "goblin"), Plan("encounter-2", 2, "kobold") }
        );
        machine.EnterRoom(1);

        DungeonRoomEntryResult result = machine.EnterRoom(2);

        Assert.That(result.Transition, Is.EqualTo(DungeonRoomEntryTransition.Reinforcement));
        Assert.That(result.StartsCombat, Is.False);
        Assert.That(result.JoinsRunningCombat, Is.True);
        Assert.That(machine.ActiveEncounterIds, Is.EqualTo(new[] { "encounter-1", "encounter-2" }));
    }

    /// <summary>Verifies suspended groups either restart or join combat based on active peers.</summary>
    [Test]
    public void SuspendedRoomEntryResumesAndReportsWhetherCombatWasAlreadyRunning()
    {
        DungeonEncounterStateMachine machine = new(
            new[]
            {
                Plan("encounter-1", 1, "goblin"),
                Plan("encounter-2", 2, "kobold"),
                Plan("encounter-3", 3, "skeleton"),
            }
        );
        machine.EnterRoom(1);
        machine.EnterRoom(2);
        machine.SuspendIfPartyOutsideActiveRegions(2, Array.Empty<int>());

        DungeonRoomEntryResult startsCombat = machine.EnterRoom(1);
        DungeonRoomEntryResult joinsCombat = machine.EnterRoom(2);

        Assert.That(startsCombat.Transition, Is.EqualTo(DungeonRoomEntryTransition.Resume));
        Assert.That(startsCombat.StartsCombat, Is.True);
        Assert.That(startsCombat.JoinsRunningCombat, Is.False);
        Assert.That(joinsCombat.Transition, Is.EqualTo(DungeonRoomEntryTransition.Resume));
        Assert.That(joinsCombat.StartsCombat, Is.False);
        Assert.That(joinsCombat.JoinsRunningCombat, Is.True);
        Assert.That(
            machine.GetRoomEncounter(3).State,
            Is.EqualTo(DungeonEncounterGroupState.Dormant)
        );
    }

    /// <summary>Verifies empty plans clear without creating combat.</summary>
    [Test]
    public void EmptyPlanClearsOnEntryWithoutStartingOrJoiningCombat()
    {
        DungeonEncounterStateMachine alone = new(new[] { Plan("empty", 1) });
        DungeonRoomEntryResult first = alone.EnterRoom(1);

        DungeonEncounterStateMachine alongside = new(
            new[] { Plan("active", 1, "goblin"), Plan("empty", 2) }
        );
        alongside.EnterRoom(1);
        DungeonRoomEntryResult reinforcement = alongside.EnterRoom(2);

        Assert.That(first.Transition, Is.EqualTo(DungeonRoomEntryTransition.FirstActivation));
        Assert.That(first.CompletedImmediately, Is.True);
        Assert.That(first.StartsCombat, Is.False);
        Assert.That(first.Encounter.State, Is.EqualTo(DungeonEncounterGroupState.Cleared));
        Assert.That(reinforcement.Transition, Is.EqualTo(DungeonRoomEntryTransition.Reinforcement));
        Assert.That(reinforcement.CompletedImmediately, Is.True);
        Assert.That(reinforcement.JoinsRunningCombat, Is.False);
        Assert.That(alongside.ActiveEncounterIds, Is.EqualTo(new[] { "active" }));
    }

    /// <summary>Verifies cleared groups cannot reactivate.</summary>
    [Test]
    public void ClearedRoomEntryCanNeverReactivateItsGroup()
    {
        DungeonEncounterStateMachine machine = new(new[] { Plan("encounter-1", 1, "goblin") });
        DungeonEncounterCreatureView creature = machine
            .EnterRoom(1)
            .Encounter.LivingCreatures.Single();
        machine.MarkCreatureDefeated(creature.InstanceId);

        DungeonRoomEntryResult result = machine.EnterRoom(1);

        Assert.That(result.Transition, Is.EqualTo(DungeonRoomEntryTransition.Cleared));
        Assert.That(result.StartsCombat, Is.False);
        Assert.That(result.JoinsRunningCombat, Is.False);
        Assert.That(result.Encounter.State, Is.EqualTo(DungeonEncounterGroupState.Cleared));
        Assert.That(result.Encounter.LivingCreatures, Is.Empty);
    }

    /// <summary>Verifies partial defeats, per-group clearing, and final active completion.</summary>
    [Test]
    public void PartialDefeatsClearOnlyTheirGroupAndFinalActiveGroupCompletesCombat()
    {
        DungeonEncounterStateMachine machine = new(
            new[] { Plan("encounter-1", 1, "goblin", "kobold"), Plan("encounter-2", 2, "skeleton") }
        );
        DungeonEncounterGroupView first = machine.EnterRoom(1).Encounter;
        DungeonEncounterGroupView second = machine.EnterRoom(2).Encounter;

        DungeonCreatureDefeatResult partial = machine.MarkCreatureDefeated(
            first.Creatures[0].InstanceId
        );
        DungeonCreatureDefeatResult firstClear = machine.MarkCreatureDefeated(
            first.Creatures[1].InstanceId
        );
        DungeonCreatureDefeatResult finalClear = machine.MarkCreatureDefeated(
            second.Creatures[0].InstanceId
        );

        Assert.That(partial.RemainingLivingCreatureCount, Is.EqualTo(1));
        Assert.That(partial.GroupCleared, Is.False);
        Assert.That(partial.CurrentCombatCompleted, Is.False);
        Assert.That(firstClear.GroupCleared, Is.True);
        Assert.That(firstClear.CurrentCombatCompleted, Is.False);
        Assert.That(
            machine.GetEncounter("encounter-1").State,
            Is.EqualTo(DungeonEncounterGroupState.Cleared)
        );
        Assert.That(finalClear.GroupCleared, Is.True);
        Assert.That(finalClear.CurrentCombatCompleted, Is.True);
        Assert.That(machine.HasActiveEncounters, Is.False);
    }

    /// <summary>Verifies suspended groups do not hold an unrelated active fight open.</summary>
    [Test]
    public void SuspendedGroupsElsewhereDoNotPreventFinalActiveGroupCompletion()
    {
        DungeonEncounterStateMachine machine = new(
            new[] { Plan("encounter-1", 1, "goblin"), Plan("encounter-2", 2, "skeleton") }
        );
        machine.EnterRoom(1);
        machine.SuspendIfPartyOutsideActiveRegions(1, Array.Empty<int>());
        DungeonEncounterCreatureView active = machine.EnterRoom(2).Encounter.Creatures.Single();

        DungeonCreatureDefeatResult result = machine.MarkCreatureDefeated(active.InstanceId);

        Assert.That(result.CurrentCombatCompleted, Is.True);
        Assert.That(
            machine.GetEncounter("encounter-1").State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );
    }

    /// <summary>Verifies any PC remaining in an active region prevents suspension.</summary>
    [Test]
    public void PartyRemainingInAnyActiveRegionPreventsSuspension()
    {
        DungeonEncounterStateMachine machine = new(
            new[] { Plan("encounter-1", 1, "goblin"), Plan("encounter-2", 2, "skeleton") }
        );
        machine.EnterRoom(1);
        machine.EnterRoom(2);

        DungeonEncounterSuspensionResult result = machine.SuspendIfPartyOutsideActiveRegions(
            3,
            new[] { 99, 2 }
        );

        Assert.That(
            result.Transition,
            Is.EqualTo(DungeonEncounterSuspensionTransition.RemainedActive)
        );
        Assert.That(result.SuspendedEncounterIds, Is.Empty);
        Assert.That(machine.ActiveEncounterIds, Is.EqualTo(new[] { "encounter-1", "encounter-2" }));
    }

    /// <summary>Verifies leaving every active region suspends all participating groups.</summary>
    [Test]
    public void PartyLeavingAllActiveRegionsSuspendsEveryActiveGroup()
    {
        DungeonEncounterStateMachine machine = new(
            new[] { Plan("encounter-b", 2, "goblin"), Plan("encounter-a", 1, "skeleton") }
        );
        machine.EnterRoom(1);
        machine.EnterRoom(2);

        DungeonEncounterSuspensionResult result = machine.SuspendIfPartyOutsideActiveRegions(
            2,
            new[] { 99 }
        );

        Assert.That(result.Transition, Is.EqualTo(DungeonEncounterSuspensionTransition.Suspended));
        Assert.That(
            result.SuspendedEncounterIds,
            Is.EqualTo(new[] { "encounter-a", "encounter-b" })
        );
        Assert.That(
            machine.Encounters.All(group => group.State == DungeonEncounterGroupState.Suspended),
            Is.True
        );
        Assert.That(machine.HasActiveEncounters, Is.False);
    }

    /// <summary>Verifies dormant, unknown, and repeated defeat notifications preserve state.</summary>
    [Test]
    public void InvalidDefeatTransitionsThrowWithoutMutatingState()
    {
        DungeonEncounterStateMachine dormant = new(new[] { Plan("encounter", 1, "goblin") });
        string id = dormant.GetEncounter("encounter").Creatures.Single().InstanceId;
        Assert.Throws<InvalidOperationException>(() => dormant.MarkCreatureDefeated(id));
        Assert.Throws<KeyNotFoundException>(() =>
            dormant.MarkCreatureDefeated("unknown/creature-0000")
        );

        DungeonEncounterStateMachine active = new(
            new[] { Plan("encounter", 1, "goblin", "kobold") }
        );
        string first = active.EnterRoom(1).Encounter.Creatures[0].InstanceId;
        active.MarkCreatureDefeated(first);
        Assert.Throws<InvalidOperationException>(() => active.MarkCreatureDefeated(first));

        Assert.That(active.GetEncounter("encounter").LivingCreatures, Has.Count.EqualTo(1));
    }

    /// <summary>Verifies targetable suspended members can be defeated without ending another fight.</summary>
    [Test]
    public void SuspendedCreatureDefeatClearsItsGroupWithoutCompletingActiveCombat()
    {
        DungeonEncounterStateMachine machine = new(
            new[] { Plan("encounter-1", 1, "goblin"), Plan("encounter-2", 2, "skeleton") }
        );
        machine.EnterRoom(1);
        machine.EnterRoom(2);
        machine.SuspendIfPartyOutsideActiveRegions(1, Array.Empty<int>());
        machine.EnterRoom(1);
        string suspendedCreatureId = machine
            .GetEncounter("encounter-2")
            .Creatures.Single()
            .InstanceId;

        DungeonCreatureDefeatResult result = machine.MarkCreatureDefeated(suspendedCreatureId);

        Assert.That(result.GroupCleared, Is.True);
        Assert.That(result.CurrentCombatCompleted, Is.False);
        Assert.That(
            machine.GetEncounter("encounter-2").State,
            Is.EqualTo(DungeonEncounterGroupState.Cleared)
        );
        Assert.That(machine.HasActiveEncounters, Is.True);
    }

    /// <summary>Verifies malformed room-entry and suspension observations fail explicitly.</summary>
    [Test]
    public void InvalidRoomAndSuspensionRequestsThrowExplicitly()
    {
        DungeonEncounterStateMachine machine = new(new[] { Plan("encounter", 1, "goblin") });

        Assert.Throws<ArgumentOutOfRangeException>(() => machine.EnterRoom(0));
        Assert.Throws<KeyNotFoundException>(() => machine.EnterRoom(2));
        Assert.Throws<KeyNotFoundException>(() => machine.GetEncounter("unknown"));
        Assert.Throws<InvalidOperationException>(() =>
            machine.SuspendIfPartyOutsideActiveRegions(1, Array.Empty<int>())
        );

        machine.EnterRoom(1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            machine.SuspendIfPartyOutsideActiveRegions(0, Array.Empty<int>())
        );
        Assert.Throws<ArgumentNullException>(() =>
            machine.SuspendIfPartyOutsideActiveRegions(1, null)
        );
        Assert.Throws<ArgumentException>(() =>
            machine.SuspendIfPartyOutsideActiveRegions(1, new[] { 0 })
        );
        Assert.That(
            machine.GetEncounter("encounter").State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );
    }

    /// <summary>Verifies every lifecycle phase and partial casualty survives restoration.</summary>
    [Test]
    public void SnapshotRoundTripPreservesDormantActiveSuspendedClearedAndPartialDefeats()
    {
        DungeonEncounterPlan[] plans =
        {
            Plan("encounter-1", 1, "goblin", "kobold"),
            Plan("encounter-2", 2, "skeleton"),
            Plan("encounter-3", 3, "zombie"),
            Plan("encounter-4", 4, true, "goblin"),
        };
        DungeonEncounterStateMachine source = new(plans);
        DungeonEncounterGroupView first = source.EnterRoom(1).Encounter;
        source.MarkCreatureDefeated(first.Creatures[0].InstanceId);
        source.SuspendIfPartyOutsideActiveRegions(2, Array.Empty<int>());
        source.EnterRoom(2);

        DungeonEncounterLifecycleSnapshot snapshot = source.CaptureSnapshot();
        DungeonEncounterStateMachine restored = DungeonEncounterStateMachine.Restore(
            plans,
            snapshot
        );

        Assert.That(
            restored.CaptureSnapshot().Groups.Select(Describe),
            Is.EqualTo(snapshot.Groups.Select(Describe))
        );
        Assert.That(
            restored.GetEncounter("encounter-1").State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );
        Assert.That(restored.GetEncounter("encounter-1").LivingCreatures, Has.Count.EqualTo(1));
        Assert.That(
            restored.GetEncounter("encounter-2").State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );
        Assert.That(
            restored.GetEncounter("encounter-3").State,
            Is.EqualTo(DungeonEncounterGroupState.Dormant)
        );
        Assert.That(
            restored.GetEncounter("encounter-4").State,
            Is.EqualTo(DungeonEncounterGroupState.Cleared)
        );
    }

    /// <summary>Verifies restored active groups become resumable exploration state.</summary>
    [Test]
    public void NormalizeActiveGroupsForExplorationRestoreSuspendsOnlyActiveGroups()
    {
        DungeonEncounterPlan[] plans =
        {
            Plan("encounter-1", 1, "goblin"),
            Plan("encounter-2", 2, "kobold"),
            Plan("encounter-3", 3, true, "skeleton"),
        };
        DungeonEncounterStateMachine machine = new(plans);
        machine.EnterRoom(1);

        IReadOnlyList<string> normalized = machine.NormalizeActiveGroupsForExplorationRestore();

        Assert.That(normalized, Is.EqualTo(new[] { "encounter-1" }));
        Assert.That(
            machine.GetEncounter("encounter-1").State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );
        Assert.That(
            machine.GetEncounter("encounter-2").State,
            Is.EqualTo(DungeonEncounterGroupState.Dormant)
        );
        Assert.That(
            machine.GetEncounter("encounter-3").State,
            Is.EqualTo(DungeonEncounterGroupState.Cleared)
        );
    }

    /// <summary>Verifies restoration rejects missing, extra, and inconsistent group state.</summary>
    [Test]
    public void RestoreRejectsSnapshotsThatDoNotExactlyMatchPlans()
    {
        DungeonEncounterPlan[] plans = { Plan("encounter-1", 1, "goblin") };
        DungeonEncounterLifecycleSnapshot missing = new(
            Array.Empty<DungeonEncounterGroupSnapshot>()
        );
        DungeonEncounterLifecycleSnapshot unknown = new(
            new[]
            {
                new DungeonEncounterGroupSnapshot(
                    "other",
                    DungeonEncounterGroupState.Dormant,
                    Array.Empty<string>()
                ),
            }
        );

        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.Restore(plans, missing)
        );
        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.Restore(plans, unknown)
        );
        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.Restore(
                plans,
                Snapshot("encounter-1", DungeonEncounterGroupState.Active, "unknown")
            )
        );
        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.Restore(
                plans,
                Snapshot(
                    "encounter-1",
                    DungeonEncounterGroupState.Dormant,
                    "encounter-1/creature-0000"
                )
            )
        );
        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.Restore(
                plans,
                Snapshot("encounter-1", DungeonEncounterGroupState.Cleared)
            )
        );
        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.Restore(
                plans,
                Snapshot(
                    "encounter-1",
                    DungeonEncounterGroupState.Active,
                    "encounter-1/creature-0000"
                )
            )
        );
    }

    /// <summary>Verifies persisted state cannot reopen a generation-resolved plan.</summary>
    [Test]
    public void RestoreCannotReopenResolvedPlan()
    {
        DungeonEncounterPlan[] plans = { Plan("encounter", 1, true, "goblin") };

        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.Restore(
                plans,
                Snapshot("encounter", DungeonEncounterGroupState.Dormant)
            )
        );
    }

    /// <summary>Verifies constructors reject malformed plans and snapshots.</summary>
    [Test]
    public void ConstructionAndSnapshotContractsRejectMalformedInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new DungeonEncounterStateMachine(null));
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterStateMachine(new DungeonEncounterPlan[] { null })
        );
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterStateMachine(new[] { Plan(" ", 1, "goblin") })
        );
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterStateMachine(
                new[] { Plan("duplicate", 1, "goblin"), Plan("duplicate", 2, "kobold") }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterStateMachine(
                new[] { Plan("encounter-1", 1, "goblin"), Plan("encounter-2", 1, "kobold") }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterStateMachine(
                new[]
                {
                    new DungeonEncounterPlan(
                        "encounter",
                        1,
                        DungeonEncounterThreat.Trivial,
                        40,
                        Array.Empty<DungeonCell>(),
                        new[] { "goblin" }
                    ),
                }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterGroupSnapshot(
                " ",
                DungeonEncounterGroupState.Dormant,
                Array.Empty<string>()
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterGroupSnapshot(
                "encounter",
                DungeonEncounterGroupState.Dormant,
                new[] { "duplicate", "duplicate" }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            new DungeonEncounterLifecycleSnapshot(
                new[]
                {
                    new DungeonEncounterGroupSnapshot(
                        "encounter",
                        DungeonEncounterGroupState.Dormant,
                        Array.Empty<string>()
                    ),
                    new DungeonEncounterGroupSnapshot(
                        "encounter",
                        DungeonEncounterGroupState.Dormant,
                        Array.Empty<string>()
                    ),
                }
            )
        );
    }

    /// <summary>Verifies snapshots own immutable copies of caller collections.</summary>
    [Test]
    public void SnapshotCopiesCallerCollections()
    {
        List<string> defeated = new() { "encounter/creature-0000" };
        DungeonEncounterGroupSnapshot group = new(
            "encounter",
            DungeonEncounterGroupState.Cleared,
            defeated
        );
        List<DungeonEncounterGroupSnapshot> groups = new() { group };
        DungeonEncounterLifecycleSnapshot snapshot = new(groups);

        defeated.Clear();
        groups.Clear();

        Assert.That(
            group.DefeatedCreatureInstanceIds,
            Is.EqualTo(new[] { "encounter/creature-0000" })
        );
        Assert.That(snapshot.Groups, Has.Count.EqualTo(1));
    }

    /// <summary>Verifies stable instance IDs require non-blank encounter identity.</summary>
    /// <param name="encounterId">The invalid identifier under test.</param>
    [TestCase("")]
    [TestCase(" ")]
    public void StableInstanceIdRejectsBlankEncounterId(string encounterId)
    {
        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterStateMachine.CreateCreatureInstanceId(encounterId, 0)
        );
    }

    /// <summary>Verifies stable instance IDs reject negative plan indexes.</summary>
    [Test]
    public void StableInstanceIdRejectsNegativeCreatureIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonEncounterStateMachine.CreateCreatureInstanceId("encounter", -1)
        );
    }

    /// <summary>Verifies persisted live and defeated identities restore a suspended group.</summary>
    [Test]
    public void RuntimeStateSnapshotRestoresPartialEncounterProgress()
    {
        DungeonEncounterPlan plan = Plan("encounter-a", 1, "goblin", "goblin");
        DungeonRuntimeState runtimeState = new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "encounter-a/creature-0000" },
            new[]
            {
                new DungeonCreatureRuntimeState(
                    "encounter-a/creature-0001",
                    "goblin",
                    "encounter-a",
                    new DungeonCell(12, 1),
                    3,
                    "child-state"
                ),
            }
        );

        DungeonEncounterLifecycleSnapshot snapshot =
            DungeonEncounterLifecycleSnapshot.FromRuntimeState(new[] { plan }, runtimeState);

        Assert.That(snapshot.Groups, Has.Count.EqualTo(1));
        Assert.That(snapshot.Groups[0].State, Is.EqualTo(DungeonEncounterGroupState.Suspended));
        Assert.That(
            snapshot.Groups[0].DefeatedCreatureInstanceIds,
            Is.EqualTo(new[] { "encounter-a/creature-0000" })
        );
        DungeonEncounterStateMachine restored = DungeonEncounterStateMachine.Restore(
            new[] { plan },
            snapshot
        );
        Assert.That(restored.GetEncounter("encounter-a").LivingCreatures, Has.Count.EqualTo(1));
    }

    /// <summary>Verifies resolved runtime state explicitly persists every planned defeat.</summary>
    [Test]
    public void RuntimeStateSnapshotRejectsResolvedGroupWithoutCompleteDefeatedState()
    {
        DungeonEncounterPlan plan = Plan("encounter-a", 1, true, "goblin", "goblin");
        DungeonRuntimeState runtimeState = new(
            Array.Empty<string>(),
            new[] { "encounter-a" },
            new[] { "encounter-a/creature-0000" }
        );

        Assert.Throws<ArgumentException>(() =>
            DungeonEncounterLifecycleSnapshot.FromRuntimeState(new[] { plan }, runtimeState)
        );
    }

    private static DungeonEncounterPlan Plan(string id, int roomId, params string[] creatureIds) =>
        Plan(id, roomId, false, creatureIds);

    private static DungeonEncounterPlan Plan(
        string id,
        int roomId,
        bool resolved,
        params string[] creatureIds
    )
    {
        DungeonCell[] spawnCells = creatureIds
            .Select((_, index) => new DungeonCell(roomId * 10 + index, roomId))
            .ToArray();
        return new DungeonEncounterPlan(
            id,
            roomId,
            DungeonEncounterThreat.Trivial,
            40,
            spawnCells,
            creatureIds,
            resolved
        );
    }

    private static DungeonEncounterLifecycleSnapshot Snapshot(
        string encounterId,
        DungeonEncounterGroupState state,
        params string[] defeatedCreatureInstanceIds
    ) =>
        new(
            new[]
            {
                new DungeonEncounterGroupSnapshot(encounterId, state, defeatedCreatureInstanceIds),
            }
        );

    private static string Describe(DungeonEncounterGroupSnapshot group) =>
        group.EncounterId
        + ":"
        + group.State
        + ":"
        + string.Join(",", group.DefeatedCreatureInstanceIds);
}
