using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
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
            if (lifecycle.HasActiveEncounters && !combatManager.IsCombatActive)
            {
                throw new InvalidOperationException(
                    "An active hostile encounter requires Tactics when a director is created."
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

        /// <summary>Raised after an encounter lifecycle transition commits.</summary>
        public event Action EncounterLifecycleChanged = delegate { };

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
            if (lifecycle.HasActiveEncounters && !combatManager.IsCombatActive)
            {
                throw new InvalidOperationException(
                    "An active hostile encounter requires Tactics before room entry."
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
                    combatManager.AddDungeonReinforcements(livingEnemies);
                else
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
        /// Retains Tactics while a hostile lifecycle is active, regardless of party room position.
        /// </summary>
        /// <param name="livingPcCount">The positive total number of living PCs.</param>
        /// <param name="livingPcRoomIds">Room IDs occupied by those PCs; omit PCs outside rooms.</param>
        /// <returns>A decision that keeps every living enrolled opponent in active Tactics.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="livingPcRoomIds"/> is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The living-PC count is not positive or a supplied room ID is invalid.
        /// </exception>
        public DungeonEncounterSuspensionResult EvaluatePartyRegions(
            int livingPcCount,
            IEnumerable<int> livingPcRoomIds
        )
        {
            ThrowIfDisposed();
            if (livingPcRoomIds == null)
                throw new ArgumentNullException(nameof(livingPcRoomIds));
            if (livingPcCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(livingPcCount));
            if (livingPcRoomIds.Any(roomId => roomId <= 0))
                throw new ArgumentOutOfRangeException(nameof(livingPcRoomIds));
            if (!combatManager.IsCombatActive)
                throw new InvalidOperationException(
                    "Party regions can only be evaluated while dungeon combat is active."
                );
            return new DungeonEncounterSuspensionResult(
                DungeonEncounterSuspensionTransition.RemainedActive,
                Array.Empty<string>()
            );
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
                if (IsSuspendedEncounterReached(encounter, livingPartyPositions))
                    EnterRoom(encounter.Plan.RoomId);
            }
        }

        // Exploration presentation asks this before allowing another leader cell to commit. Keep
        // that preflight on the same materialization and region policy that performs the resume.
        internal bool IsSuspendedEncounterReached(Vector3 livingPartyPosition)
        {
            ThrowIfDisposed();
            IReadOnlyList<Vector3> livingPartyPositions = new[] { livingPartyPosition };
            IReadOnlyList<DungeonEncounterGroupView> encounters = lifecycle.Encounters;
            for (int encounterIndex = 0; encounterIndex < encounters.Count; encounterIndex++)
            {
                DungeonEncounterGroupView encounter = encounters[encounterIndex];
                if (
                    encounter.State == DungeonEncounterGroupState.Suspended
                    && IsSuspendedEncounterReached(encounter, livingPartyPositions)
                )
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSuspendedEncounterReached(
            DungeonEncounterGroupView encounter,
            IReadOnlyList<Vector3> livingPartyPositions
        )
        {
            if (!materializations.TryGetValue(encounter.Plan.Id, out var materialization))
            {
                throw new InvalidOperationException(
                    $"Suspended encounter '{encounter.Plan.Id}' has no materialized creatures."
                );
            }

            for (int enemyIndex = 0; enemyIndex < materialization.Controllers.Count; enemyIndex++)
            {
                ActionController enemy = materialization.Controllers[enemyIndex];
                if (!CanParticipate(enemy))
                    continue;
                for (int partyIndex = 0; partyIndex < livingPartyPositions.Count; partyIndex++)
                {
                    if (
                        OccupiesReachedRegion(
                            livingPartyPositions[partyIndex],
                            enemy.transform.position
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
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

        /// <summary>Captures all living materialized enemies in stable identity order.</summary>
        /// <param name="capturePersistentState">Captures child-owned state for each live actor.</param>
        /// <returns>Complete live enemy runtime records for the floor document.</returns>
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
                    throw new InvalidOperationException(
                        "Encounter materialization identity and controllers are misaligned."
                    );

                for (int index = 0; index < materialization.Members.Count; index++)
                {
                    DungeonEncounterMember member = materialization.Members[index];
                    ActionController controller = materialization.Controllers[index];
                    if (member.DefeatWasReported)
                        continue;

                    CreatureComponent creature =
                        controller.GetComponent<CreatureComponent>()
                        ?? throw new InvalidOperationException(
                            $"Encounter actor '{member.InstanceId}' has no creature state."
                        );
                    if (creature.IsDefeated || creature.hp <= 0)
                        throw new InvalidOperationException(
                            $"Living encounter actor '{member.InstanceId}' has invalid health."
                        );

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

            return captured
                .OrderBy(creature => creature.InstanceId, StringComparer.Ordinal)
                .ToArray();
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
            if (result.CurrentCombatCompleted && !HasActionInProgress)
                combatManager.CheckForEndOfGame();
        }

        private void NotifyLifecycleChange(
            DungeonEncounterGroupView before,
            DungeonEncounterGroupView after
        )
        {
            if (before.State != after.State)
                EncounterLifecycleChanged();
        }

        internal bool HasActionInProgress =>
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
