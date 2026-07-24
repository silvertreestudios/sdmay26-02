using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;
using UnityEngine;

namespace Game.DungeonPersistence.Autosave
{
    /// <summary>Coalesces current-floor mutations into safe action-boundary autosaves.</summary>
    [DisallowMultipleComponent]
    internal sealed class DungeonAutosaveCoordinator : MonoBehaviour
    {
        private IDungeonSaveRepository repository;
        private DungeonLevelDocument activeSourceDocument;
        private DungeonRunSave lastCommitted;
        private bool hasCommittedSnapshot;
        private DungeonEncounterRuntimeController runtime;
        private ActionController[] party = Array.Empty<ActionController>();
        private bool pending;

        internal bool IsInitialized { get; private set; }

        internal DungeonRunSave LastCommittedSnapshot =>
            hasCommittedSnapshot
                ? lastCommitted
                : throw new InvalidOperationException("No dungeon autosave has committed yet.");
        internal IReadOnlyList<DungeonSaveDiagnostic> LastDiagnostics { get; private set; } =
            Array.Empty<DungeonSaveDiagnostic>();

        internal void InitializeNewRun(
            DungeonLevelDocument document,
            IDungeonSaveRepository repository,
            DungeonEncounterRuntimeController runtime,
            IEnumerable<ActionController> party
        )
        {
            InitializeCore(document, repository, runtime, party);
            DungeonSaveResult<DungeonRunSave> checkpoint = CheckpointCurrentFloor();
            LastDiagnostics = checkpoint.Diagnostics;
        }

        internal void InitializeLoadedRun(
            DungeonRunSave save,
            IDungeonSaveRepository repository,
            DungeonEncounterRuntimeController runtime,
            IEnumerable<ActionController> party
        )
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            DungeonRunSaveManifest manifest = save.Manifest;
            InitializeCore(save.GetFloor(manifest.CurrentDepth), repository, runtime, party);
            lastCommitted = save;
            hasCommittedSnapshot = true;
        }

        internal DungeonSaveResult<DungeonRunSave> CheckpointCurrentFloor()
        {
            if (!IsInitialized || runtime == null || !runtime.IsInitialized)
                return Failure("The dungeon runtime is not stable and initialized.");
            if (runtime.HasActionInProgress)
                return Failure("A dungeon action is still in progress.");

            DungeonRunSave candidate;
            try
            {
                (DungeonPartyMemberSaveState[] partyState, DungeonLevelDocument floor) =
                    CaptureCurrentState();
                candidate = !hasCommittedSnapshot
                    ? DungeonRunSave.CreateNew(partyState, floor)
                    : lastCommitted.WithCurrentCheckpoint(partyState, floor);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return Failure(exception.Message);
            }

            DungeonSaveResult<bool> published;
            try
            {
                published = repository.Save(candidate);
            }
            catch (Exception exception)
            {
                return Failure(exception.Message);
            }
            LastDiagnostics = published.Diagnostics;
            if (!published.IsSuccess)
            {
                DungeonSaveDiagnostic diagnostic = published.Diagnostics[0];
                return DungeonSaveResult<DungeonRunSave>.Failure(
                    diagnostic.Code,
                    diagnostic.Path,
                    diagnostic.Message
                );
            }

            lastCommitted = candidate;
            hasCommittedSnapshot = true;
            pending = false;
            return DungeonSaveResult<DungeonRunSave>.Success(candidate);
        }

        private void InitializeCore(
            DungeonLevelDocument document,
            IDungeonSaveRepository repository,
            DungeonEncounterRuntimeController runtime,
            IEnumerable<ActionController> party
        )
        {
            if (IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon autosave coordinator can only initialize once."
                );
            activeSourceDocument = document ?? throw new ArgumentNullException(nameof(document));
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (party == null)
                throw new ArgumentNullException(nameof(party));
            this.party = party.ToArray();
            if (this.party.Length == 0 || this.party.Any(controller => controller == null))
                throw new ArgumentException(
                    "Dungeon autosave requires a complete party.",
                    nameof(party)
                );

            runtime.PersistentStateChanged += RequestSave;
            OnGameplayStateCommitted.AddListener(HandleGameplayStateCommitted);
            IsInitialized = true;
        }

        private DungeonSaveResult<DungeonRunSave> Failure(string message)
        {
            DungeonSaveResult<DungeonRunSave> result = DungeonSaveResult<DungeonRunSave>.Failure(
                DungeonSaveDiagnosticCode.InvalidSnapshot,
                "autosave.capture",
                message
            );
            LastDiagnostics = result.Diagnostics;
            return result;
        }

        private void Update()
        {
            TrySavePending();
        }

