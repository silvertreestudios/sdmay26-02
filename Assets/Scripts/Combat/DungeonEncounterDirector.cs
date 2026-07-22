using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using UnityEngine;

namespace Game.Combat.Encounters
{
    /// <summary>
    /// Coordinates room-entry lifecycle transitions with creature materialization and initiative.
    /// </summary>
    /// <remarks>
    /// Movement code supplies explicit room-entry and party-region observations. The director owns
    /// no singleton lookup and no static event subscription, which keeps generated-dungeon policy
    /// testable without loading a production scene.
    /// </remarks>
    public sealed class DungeonEncounterDirector : IDisposable
    {
        private readonly DungeonEncounterStateMachine lifecycle;
        private readonly ActionController[] party;
        private readonly CombatManagerInterface combatManager;
        private readonly DungeonEncounterMaterializer materializer;
        private readonly Transform encounterRoot;
        private readonly DungeonRoom[] rooms;
        private readonly IReadOnlyDictionary<int, DungeonRoom> roomsById;
        private readonly Dictionary<string, DungeonEncounterMaterialization> materializations = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<
            DungeonReinforcementRequest,
            PendingReinforcementActivation
        > pendingReinforcementActivations = new();
        private readonly Dictionary<
            string,
            DungeonReinforcementRequest
        > pendingReinforcementRequestsByEncounter = new(StringComparer.Ordinal);
        private readonly HashSet<int> effectiveOccupiedRooms = new();
        private PendingStartupActivation pendingStartupActivation;
        private bool isDisposed;

        /// <summary>Creates a director for one floor's lifecycle state and runtime dependencies.</summary>
        /// <param name="lifecycle">The required pristine or restored floor lifecycle.</param>
        /// <param name="party">The floor's distinct registered player controllers.</param>
        /// <param name="combatManager">The explicit combat scheduler used by this floor.</param>
        /// <param name="materializer">The catalog-backed creature materializer.</param>
        /// <param name="encounterRoot">The hierarchy owner for materialized encounter creatures.</param>
        /// <param name="rooms">The floor's unique room definitions used for safe spawn fallback.</param>
        /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
        /// <exception cref="ArgumentException">The party is empty, duplicated, or contains null.</exception>
        public DungeonEncounterDirector(
            DungeonEncounterStateMachine lifecycle,
            IEnumerable<ActionController> party,
            CombatManagerInterface combatManager,
            DungeonEncounterMaterializer materializer,
            Transform encounterRoot,
            IEnumerable<DungeonRoom> rooms
        )
        {
            this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            if (party == null)
                throw new ArgumentNullException(nameof(party));
            this.party = party.ToArray();
            if (this.party.Length == 0)
                throw new ArgumentException("A dungeon encounter requires a party.", nameof(party));
            if (this.party.Any(controller => controller == null))
                throw new ArgumentException(
                    "The dungeon party cannot contain null.",
                    nameof(party)
                );
            if (this.party.Distinct().Count() != this.party.Length)
                throw new ArgumentException(
                    "The dungeon party cannot contain duplicate controllers.",
                    nameof(party)
                );

            this.combatManager =
                combatManager ?? throw new ArgumentNullException(nameof(combatManager));
            this.materializer =
                materializer ?? throw new ArgumentNullException(nameof(materializer));
            this.encounterRoot =
                encounterRoot != null
                    ? encounterRoot
                    : throw new ArgumentNullException(nameof(encounterRoot));
            if (rooms == null)
                throw new ArgumentNullException(nameof(rooms));
            DungeonRoom[] copiedRooms = rooms.ToArray();
            if (copiedRooms.Any(room => room == null))
                throw new ArgumentException("Dungeon rooms cannot contain null.", nameof(rooms));
            if (copiedRooms.Select(room => room.Id).Distinct().Count() != copiedRooms.Length)
                throw new ArgumentException("Dungeon room IDs must be unique.", nameof(rooms));
            this.rooms = copiedRooms;
            roomsById = copiedRooms.ToDictionary(room => room.Id);
            if (
                lifecycle.Encounters.Any(encounter => !roomsById.ContainsKey(encounter.Plan.RoomId))
            )
                throw new ArgumentException(
                    "Every encounter plan must reference a supplied room.",
                    nameof(rooms)
                );

            if (!combatManager.IsCombatActive && lifecycle.HasActiveEncounters)
                lifecycle.NormalizeActiveGroupsForExplorationRestore();
            if (combatManager.IsCombatActive != lifecycle.HasActiveEncounters)
            {
                throw new InvalidOperationException(
                    "Encounter lifecycle and combat activity must agree when a director is created."
                );
            }

            // Persisted groups have already been materialized in an earlier session. Restore their
            // living scene objects immediately, including survivors displaced outside the source room.
            foreach (DungeonEncounterGroupView encounter in lifecycle.Encounters)
            {
                if (
                    encounter.State == DungeonEncounterGroupState.Suspended
                    && encounter.LivingCreatures.Count > 0
                )
                {
                    AddMaterialization(encounter);
                }
            }
            combatManager.DungeonCombatStartupAborted += OnDungeonCombatStartupAborted;
            combatManager.DungeonCombatStartupCompleted += OnDungeonCombatStartupCompleted;
            combatManager.DungeonReinforcementRequestCompleted +=
                OnDungeonReinforcementRequestCompleted;
        }

