using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.Combat.Encounters
{
    /// <summary>
    /// Owns deterministic, Unity-independent room-encounter lifecycle state for one dungeon floor.
    /// </summary>
    /// <remarks>
    /// This type does not roll initiative, instantiate creatures, observe transforms, or dispatch UI
    /// events. Unity adapters perform those effects from the explicit transition results while this
    /// state machine preserves the rules that persistence and tests must share.
    /// </remarks>
    public sealed class DungeonEncounterStateMachine
    {
        private readonly EncounterGroup[] orderedGroups;
        private readonly Dictionary<string, EncounterGroup> groupsByEncounterId;
        private readonly Dictionary<int, EncounterGroup> groupsByRoomId;
        private readonly Dictionary<string, CreatureEntry> creaturesByInstanceId;
        private readonly DungeonEncounterGroupView[] encounterViews;
        private readonly IReadOnlyList<DungeonEncounterGroupView> readOnlyEncounterViews;

        /// <summary>Creates pristine lifecycle state from immutable generated encounter plans.</summary>
        /// <param name="plans">Unique room encounter plans for one floor.</param>
        /// <exception cref="ArgumentNullException"><paramref name="plans"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// A plan is null or has invalid, duplicate, or internally inconsistent identifiers.
        /// </exception>
        public DungeonEncounterStateMachine(IEnumerable<DungeonEncounterPlan> plans)
        {
            if (plans == null)
                throw new ArgumentNullException(nameof(plans));

            DungeonEncounterPlan[] copied = plans.ToArray();
            ValidatePlans(copied);
            DungeonEncounterPlan[] orderedPlans = copied
                .OrderBy(plan => plan.Id, StringComparer.Ordinal)
                .ToArray();
            orderedGroups = new EncounterGroup[orderedPlans.Length];
            for (int index = 0; index < orderedPlans.Length; index++)
                orderedGroups[index] = CreateInitialGroup(orderedPlans[index], index);
            groupsByEncounterId = orderedGroups.ToDictionary(
                group => group.Plan.Id,
                StringComparer.Ordinal
            );
            groupsByRoomId = orderedGroups.ToDictionary(group => group.Plan.RoomId);
            creaturesByInstanceId = orderedGroups
                .SelectMany(group => group.Creatures)
                .ToDictionary(creature => creature.InstanceId, StringComparer.Ordinal);
            encounterViews = orderedGroups.Select(CreateView).ToArray();
            readOnlyEncounterViews = Array.AsReadOnly(encounterViews);
        }

        /// <summary>Gets cached immutable views of all groups ordered by stable encounter ID.</summary>
        /// <remarks>Only the view for a group changed by a lifecycle transition is replaced.</remarks>
        public IReadOnlyList<DungeonEncounterGroupView> Encounters => readOnlyEncounterViews;

        /// <summary>Gets whether at least one group participates in the current combat.</summary>
        public bool HasActiveEncounters
        {
            get
            {
                foreach (EncounterGroup group in orderedGroups)
                {
                    if (group.State == DungeonEncounterGroupState.Active)
                        return true;
                }
                return false;
            }
        }

        /// <summary>Gets active encounter IDs ordered deterministically.</summary>
        public IReadOnlyList<string> ActiveEncounterIds =>
            Array.AsReadOnly(
                orderedGroups
                    .Where(group => group.State == DungeonEncounterGroupState.Active)
                    .Select(group => group.Plan.Id)
                    .ToArray()
            );

        /// <summary>Creates the stable ID for one zero-based creature position in a plan.</summary>
        /// <param name="encounterId">The stable non-empty encounter ID.</param>
        /// <param name="creatureIndex">The zero-based creature index in plan order.</param>
        /// <returns>An ID stable across repeated construction and snapshot restoration.</returns>
        /// <exception cref="ArgumentException"><paramref name="encounterId"/> is blank.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="creatureIndex"/> is negative.</exception>
        public static string CreateCreatureInstanceId(string encounterId, int creatureIndex)
        {
            return DungeonCreatureInstanceIdentity.Create(encounterId, creatureIndex);
        }

        /// <summary>Gets one encounter by stable encounter ID.</summary>
        /// <param name="encounterId">The stable non-empty encounter ID.</param>
        /// <returns>An immutable view of the current group state.</returns>
        /// <exception cref="ArgumentException"><paramref name="encounterId"/> is blank.</exception>
        /// <exception cref="KeyNotFoundException">No plan uses the requested encounter ID.</exception>
        public DungeonEncounterGroupView GetEncounter(string encounterId)
        {
            if (string.IsNullOrWhiteSpace(encounterId))
                throw new ArgumentException("An encounter ID is required.", nameof(encounterId));
            if (!groupsByEncounterId.TryGetValue(encounterId, out EncounterGroup group))
                throw new KeyNotFoundException($"Encounter '{encounterId}' is not registered.");
            return encounterViews[group.Index];
        }

        /// <summary>Gets one encounter by its unique positive room ID.</summary>
        /// <param name="roomId">The positive room ID.</param>
        /// <returns>An immutable view of the current group state.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="roomId"/> is not positive.</exception>
        /// <exception cref="KeyNotFoundException">No encounter plan belongs to the requested room.</exception>
        public DungeonEncounterGroupView GetRoomEncounter(int roomId)
        {
            if (roomId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomId));
            if (!groupsByRoomId.TryGetValue(roomId, out EncounterGroup group))
                throw new KeyNotFoundException($"Room {roomId} has no encounter plan.");
            return encounterViews[group.Index];
        }

        /// <summary>
        /// Processes a living PC entering a room and reports whether to start, reinforce, resume,
        /// or leave combat unchanged.
        /// </summary>
        /// <param name="roomId">The positive room ID entered by a living PC.</param>
        /// <returns>A complete transition result for Unity-side materialization and scheduling.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="roomId"/> is not positive.</exception>
        /// <exception cref="KeyNotFoundException">No encounter plan belongs to the requested room.</exception>
        public DungeonRoomEntryResult EnterRoom(int roomId)
        {
            if (roomId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomId));
            if (!groupsByRoomId.TryGetValue(roomId, out EncounterGroup group))
                throw new KeyNotFoundException($"Room {roomId} has no encounter plan.");

            bool hadActiveEncounters = HasActiveEncounters;
            DungeonRoomEntryTransition transition;
            bool changedToActive = false;
            bool completedImmediately = false;
            switch (group.State)
            {
                case DungeonEncounterGroupState.Dormant:
                    transition = hadActiveEncounters
                        ? DungeonRoomEntryTransition.Reinforcement
                        : DungeonRoomEntryTransition.FirstActivation;
                    if (LivingCreatureCount(group) == 0)
                    {
                        group.State = DungeonEncounterGroupState.Cleared;
                        completedImmediately = true;
                    }
                    else
                    {
                        group.State = DungeonEncounterGroupState.Active;
                        changedToActive = true;
                    }
                    break;
                case DungeonEncounterGroupState.Suspended:
                    transition = DungeonRoomEntryTransition.Resume;
                    group.State = DungeonEncounterGroupState.Active;
                    changedToActive = true;
                    break;
                case DungeonEncounterGroupState.Active:
                    transition = DungeonRoomEntryTransition.AlreadyActive;
                    break;
                case DungeonEncounterGroupState.Cleared:
                    transition = DungeonRoomEntryTransition.Cleared;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Encounter '{group.Plan.Id}' has undefined state '{group.State}'."
                    );
            }

            DungeonEncounterGroupView encounterView =
                changedToActive || completedImmediately
                    ? RefreshEncounterView(group)
                    : encounterViews[group.Index];
            return new DungeonRoomEntryResult(
                encounterView,
                transition,
                changedToActive && !hadActiveEncounters,
                changedToActive && hadActiveEncounters,
                completedImmediately
            );
        }

        /// <summary>
        /// Suspends every active group only when no living PC occupies an active group's room.
        /// </summary>
        /// <param name="livingPcCount">The positive total number of living PCs.</param>
        /// <param name="livingPcRoomIds">
        /// Distinct or repeated room IDs occupied by living PCs; omit PCs currently outside any
        /// room. Rooms without encounter plans are allowed.
        /// </param>
        /// <returns>The explicit remained-active or suspended transition.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="livingPcCount"/> is not positive.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="livingPcRoomIds"/> is null.</exception>
        /// <exception cref="ArgumentException">A supplied room ID is not positive.</exception>
        /// <exception cref="InvalidOperationException">No encounter group is currently active.</exception>
        public DungeonEncounterSuspensionResult SuspendIfPartyOutsideActiveRegions(
            int livingPcCount,
            IEnumerable<int> livingPcRoomIds
        )
        {
            if (livingPcCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(livingPcCount));
            if (livingPcRoomIds == null)
                throw new ArgumentNullException(nameof(livingPcRoomIds));

            HashSet<int> occupied;
            if (livingPcRoomIds is HashSet<int> suppliedSet)
            {
                occupied = suppliedSet;
                foreach (int roomId in occupied)
                {
                    if (roomId <= 0)
                        throw new ArgumentException(
                            "Occupied room IDs must be positive.",
                            nameof(livingPcRoomIds)
                        );
                }
            }
            else
            {
                HashSet<int> copiedRooms = new();
                foreach (int roomId in livingPcRoomIds)
                {
                    if (roomId <= 0)
                        throw new ArgumentException(
                            "Occupied room IDs must be positive.",
                            nameof(livingPcRoomIds)
                        );
                    copiedRooms.Add(roomId);
                }
                occupied = copiedRooms;
            }

            int activeCount = 0;
            foreach (EncounterGroup group in orderedGroups)
            {
                if (group.State != DungeonEncounterGroupState.Active)
                    continue;
                activeCount++;
                if (occupied.Contains(group.Plan.RoomId))
                {
                    return new DungeonEncounterSuspensionResult(
                        DungeonEncounterSuspensionTransition.RemainedActive,
                        Array.Empty<string>()
                    );
                }
            }
            if (activeCount == 0)
                throw new InvalidOperationException(
                    "Cannot suspend combat when no encounter group is active."
                );

            string[] suspendedEncounterIds = new string[activeCount];
            int suspendedIndex = 0;
            foreach (EncounterGroup group in orderedGroups)
            {
                if (group.State != DungeonEncounterGroupState.Active)
                    continue;
                group.State = DungeonEncounterGroupState.Suspended;
                suspendedEncounterIds[suspendedIndex++] = group.Plan.Id;
                RefreshEncounterView(group);
            }
            return new DungeonEncounterSuspensionResult(
                DungeonEncounterSuspensionTransition.Suspended,
                suspendedEncounterIds
            );
        }

        /// <summary>
        /// Marks one materialized active or suspended creature permanently defeated and clears
        /// empty groups.
        /// </summary>
        /// <param name="creatureInstanceId">The stable non-empty creature instance ID.</param>
        /// <returns>
        /// The remaining group count and whether the group or current active combat completed.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="creatureInstanceId"/> is blank.</exception>
        /// <exception cref="KeyNotFoundException">No planned creature uses the instance ID.</exception>
        /// <exception cref="InvalidOperationException">
        /// The creature is not in an active or suspended group, or was already defeated.
        /// </exception>
        public DungeonCreatureDefeatResult MarkCreatureDefeated(string creatureInstanceId)
        {
            if (string.IsNullOrWhiteSpace(creatureInstanceId))
                throw new ArgumentException(
                    "A creature instance ID is required.",
                    nameof(creatureInstanceId)
                );
            if (!creaturesByInstanceId.TryGetValue(creatureInstanceId, out CreatureEntry creature))
            {
                throw new KeyNotFoundException(
                    $"Creature instance '{creatureInstanceId}' is not registered."
                );
            }

            EncounterGroup group = creature.Group;
            bool participatedInCurrentCombat = group.State == DungeonEncounterGroupState.Active;
            if (!participatedInCurrentCombat && group.State != DungeonEncounterGroupState.Suspended)
            {
                throw new InvalidOperationException(
                    $"Creature '{creatureInstanceId}' cannot be defeated while encounter "
                        + $"'{group.Plan.Id}' is {group.State}."
                );
            }
            if (creature.IsDefeated)
                throw new InvalidOperationException(
                    $"Creature '{creatureInstanceId}' is already defeated."
                );

            creature.IsDefeated = true;
            int remaining = LivingCreatureCount(group);
            bool groupCleared = remaining == 0;
            if (groupCleared)
                group.State = DungeonEncounterGroupState.Cleared;
            RefreshEncounterView(group);
            bool currentCombatCompleted =
                participatedInCurrentCombat && groupCleared && !HasActiveEncounters;
            return new DungeonCreatureDefeatResult(
                group.Plan.Id,
                creatureInstanceId,
                remaining,
                groupCleared,
                currentCombatCompleted
            );
        }

        /// <summary>Captures all persistent lifecycle state in deterministic encounter order.</summary>
        /// <returns>An immutable snapshot suitable for a later persistence adapter.</returns>
        public DungeonEncounterLifecycleSnapshot CaptureSnapshot()
        {
            return new DungeonEncounterLifecycleSnapshot(
                orderedGroups.Select(group => new DungeonEncounterGroupSnapshot(
                    group.Plan.Id,
                    group.State,
                    group
                        .Creatures.Where(creature => creature.IsDefeated)
                        .Select(creature => creature.InstanceId)
                ))
            );
        }

        /// <summary>
        /// Converts restored active groups to suspended groups before exploration resumes.
        /// </summary>
        /// <returns>The normalized encounter IDs in deterministic order.</returns>
        /// <remarks>
        /// Initiative and transient turn state are intentionally not persisted. A process loading
        /// a snapshot must therefore resume those groups through room entry and roll a fresh round.
        /// </remarks>
        public IReadOnlyList<string> NormalizeActiveGroupsForExplorationRestore()
        {
            EncounterGroup[] active = orderedGroups
                .Where(group => group.State == DungeonEncounterGroupState.Active)
                .ToArray();
            foreach (EncounterGroup group in active)
            {
                group.State = DungeonEncounterGroupState.Suspended;
                RefreshEncounterView(group);
            }
            return Array.AsReadOnly(active.Select(group => group.Plan.Id).ToArray());
        }

        /// <summary>Restores lifecycle state against the immutable plans for the same floor.</summary>
        /// <param name="plans">The complete immutable encounter plans for the floor.</param>
        /// <param name="snapshot">Exactly one valid group snapshot for every plan.</param>
        /// <returns>A restored state machine ready for room-entry and defeat transitions.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="plans"/> or <paramref name="snapshot"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The snapshot does not exactly match the plans or violates a lifecycle invariant.
        /// </exception>
        public static DungeonEncounterStateMachine Restore(
            IEnumerable<DungeonEncounterPlan> plans,
            DungeonEncounterLifecycleSnapshot snapshot
        )
        {
            if (plans == null)
                throw new ArgumentNullException(nameof(plans));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            DungeonEncounterStateMachine restored = new(plans);
            restored.ApplySnapshot(snapshot);
            return restored;
        }

        private static void ValidatePlans(IReadOnlyList<DungeonEncounterPlan> plans)
        {
            if (plans.Any(plan => plan == null))
                throw new ArgumentException(
                    "Encounter plan collections cannot contain null entries.",
                    nameof(plans)
                );
            if (plans.Any(plan => string.IsNullOrWhiteSpace(plan.Id)))
                throw new ArgumentException(
                    "Every encounter plan requires a stable non-empty ID.",
                    nameof(plans)
                );
            if (
                plans.Select(plan => plan.Id).Distinct(StringComparer.Ordinal).Count()
                != plans.Count
            )
                throw new ArgumentException("Encounter plan IDs must be unique.", nameof(plans));
            if (plans.Any(plan => plan.RoomId <= 0))
                throw new ArgumentException(
                    "Every encounter plan requires a positive room ID.",
                    nameof(plans)
                );
            if (plans.Select(plan => plan.RoomId).Distinct().Count() != plans.Count)
                throw new ArgumentException(
                    "At most one encounter plan may belong to a room.",
                    nameof(plans)
                );
            if (plans.Any(plan => plan.SpawnCells.Count != plan.CreatureIds.Count))
            {
                throw new ArgumentException(
                    "Every encounter plan requires one spawn cell per creature ID.",
                    nameof(plans)
                );
            }
            if (plans.Any(plan => plan.CreatureIds.Any(string.IsNullOrWhiteSpace)))
                throw new ArgumentException(
                    "Encounter creature content IDs cannot be blank.",
                    nameof(plans)
                );
        }

        private static EncounterGroup CreateInitialGroup(DungeonEncounterPlan plan, int groupIndex)
        {
            EncounterGroup group = new(
                plan,
                plan.IsResolved
                    ? DungeonEncounterGroupState.Cleared
                    : DungeonEncounterGroupState.Dormant,
                groupIndex
            );
            for (int index = 0; index < plan.CreatureIds.Count; index++)
            {
                group.Creatures.Add(
                    new CreatureEntry(
                        group,
                        CreateCreatureInstanceId(plan.Id, index),
                        plan.CreatureIds[index],
                        plan.IsResolved
                    )
                );
            }
            return group;
        }

        private static DungeonEncounterGroupView CreateView(EncounterGroup group)
        {
            return new DungeonEncounterGroupView(
                group.Plan,
                group.State,
                group.Creatures.Select(creature => new DungeonEncounterCreatureView(
                    creature.InstanceId,
                    creature.CreatureId,
                    group.Plan.Id,
                    creature.IsDefeated
                ))
            );
        }

        private static int LivingCreatureCount(EncounterGroup group) =>
            group.Creatures.Count(creature => !creature.IsDefeated);

        private DungeonEncounterGroupView RefreshEncounterView(EncounterGroup group)
        {
            DungeonEncounterGroupView refreshed = CreateView(group);
            encounterViews[group.Index] = refreshed;
            return refreshed;
        }

        private void ApplySnapshot(DungeonEncounterLifecycleSnapshot snapshot)
        {
            if (snapshot.Groups.Count != orderedGroups.Length)
            {
                throw new ArgumentException(
                    "The lifecycle snapshot must contain exactly one group for every encounter plan.",
                    nameof(snapshot)
                );
            }

            Dictionary<string, DungeonEncounterGroupSnapshot> snapshotById =
                snapshot.Groups.ToDictionary(group => group.EncounterId, StringComparer.Ordinal);
            foreach (EncounterGroup group in orderedGroups)
            {
                if (
                    !snapshotById.TryGetValue(
                        group.Plan.Id,
                        out DungeonEncounterGroupSnapshot groupSnapshot
                    )
                )
                {
                    throw new ArgumentException(
                        $"The lifecycle snapshot is missing encounter '{group.Plan.Id}'.",
                        nameof(snapshot)
                    );
                }
                ValidateGroupSnapshot(group, groupSnapshot, snapshot);
            }

            foreach (EncounterGroup group in orderedGroups)
            {
                DungeonEncounterGroupSnapshot groupSnapshot = snapshotById[group.Plan.Id];
                HashSet<string> defeated = new(
                    groupSnapshot.DefeatedCreatureInstanceIds,
                    StringComparer.Ordinal
                );
                group.State = groupSnapshot.State;
                foreach (CreatureEntry creature in group.Creatures)
                    creature.IsDefeated = defeated.Contains(creature.InstanceId);
                RefreshEncounterView(group);
            }
        }

        private static void ValidateGroupSnapshot(
            EncounterGroup group,
            DungeonEncounterGroupSnapshot snapshot,
            DungeonEncounterLifecycleSnapshot completeSnapshot
        )
        {
            HashSet<string> knownIds = new(
                group.Creatures.Select(creature => creature.InstanceId),
                StringComparer.Ordinal
            );
            if (snapshot.DefeatedCreatureInstanceIds.Any(id => !knownIds.Contains(id)))
            {
                throw new ArgumentException(
                    $"Encounter '{group.Plan.Id}' snapshot contains an unknown creature instance ID.",
                    nameof(completeSnapshot)
                );
            }

            int defeatedCount = snapshot.DefeatedCreatureInstanceIds.Count;
            int creatureCount = group.Creatures.Count;
            if (group.Plan.IsResolved && snapshot.State != DungeonEncounterGroupState.Cleared)
            {
                throw new ArgumentException(
                    $"Resolved encounter '{group.Plan.Id}' cannot be restored as {snapshot.State}.",
                    nameof(completeSnapshot)
                );
            }
            if (snapshot.State == DungeonEncounterGroupState.Dormant && defeatedCount != 0)
            {
                throw new ArgumentException(
                    $"Dormant encounter '{group.Plan.Id}' cannot contain defeated creatures.",
                    nameof(completeSnapshot)
                );
            }
            if (
                snapshot.State == DungeonEncounterGroupState.Cleared
                && defeatedCount != creatureCount
            )
            {
                throw new ArgumentException(
                    $"Cleared encounter '{group.Plan.Id}' must have every creature defeated.",
                    nameof(completeSnapshot)
                );
            }
            if (
                (
                    snapshot.State == DungeonEncounterGroupState.Active
                    || snapshot.State == DungeonEncounterGroupState.Suspended
                )
                && defeatedCount >= creatureCount
            )
            {
                throw new ArgumentException(
                    $"{snapshot.State} encounter '{group.Plan.Id}' must retain a living creature.",
                    nameof(completeSnapshot)
                );
            }
        }

        private sealed class EncounterGroup
        {
            internal EncounterGroup(
                DungeonEncounterPlan plan,
                DungeonEncounterGroupState state,
                int index
            )
            {
                Plan = plan;
                State = state;
                Index = index;
            }

            internal DungeonEncounterPlan Plan { get; }
            internal List<CreatureEntry> Creatures { get; } = new();
            internal DungeonEncounterGroupState State { get; set; }
            internal int Index { get; }
        }

        private sealed class CreatureEntry
        {
            internal CreatureEntry(
                EncounterGroup group,
                string instanceId,
                string creatureId,
                bool isDefeated
            )
            {
                Group = group;
                InstanceId = instanceId;
                CreatureId = creatureId;
                IsDefeated = isDefeated;
            }

            internal EncounterGroup Group { get; }
            internal string InstanceId { get; }
            internal string CreatureId { get; }
            internal bool IsDefeated { get; set; }
        }
    }
}
