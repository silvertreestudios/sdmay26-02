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
        private DungeonLevelDocument sourceDocument;
        private DungeonEncounterRuntimeController runtime;
        private ActionController[] party = Array.Empty<ActionController>();
        private bool pending;

        internal bool IsInitialized { get; private set; }
        internal IReadOnlyList<DungeonSaveDiagnostic> LastDiagnostics { get; private set; } =
            Array.Empty<DungeonSaveDiagnostic>();

        internal void Initialize(
            DungeonLevelDocument document,
            IDungeonSaveRepository repository,
            DungeonEncounterRuntimeController runtime,
            IEnumerable<ActionController> party,
            bool saveImmediately
        )
        {
            if (IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon autosave coordinator can only initialize once."
                );
            sourceDocument = document ?? throw new ArgumentNullException(nameof(document));
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
            OnActorActionCompleted.AddListener(HandleActorActionCompleted);
            OnTurnCompleted.AddListener(HandleTurnCompleted);
            IsInitialized = true;
            if (saveImmediately)
            {
                RequestSave();
                TrySavePending();
            }
        }

        private void Update()
        {
            TrySavePending();
        }

        private void OnDestroy()
        {
            if (runtime != null)
                runtime.PersistentStateChanged -= RequestSave;
            OnActorActionCompleted.RemoveListener(HandleActorActionCompleted);
            OnTurnCompleted.RemoveListener(HandleTurnCompleted);
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

        private void HandleActorActionCompleted(GameObject _)
        {
            RequestSave();
            TrySavePending();
        }

        private void HandleTurnCompleted(GameObject _)
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

            DungeonSaveResult<bool> result;
            try
            {
                result = repository.Save(Capture());
            }
            catch (Exception exception)
            {
                LastDiagnostics = new[]
                {
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.InvalidSnapshot,
                        "autosave.capture",
                        exception.Message
                    ),
                };
                return;
            }

            LastDiagnostics = result.Diagnostics;
            if (result.IsSuccess)
                pending = false;
        }

        private DungeonRunSave Capture()
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
                    return new DungeonPartyMemberSaveState(
                        identity.RosterSlotId,
                        identity.CreatureContentId,
                        cell.x,
                        cell.z,
                        creature.hp,
                        creature.IsDefeated,
                        DungeonActorStateAdapter.Capture(controller, IdentifyActor)
                    );
                })
                .OrderBy(member => member.RosterSlotId, StringComparer.Ordinal)
                .ToArray();

            DungeonRuntimeState runtimeState = runtime.CaptureRuntimeState(controller =>
                DungeonSaveJson.SerializeActor(
                    DungeonActorStateAdapter.Capture(controller, IdentifyActor)
                )
            );
            DungeonLevelDocument floor = BuildFloorDocument(sourceDocument, runtimeState);
            DungeonRunSaveManifest manifest = new(
                DungeonSaveSchema.Version,
                floor.Generation.RunSeed,
                floor.Generation.Algorithm,
                floor.Generation.Depth,
                new DungeonFloorSaveReference(
                    DungeonSaveSchema.Version,
                    DungeonSaveSchema.FloorPath
                ),
                new DungeonPartySaveState(partyState)
            );
            return new DungeonRunSave(manifest, floor);
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