        /// <summary>Raised after one materialized encounter creature is permanently defeated.</summary>
        public event Action<DungeonCreatureDefeatResult> CreatureDefeated = delegate { };

        /// <summary>Gets the authoritative lifecycle state owned by this director.</summary>
        public DungeonEncounterStateMachine Lifecycle => lifecycle;

        /// <summary>
        /// Activates, reinforces, or resumes the encounter for a room entered by a living PC.
        /// </summary>
        /// <param name="roomId">The positive room ID entered by a living party member.</param>
        /// <returns>The lifecycle transition that was applied.</returns>
        /// <exception cref="InvalidOperationException">
        /// The director is disposed, no living PC remains, or combat and lifecycle state disagree.
        /// </exception>
        public DungeonRoomEntryResult EnterRoom(int roomId)
        {
            ThrowIfDisposed();
            ActionController[] livingParty = party.Where(CanParticipate).ToArray();
            if (livingParty.Length == 0)
                throw new InvalidOperationException(
                    "A living PC is required to enter an encounter room."
                );

            DungeonEncounterGroupView before = lifecycle.GetRoomEncounter(roomId);
            if (lifecycle.HasActiveEncounters != combatManager.IsCombatActive)
            {
                throw new InvalidOperationException(
                    "Encounter lifecycle and combat activity must agree before room entry."
                );
            }
            if (
                before.State == DungeonEncounterGroupState.Dormant
                || before.State == DungeonEncounterGroupState.Suspended
            )
                SupersedePendingReinforcement(before.Plan.Id);
            if (
                (
                    before.State == DungeonEncounterGroupState.Dormant
                    || before.State == DungeonEncounterGroupState.Suspended
                )
                && before.LivingCreatures.Count > 0
                && !materializations.ContainsKey(before.Plan.Id)
            )
            {
                AddMaterialization(before);
            }

            DungeonRoomEntryResult result = lifecycle.EnterRoom(roomId);
            if (result.CompletedImmediately || !result.StartsCombat && !result.JoinsRunningCombat)
                return result;

            DungeonEncounterMaterialization materialization = materializations[
                result.Encounter.Plan.Id
            ];
            ActionController[] livingEnemies = materialization
                .Controllers.Where(CanParticipate)
                .ToArray();
            if (livingEnemies.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Encounter '{result.Encounter.Plan.Id}' activated without a living materialized creature."
                );
            }

            foreach (ActionController enemy in livingEnemies)
                combatManager.AddCombatant(enemy);
            foreach (ActionController partyMember in livingParty)
                combatManager.AddCombatant(partyMember);