        private void OnDestroy()
        {
            if (runtime != null)
                runtime.PersistentStateChanged -= RequestSave;
            OnGameplayStateCommitted.RemoveListener(HandleGameplayStateCommitted);
            IsInitialized = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                RequestSave();
                TrySavePending();
            }
        }

        private void OnApplicationQuit()
        {
            RequestSave();
            TrySavePending();
        }

        private void HandleGameplayStateCommitted()
        {
            RequestSave();
            TrySavePending();
        }

        private void RequestSave()
        {
            if (IsInitialized)
                pending = true;
        }

        private void TrySavePending()
        {
            if (!pending || runtime == null || runtime.HasActionInProgress)
                return;

            DungeonSaveResult<DungeonRunSave> result = CheckpointCurrentFloor();
            LastDiagnostics = result.Diagnostics;
        }

        private (
            DungeonPartyMemberSaveState[] Party,
            DungeonLevelDocument Floor
        ) CaptureCurrentState()
        {
            Dictionary<string, GameObject> actorsById = BuildActorIndex();
            string IdentifyActor(GameObject actor)
            {
                foreach (KeyValuePair<string, GameObject> entry in actorsById)
                {
                    if (entry.Value == actor)
                        return entry.Key;
                }
                throw new InvalidOperationException(
                    $"Actor '{actor?.name}' has no dungeon persistence identity."
                );
            }

            DungeonPartyMemberSaveState[] partyState = party
                .Select(controller =>
                {
                    DungeonPartyMemberIdentity identity =
                        controller.GetComponent<DungeonPartyMemberIdentity>();
                    CreatureComponent creature = controller.GetComponent<CreatureComponent>();
                    if (identity == null || !identity.IsConfigured || creature == null)
                        throw new InvalidOperationException(
                            $"Party actor '{controller.name}' has incomplete persistence identity."
                        );
                    Vector3Int cell = Vector3Int.RoundToInt(controller.transform.position);
                    return new DungeonPartyMemberSaveState
                    {
                        RosterSlotId = identity.RosterSlotId,
                        CreatureContentId = identity.CreatureContentId,
                        CellX = cell.x,
                        CellZ = cell.z,
                        CurrentHitPoints = creature.hp,
                        IsDefeated = creature.IsDefeated,
                        State = DungeonActorStateAdapter.Capture(controller, IdentifyActor),
                    };
                })
                .OrderBy(member => member.RosterSlotId, StringComparer.Ordinal)
                .ToArray();

            DungeonRuntimeState runtimeState = runtime.CaptureRuntimeState(controller =>
                DungeonSaveJson.SerializeActor(
                    DungeonActorStateAdapter.Capture(controller, IdentifyActor)
                )
            );
            DungeonLevelDocument floor = BuildFloorDocument(activeSourceDocument, runtimeState);
            return (partyState, floor);
        }

        private Dictionary<string, GameObject> BuildActorIndex()
        {
            Dictionary<string, GameObject> actors = new(StringComparer.Ordinal);
            foreach (ActionController controller in party)
            {
                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (identity == null || !identity.IsConfigured)
                    throw new InvalidOperationException(
                        $"Party actor '{controller.name}' has no stable roster slot."
                    );
                actors.Add(identity.RosterSlotId, controller.gameObject);
            }
            foreach (
                DungeonEncounterMember member in runtime.GetComponentsInChildren<DungeonEncounterMember>(
                    includeInactive: true
                )
            )
            {
                if (member != null && member.IsConfigured)
                    actors.Add(member.InstanceId, member.gameObject);
            }
            return actors;
        }

        private static DungeonLevelDocument BuildFloorDocument(
            DungeonLevelDocument source,
            DungeonRuntimeState runtimeState
        )
        {
            HashSet<string> openDoors = new(runtimeState.OpenDoorIds, StringComparer.Ordinal);
            HashSet<string> resolvedEncounters = new(
                runtimeState.ResolvedEncounterIds,
                StringComparer.Ordinal
            );
            return new DungeonLevelDocument(
                source.Generation,
                source.Rows,
                source.Rooms,
                source.Doors.Select(door => new DungeonDoor(
                    door.Id,
                    door.Cell,
                    openDoors.Contains(door.Id)
                )),
                source.Stairs,
                source.StartCell,
                source.SafeCells,
                source.Objects,
                source.EncounterPlans.Select(plan => new DungeonEncounterPlan(
                    plan.Id,
                    plan.RoomId,
                    plan.Threat,
                    plan.Budget,
                    plan.SpawnCells,
                    plan.CreatureIds,
                    resolvedEncounters.Contains(plan.Id)
                )),
                runtimeState
            );
        }
    }
}
