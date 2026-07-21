using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using UnityEngine;

namespace Game.Combat.Encounters
{
    /// <summary>
    /// Identifies one materialized encounter actor at a single persistence capture boundary.
    /// </summary>
    /// <remarks>
    /// The controller remains a live Unity object and must be consumed immediately. Stable IDs,
    /// cell, and defeat state are copied so persistence adapters never infer identity from object
    /// names or Unity instance IDs.
    /// </remarks>
    public sealed class DungeonEncounterCreatureCapture
    {
        internal DungeonEncounterCreatureCapture(
            string encounterId,
            string instanceId,
            string creatureContentId,
            DungeonCell cell,
            bool isDefeated,
            ActionController controller
        )
        {
            EncounterId = encounterId;
            InstanceId = instanceId;
            CreatureContentId = creatureContentId;
            Cell = cell;
            IsDefeated = isDefeated;
            Controller = controller;
        }

        /// <summary>Gets the stable encounter-plan identifier.</summary>
        public string EncounterId { get; }

        /// <summary>Gets the stable plan-derived actor identifier.</summary>
        public string InstanceId { get; }

        /// <summary>Gets the immutable creature catalog identifier.</summary>
        public string CreatureContentId { get; }

        /// <summary>Gets the actor's captured grid cell.</summary>
        public DungeonCell Cell { get; }

        /// <summary>Gets whether this actor has permanently reported defeat.</summary>
        public bool IsDefeated { get; }