            if (result.StartsCombat)
            {
                if (combatManager.IsCombatActive)
                    throw new InvalidOperationException(
                        "The encounter lifecycle requested a new combat while combat is already active."
                    );
                PendingStartupActivation activation = new();
                activation.Add(result.Encounter.Plan.Id, before.State);
                pendingStartupActivation = activation;
                try
                {
                    long generation = combatManager.StartDungeonCombat(
                        livingParty.Concat(livingEnemies).ToArray()
                    );
                    if (ReferenceEquals(pendingStartupActivation, activation))
                        activation.Generation = generation;
                }
                catch
                {
                    if (ReferenceEquals(pendingStartupActivation, activation))
                    {
                        activation.Restore(lifecycle);
                        pendingStartupActivation = null;
                    }
                    throw;
                }
            }
            else
            {
                if (!combatManager.IsCombatActive)
                    throw new InvalidOperationException(
                        "The encounter lifecycle requested reinforcements while combat is inactive."
                    );
                PendingStartupActivation activation = pendingStartupActivation;
                bool belongsToStartingGeneration = activation != null;
                if (belongsToStartingGeneration)
                    activation.Add(result.Encounter.Plan.Id, before.State);
                try
                {
                    DungeonReinforcementRequest request = combatManager.AddDungeonReinforcements(
                        livingEnemies
                    );
                    PendingReinforcementActivation pending = new(
                        result.Encounter.Plan.Id,
                        before.State
                    );
                    pendingReinforcementActivations.Add(request, pending);
                    pendingReinforcementRequestsByEncounter[result.Encounter.Plan.Id] = request;
                }
                catch
                {
                    lifecycle.RestoreActivationAfterRejectedReinforcement(
                        result.Encounter.Plan.Id,
                        before.State
                    );
                    if (belongsToStartingGeneration)
                        activation.Remove(result.Encounter.Plan.Id);
                    throw;
                }
            }

            return result;
        }

        /// <summary>
        /// Suspends combat when no living PC occupies an active encounter room and no living
        /// active enemy is acting or positioned outside its source room.
        /// </summary>
        /// <param name="livingPcCount">The positive total number of living PCs.</param>
        /// <param name="livingPcRoomIds">Room IDs occupied by those PCs; omit PCs outside rooms.</param>
        /// <returns>The lifecycle suspension decision.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="livingPcRoomIds"/> is null.
        /// </exception>
        public DungeonEncounterSuspensionResult EvaluatePartyRegions(
            int livingPcCount,
            IEnumerable<int> livingPcRoomIds
        )
        {
            ThrowIfDisposed();
            if (livingPcRoomIds == null)
                throw new ArgumentNullException(nameof(livingPcRoomIds));
            if (!combatManager.IsCombatActive)
                throw new InvalidOperationException(
                    "Party regions can only be evaluated while dungeon combat is active."
                );
            effectiveOccupiedRooms.Clear();
            if (livingPcRoomIds is HashSet<int> suppliedSet)
            {
                foreach (int roomId in suppliedSet)
                    effectiveOccupiedRooms.Add(roomId);
            }
            else
            {
                foreach (int roomId in livingPcRoomIds)
                    effectiveOccupiedRooms.Add(roomId);
            }
            AddActiveEncounterRoomsRequiringCombat(effectiveOccupiedRooms);
            DungeonEncounterSuspensionResult result = lifecycle.SuspendIfPartyOutsideActiveRegions(
                livingPcCount,
                effectiveOccupiedRooms
            );
            if (result.Transition == DungeonEncounterSuspensionTransition.Suspended)
            {
                combatManager.SuspendDungeonCombat();
            }
            return result;
        }

        // Restored survivors can occupy a different room or an unroomed corridor. Resume their
        // suspended encounter when a party member reaches that room, or becomes adjacent where no
        // room boundary exists, so the survivor cannot remain an inert blocking grid occupant.
        internal void ResumeReachedSuspendedEncounters(IReadOnlyList<Vector3> livingPartyPositions)
        {
            ThrowIfDisposed();
            if (livingPartyPositions == null)
                throw new ArgumentNullException(nameof(livingPartyPositions));

            IReadOnlyList<DungeonEncounterGroupView> encounters = lifecycle.Encounters;
            for (int encounterIndex = 0; encounterIndex < encounters.Count; encounterIndex++)
            {
                DungeonEncounterGroupView encounter = encounters[encounterIndex];
                if (encounter.State != DungeonEncounterGroupState.Suspended)
                    continue;
                if (!materializations.TryGetValue(encounter.Plan.Id, out var materialization))
                {
                    throw new InvalidOperationException(
                        $"Suspended encounter '{encounter.Plan.Id}' has no materialized creatures."
                    );
                }

                bool reached = false;
                for (
                    int enemyIndex = 0;
                    enemyIndex < materialization.Controllers.Count;
                    enemyIndex++
                )
                {
                    ActionController enemy = materialization.Controllers[enemyIndex];
                    if (!CanParticipate(enemy))
                        continue;
                    for (int partyIndex = 0; partyIndex < livingPartyPositions.Count; partyIndex++)
                    {
                        Vector3 partyPosition = livingPartyPositions[partyIndex];
                        if (!OccupiesReachedRegion(partyPosition, enemy.transform.position))
                            continue;
                        reached = true;
                        break;
                    }
                    if (reached)
                        break;
                }
                if (reached)
                    EnterRoom(encounter.Plan.RoomId);
            }
        }

