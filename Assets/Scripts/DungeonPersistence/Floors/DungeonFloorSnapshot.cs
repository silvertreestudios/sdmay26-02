using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Floors
{
    /// <summary>Pairs one stable party snapshot with its complete current-floor document.</summary>
    internal sealed class DungeonCurrentFloorCapture
    {
        /// <summary>Creates one current-floor transaction input.</summary>
        public DungeonCurrentFloorCapture(DungeonPartySaveState party, DungeonFloorSaveState floor)
        {
            Party = party ?? throw new ArgumentNullException(nameof(party));
            Floor = floor ?? throw new ArgumentNullException(nameof(floor));
        }

        /// <summary>Gets the complete current party state.</summary>
        public DungeonPartySaveState Party { get; }

        /// <summary>Gets the complete generated-level document for the current depth.</summary>
        public DungeonFloorSaveState Floor { get; }
    }

    /// <summary>
    /// Captures live Unity state into the existing generated-level runtime-state contract.
    /// </summary>
    internal static class DungeonCurrentFloorCaptureService
    {
        /// <summary>Captures a generated depth that has not previously committed.</summary>
        public static DungeonCurrentFloorCapture CaptureNew(
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime
        ) => Capture(staticFloorJson, runtime);

        /// <summary>
        /// Recaptures a committed depth. The previous document is used only to prove that callers
        /// are replacing the same depth; complete current state comes from the live runtime.
        /// </summary>
        public static DungeonCurrentFloorCapture CaptureExisting(
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime,
            DungeonFloorSaveState previousFloor
        )
        {
            if (previousFloor == null)
                throw new ArgumentNullException(nameof(previousFloor));
            DungeonLevelDocument source = ParsePristineFloor(staticFloorJson);
            if (source.Generation.Depth != previousFloor.Depth)
                throw new ArgumentException(
                    "The previous floor must match the recaptured depth.",
                    nameof(previousFloor)
                );
            return Capture(source, runtime);
        }

        private static DungeonCurrentFloorCapture Capture(
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime
        ) => Capture(ParsePristineFloor(staticFloorJson), runtime);

        private static DungeonCurrentFloorCapture Capture(
            DungeonLevelDocument source,
            DungeonEncounterRuntimeController runtime
        )
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (!runtime.IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon runtime must be initialized before it can be captured."
                );

            IReadOnlyList<ActionController> partyControllers = runtime.CapturePartyControllers();
            IReadOnlyList<DungeonEncounterCreatureCapture> encounterActors =
                runtime.CaptureMaterializedCreatures();
            if (
                partyControllers.Any(controller => controller == null)
                || encounterActors.Any(actor => actor.Controller == null)
            )
            {
                throw new InvalidOperationException(
                    "Dungeon autosave cannot capture an actor that is no longer materialized."
                );
            }
            if (
                partyControllers.Any(controller => controller.IsTakingAction)
                || encounterActors.Any(actor => actor.Controller.IsTakingAction)
            )
            {
                throw new InvalidOperationException(
                    "Dungeon autosave capture must wait until every action has completed."
                );
            }

            PartyTarget[] partyTargets = partyControllers.Select(RequirePartyTarget).ToArray();
            DungeonActorCaptureTarget[] actorTargets = partyTargets
                .Select(target => target.Actor)
                .Concat(
                    encounterActors
                        .Where(actor => !actor.IsDefeated)
                        .Select(actor => new DungeonActorCaptureTarget(
                            actor.Controller,
                            actor.InstanceId,
                            actor.CreatureContentId
                        ))
                )
                .ToArray();
            IReadOnlyList<DungeonCreatureSaveState> captured = DungeonActorStateAdapter.Capture(
                actorTargets
            );
            IReadOnlyDictionary<string, DungeonCreatureSaveState> stateById = captured.ToDictionary(
                state => state.InstanceId,
                StringComparer.Ordinal
            );
            IReadOnlyDictionary<ActionController, string> tokenByController =
                actorTargets.ToDictionary(
                    target => target.Controller,
                    target => DungeonSaveJsonCodec.SerializeCreature(stateById[target.InstanceId])
                );

            foreach (
                DungeonEncounterCreatureCapture actor in encounterActors.Where(actor =>
                    !actor.IsDefeated
                )
            )
            {
                DungeonCreatureSaveState state = stateById[actor.InstanceId];
                if (
                    state.Cell != new DungeonSaveCell(actor.Cell.X, actor.Cell.Z)
                    || state.IsDefeated
                )
                {
                    throw new InvalidOperationException(
                        $"Captured actor '{actor.InstanceId}' changed during autosave capture."
                    );
                }
            }

            string leaderRosterSlotId = string.Empty;
            if (runtime.TryCaptureExplorationLeader(out ActionController leader))
            {
                PartyTarget leaderTarget = partyTargets.Single(target =>
                    target.Actor.Controller == leader
                );
                leaderRosterSlotId = leaderTarget.Identity.RosterSlotId;
            }
            DungeonPartySaveState party = new(
                leaderRosterSlotId,
                partyTargets.Select(target => new DungeonPartyMemberSaveState(
                    target.Identity.RosterSlotId,
                    stateById[target.Identity.ActorInstanceId]
                ))
            );

            DungeonRuntimeState runtimeState = runtime.CaptureRuntimeState(controller =>
                tokenByController.TryGetValue(controller, out string token)
                    ? token
                    : throw new InvalidOperationException(
                        "The encounter runtime requested state for an uncaptured actor."
                    )
            );
            HashSet<string> openDoors = new(runtimeState.OpenDoorIds, StringComparer.Ordinal);
            HashSet<string> resolvedEncounters = new(
                runtimeState.ResolvedEncounterIds,
                StringComparer.Ordinal
            );
            DungeonLevelDocument complete = new(
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
            DungeonFloorSaveState floor = new(
                DungeonSaveSchema.FloorStateVersion,
                source.Generation.Depth,
                DungeonLevelJsonSerializer.Serialize(complete)
            );
            return new DungeonCurrentFloorCapture(party, floor);
        }

        private static DungeonLevelDocument ParsePristineFloor(string json)
        {
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);
            if (!parsed.IsSuccess)
            {
                throw new ArgumentException(
                    "Static floor JSON is invalid: "
                        + string.Join(" ", parsed.Diagnostics.Select(item => item.Message)),
                    nameof(json)
                );
            }
            DungeonLevelDocument document = parsed.Document;
            if (document.RuntimeState == null)
                return document;
            return new DungeonLevelDocument(
                document.Generation,
                document.Rows,
                document.Rooms,
                document.Doors.Select(door => new DungeonDoor(door.Id, door.Cell, false)),
                document.Stairs,
                document.StartCell,
                document.SafeCells,
                document.Objects,
                document.EncounterPlans.Select(plan => new DungeonEncounterPlan(
                    plan.Id,
                    plan.RoomId,
                    plan.Threat,
                    plan.Budget,
                    plan.SpawnCells,
                    plan.CreatureIds
                ))
            );
        }

        private static PartyTarget RequirePartyTarget(ActionController controller)
        {
            DungeonPartyMemberIdentity identity =
                controller.GetComponent<DungeonPartyMemberIdentity>();
            if (identity == null || !identity.IsConfigured)
            {
                throw new InvalidOperationException(
                    $"Party actor '{controller.name}' requires configured stable dungeon identity."
                );
            }
            return new PartyTarget(
                identity,
                new DungeonActorCaptureTarget(
                    controller,
                    identity.ActorInstanceId,
                    identity.CreatureContentId
                )
            );
        }

        private sealed class PartyTarget
        {
            internal PartyTarget(
                DungeonPartyMemberIdentity identity,
                DungeonActorCaptureTarget actor
            )
            {
                Identity = identity;
                Actor = actor;
            }

            internal DungeonPartyMemberIdentity Identity { get; }

            internal DungeonActorCaptureTarget Actor { get; }
        }
    }
}