        /// <summary>
        /// Gets the live controller whose child-owned state must be captured before the next frame.
        /// </summary>
        public ActionController Controller { get; }
    }

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
        private readonly HashSet<int> effectiveOccupiedRooms = new();
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
        }

        /// <summary>Raised after one materialized encounter creature is permanently defeated.</summary>
        public event Action<DungeonCreatureDefeatResult> CreatureDefeated = delegate { };

        /// <summary>Raised after an encounter lifecycle transition is fully committed.</summary>
        public event Action<string> EncounterLifecycleChanged = delegate { };

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
            {
                NotifyLifecycleChange(before, lifecycle.GetRoomEncounter(roomId));
                return result;
            }

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
                combatManager.StartDungeonCombat(livingParty.Concat(livingEnemies).ToArray());
            }
            else
            {
                if (!combatManager.IsCombatActive)
                    throw new InvalidOperationException(
                        "The encounter lifecycle requested reinforcements while combat is inactive."
                    );
                combatManager.AddDungeonReinforcements(livingEnemies);
            }

            NotifyLifecycleChange(before, lifecycle.GetRoomEncounter(roomId));
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
                foreach (string encounterId in result.SuspendedEncounterIds)
                    EncounterLifecycleChanged(encounterId);
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

        /// <summary>
        /// Captures every currently living materialized encounter creature in stable instance-ID
        /// order.
        /// </summary>
        /// <param name="capturePersistentState">
        /// Serializes mutable child state after this director has captured identity, cell, and HP.
        /// </param>
        /// <returns>
        /// Live runtime creature records. Dormant encounters remain absent and permanently defeated
        /// instances remain represented by <see cref="DungeonEncounterLifecycleSnapshot"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="capturePersistentState"/> is absent.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A materialization has lost aligned identity/controller state or contains an invalid live
        /// creature.
        /// </exception>
        public IReadOnlyList<DungeonCreatureRuntimeState> CaptureLivingCreatureStates(
            Func<ActionController, string> capturePersistentState
        )
        {
            ThrowIfDisposed();
            if (capturePersistentState == null)
                throw new ArgumentNullException(nameof(capturePersistentState));

            List<DungeonCreatureRuntimeState> captured = new();
            foreach (
                DungeonEncounterMaterialization materialization in materializations
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => entry.Value)
            )
            {
                if (materialization.Members.Count != materialization.Controllers.Count)
                {
                    throw new InvalidOperationException(
                        "Encounter materialization identity and controller sequences are misaligned."
                    );
                }

                for (int index = 0; index < materialization.Members.Count; index++)
                {
                    DungeonEncounterMember member = materialization.Members[index];
                    ActionController controller = materialization.Controllers[index];
                    if (member == null || controller == null || !member.IsConfigured)
                    {
                        throw new InvalidOperationException(
                            "A materialized encounter creature lost its configured runtime identity."
                        );
                    }
                    if (member.DefeatWasReported)
                        continue;

                    CreatureComponent creature = controller.GetComponent<CreatureComponent>();
                    if (creature == null || creature.IsDefeated || creature.hp <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Living encounter member '{member.InstanceId}' has invalid creature state."
                        );
                    }

                    Vector3Int position = Vector3Int.RoundToInt(controller.transform.position);
                    captured.Add(
                        new DungeonCreatureRuntimeState(
                            member.InstanceId,
                            member.CreatureContentId,
                            member.EncounterId,
                            new DungeonCell(position.x, position.z),
                            creature.hp,
                            capturePersistentState(controller) ?? string.Empty
                        )
                    );
                }
            }

            DungeonCreatureRuntimeState[] ordered = captured
                .OrderBy(creature => creature.InstanceId, StringComparer.Ordinal)
                .ToArray();
            if (
                ordered
                    .Select(creature => creature.InstanceId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != ordered.Length
            )
            {
                throw new InvalidOperationException(
                    "Materialized encounter creature instance IDs must be unique."
                );
            }
            return Array.AsReadOnly(ordered);
        }

        /// <summary>
        /// Captures stable identity and Unity handles for every materialized encounter creature.
        /// </summary>
        /// <returns>
        /// Living and defeated materialized actors ordered by stable instance ID. Dormant
        /// encounters remain absent because they have never created runtime actors.
        /// </returns>
        /// <remarks>
        /// Callers must consume each returned controller immediately. The copied identity, cell,
        /// and defeat state are suitable for validating an actor persistence adapter without
        /// relying on Unity object names or instance IDs.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// A materialization has lost aligned identity/controller state, or an actor's reported
        /// defeat disagrees with its authoritative health state.
        /// </exception>
        public IReadOnlyList<DungeonEncounterCreatureCapture> CaptureMaterializedCreatures()
        {
            ThrowIfDisposed();

            List<DungeonEncounterCreatureCapture> captured = new();
            foreach (
                DungeonEncounterMaterialization materialization in materializations
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => entry.Value)
            )
            {
                if (materialization.Members.Count != materialization.Controllers.Count)
                {
                    throw new InvalidOperationException(
                        "Encounter materialization identity and controller sequences are misaligned."
                    );
                }

                for (int index = 0; index < materialization.Members.Count; index++)
                {
                    DungeonEncounterMember member = materialization.Members[index];
                    ActionController controller = materialization.Controllers[index];
                    if (
                        member == null
                        || controller == null
                        || !member.IsConfigured
                        || member.GetComponent<ActionController>() != controller
                    )
                    {
                        throw new InvalidOperationException(
                            "A materialized encounter creature lost its configured runtime identity."
                        );
                    }

                    CreatureComponent creature = controller.GetComponent<CreatureComponent>();
                    if (creature == null)
                    {
                        throw new InvalidOperationException(
                            $"Encounter member '{member.InstanceId}' has no creature state."
                        );
                    }

                    bool isDefeated = member.DefeatWasReported;
                    if (isDefeated != creature.IsDefeated || isDefeated != (creature.hp == 0))
                    {
                        throw new InvalidOperationException(
                            $"Encounter member '{member.InstanceId}' has inconsistent defeat and health state."
                        );
                    }

                    Vector3Int position = Vector3Int.RoundToInt(controller.transform.position);
                    captured.Add(
                        new DungeonEncounterCreatureCapture(
                            member.EncounterId,
                            member.InstanceId,
                            member.CreatureContentId,
                            new DungeonCell(position.x, position.z),
                            isDefeated,
                            controller
                        )
                    );
                }
            }

            DungeonEncounterCreatureCapture[] ordered = captured
                .OrderBy(creature => creature.InstanceId, StringComparer.Ordinal)
                .ToArray();
            if (
                ordered
                    .Select(creature => creature.InstanceId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != ordered.Length
            )
            {
                throw new InvalidOperationException(
                    "Materialized encounter creature instance IDs must be unique."
                );
            }
            return Array.AsReadOnly(ordered);
        }

        /// <summary>Releases instance-event subscriptions owned by this director.</summary>
        public void Dispose()
        {
            if (isDisposed)
                return;
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

        private void NotifyLifecycleChange(
            DungeonEncounterGroupView before,
            DungeonEncounterGroupView after
        )
        {
            if (before.State != after.State)
                EncounterLifecycleChanged(after.Plan.Id);
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
    }
}