        private void AddActiveEncounterRoomsRequiringCombat(ISet<int> occupiedRoomIds)
        {
            IReadOnlyList<DungeonEncounterGroupView> encounters = lifecycle.Encounters;
            for (int encounterIndex = 0; encounterIndex < encounters.Count; encounterIndex++)
            {
                DungeonEncounterGroupView encounter = encounters[encounterIndex];
                if (encounter.State != DungeonEncounterGroupState.Active)
                    continue;
                if (!materializations.TryGetValue(encounter.Plan.Id, out var materialization))
                {
                    throw new InvalidOperationException(
                        $"Active encounter '{encounter.Plan.Id}' has no materialized creatures."
                    );
                }

                DungeonRoom room = roomsById[encounter.Plan.RoomId];
                for (
                    int controllerIndex = 0;
                    controllerIndex < materialization.Controllers.Count;
                    controllerIndex++
                )
                {
                    ActionController controller = materialization.Controllers[controllerIndex];
                    if (
                        !CanParticipate(controller)
                        || !controller.IsTakingAction
                            && Contains(room, controller.transform.position)
                    )
                    {
                        continue;
                    }
                    // A moving or displaced enemy remains a live grid occupant. Keep its encounter
                    // active so movement can finish and the party can continue targeting it.
                    occupiedRoomIds.Add(encounter.Plan.RoomId);
                    break;
                }
            }
        }

        private static bool Contains(DungeonRoom room, Vector3 worldPosition)
        {
            int x = Mathf.RoundToInt(worldPosition.x);
            int z = Mathf.RoundToInt(worldPosition.z);
            return x >= room.MinimumX
                && x <= room.MaximumX
                && z >= room.MinimumZ
                && z <= room.MaximumZ;
        }

        private bool OccupiesReachedRegion(Vector3 partyPosition, Vector3 enemyPosition)
        {
            foreach (DungeonRoom room in rooms)
            {
                if (Contains(room, partyPosition) && Contains(room, enemyPosition))
                    return true;
            }

            foreach (DungeonRoom room in rooms)
            {
                if (Contains(room, enemyPosition))
                    return false;
            }

            int partyX = Mathf.RoundToInt(partyPosition.x);
            int partyZ = Mathf.RoundToInt(partyPosition.z);
            int enemyX = Mathf.RoundToInt(enemyPosition.x);
            int enemyZ = Mathf.RoundToInt(enemyPosition.z);
            return Math.Abs(partyX - enemyX) <= 1 && Math.Abs(partyZ - enemyZ) <= 1;
        }

        /// <summary>Captures the complete persistence-facing encounter lifecycle state.</summary>
        /// <returns>An immutable snapshot that can be restored through the state-machine API.</returns>
        public DungeonEncounterLifecycleSnapshot CaptureSnapshot()
        {
            ThrowIfDisposed();
            return lifecycle.CaptureSnapshot();
        }

        /// <summary>Releases instance-event subscriptions owned by this director.</summary>
        public void Dispose()
        {
            if (isDisposed)
                return;
            combatManager.DungeonCombatStartupAborted -= OnDungeonCombatStartupAborted;
            combatManager.DungeonCombatStartupCompleted -= OnDungeonCombatStartupCompleted;
            combatManager.DungeonReinforcementRequestCompleted -=
                OnDungeonReinforcementRequestCompleted;
            pendingReinforcementActivations.Clear();
            pendingReinforcementRequestsByEncounter.Clear();
            foreach (DungeonEncounterMaterialization materialization in materializations.Values)
            {
                foreach (DungeonEncounterMember member in materialization.Members)
                {
                    if (member != null)
                        member.Defeated -= OnCreatureDefeated;
                }
            }
            isDisposed = true;
        }

        private void OnDungeonCombatStartupAborted(long generation)
        {
            if (isDisposed || pendingStartupActivation == null)
                return;
            PendingStartupActivation activation = pendingStartupActivation;
            if (activation.Generation.HasValue && activation.Generation.Value != generation)
                return;

            activation.Restore(lifecycle);
            pendingStartupActivation = null;
        }

        private void OnDungeonCombatStartupCompleted(long generation)
        {
            if (isDisposed || pendingStartupActivation == null)
                return;
            PendingStartupActivation activation = pendingStartupActivation;
            if (activation.Generation.HasValue && activation.Generation.Value != generation)
                return;
            pendingStartupActivation = null;
        }

        private void OnDungeonReinforcementRequestCompleted(
            DungeonReinforcementRequest request,
            DungeonReinforcementRequestStatus status
        )
        {
            if (
                isDisposed
                || request == null
                || !pendingReinforcementActivations.TryGetValue(
                    request,
                    out PendingReinforcementActivation activation
                )
            )
                return;
            pendingReinforcementActivations.Remove(request);
            if (
                !pendingReinforcementRequestsByEncounter.TryGetValue(
                    activation.EncounterId,
                    out DungeonReinforcementRequest current
                ) || !ReferenceEquals(current, request)
            )
                return;
            pendingReinforcementRequestsByEncounter.Remove(activation.EncounterId);

            switch (status)
            {
                case DungeonReinforcementRequestStatus.Accepted:
                    return;
                case DungeonReinforcementRequestStatus.RejectedBeforeAcceptance:
                    // The bridge guarantees rejected requests published no identity, controller,
                    // passive hook, or action state. Retain the pre-accept materialization for a
                    // deterministic retry and compensate only its exact lifecycle activation.
                    lifecycle.RestoreActivationAfterRejectedReinforcement(
                        activation.EncounterId,
                        activation.PreviousState
                    );
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        private void SupersedePendingReinforcement(string encounterId)
        {
            if (
                !pendingReinforcementRequestsByEncounter.TryGetValue(
                    encounterId,
                    out DungeonReinforcementRequest superseded
                )
            )
                return;
            pendingReinforcementRequestsByEncounter.Remove(encounterId);
            pendingReinforcementActivations.Remove(superseded);
        }

        private void AddMaterialization(DungeonEncounterGroupView encounter)
        {
            DungeonEncounterMaterialization created = materializer.Materialize(
                encounter,
                encounterRoot,
                roomsById[encounter.Plan.RoomId]
            );
            foreach (DungeonEncounterMember member in created.Members)
                member.Defeated += OnCreatureDefeated;
            materializations.Add(encounter.Plan.Id, created);
        }

        private void OnCreatureDefeated(DungeonEncounterMember member)
        {
            if (isDisposed)
                return;
            DungeonCreatureDefeatResult result = lifecycle.MarkCreatureDefeated(member.InstanceId);
            CreatureDefeated.Invoke(result);
            if (result.CurrentCombatCompleted && !HasActionInProgress())
                combatManager.CheckForEndOfGame();
        }

        private bool HasActionInProgress() =>
            party.Any(controller => controller != null && controller.IsTakingAction)
            || materializations.Values.Any(materialization =>
                materialization.Controllers.Any(controller =>
                    controller != null && controller.IsTakingAction
                )
            );

        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(DungeonEncounterDirector));
        }

        private static bool CanParticipate(ActionController controller) =>
            controller != null && controller.gameObject.activeSelf && controller.isActiveAndEnabled;

        private sealed class PendingStartupActivation
        {
            private readonly List<Activation> activations = new();

            internal void Add(string encounterId, DungeonEncounterGroupState previousState)
            {
                if (activations.Any(value => value.EncounterId == encounterId))
                    return;
                activations.Add(new Activation(encounterId, previousState));
            }

            internal void Remove(string encounterId) =>
                activations.RemoveAll(value => value.EncounterId == encounterId);

            internal void Restore(DungeonEncounterStateMachine lifecycle)
            {
                for (int index = activations.Count - 1; index >= 0; index--)
                {
                    Activation activation = activations[index];
                    lifecycle.RestoreActivationAfterStartupAbort(
                        activation.EncounterId,
                        activation.PreviousState
                    );
                }
            }

            internal long? Generation { get; set; }

            private readonly struct Activation
            {
                internal Activation(string encounterId, DungeonEncounterGroupState previousState)
                {
                    EncounterId = encounterId;
                    PreviousState = previousState;
                }

                internal string EncounterId { get; }
                internal DungeonEncounterGroupState PreviousState { get; }
            }
        }

        private sealed class PendingReinforcementActivation
        {
            internal PendingReinforcementActivation(
                string encounterId,
                DungeonEncounterGroupState previousState
            )
            {
                EncounterId = encounterId;
                PreviousState = previousState;
            }

            internal string EncounterId { get; }
            internal DungeonEncounterGroupState PreviousState { get; }
        }
    }
}
