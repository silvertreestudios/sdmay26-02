using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Floors
{
    /// <summary>Contains the party and current floor captured at one stable autosave boundary.</summary>
    public sealed class DungeonCurrentFloorCapture
    {
        /// <summary>Creates one complete current-floor capture.</summary>
        /// <param name="party">The ordered party and selected exploration leader.</param>
        /// <param name="floor">The exact mutable state for the party's current floor.</param>
        public DungeonCurrentFloorCapture(DungeonPartySaveState party, DungeonFloorSaveState floor)
        {
            Party = party ?? throw new ArgumentNullException(nameof(party));
            Floor = floor ?? throw new ArgumentNullException(nameof(floor));
        }

        /// <summary>Gets the complete ordered party state.</summary>
        public DungeonPartySaveState Party { get; }

        /// <summary>Gets the complete current-floor state.</summary>
        public DungeonFloorSaveState Floor { get; }
    }

    /// <summary>
    /// Captures one live dungeon runtime without deriving identity from scene serialization,
    /// object names, or Unity instance IDs.
    /// </summary>
    public static class DungeonCurrentFloorCaptureService
    {
        /// <summary>Captures a newly generated floor that has no prior persisted actor records.</summary>
        /// <param name="staticFloorJson">Validated pristine generator JSON for this floor.</param>
        /// <param name="runtime">The initialized runtime owning party, doors, and encounters.</param>
        /// <returns>The complete party and floor transaction inputs.</returns>
        public static DungeonCurrentFloorCapture CaptureNew(
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime
        ) => Capture(staticFloorJson, runtime, Array.Empty<DungeonEncounterCreatureSaveState>());

        /// <summary>
        /// Captures a loaded floor while preserving rich records for defeated actors that are no
        /// longer materialized in the scene.
        /// </summary>
        /// <param name="staticFloorJson">Validated pristine generator JSON for this floor.</param>
        /// <param name="runtime">The initialized runtime owning party, doors, and encounters.</param>
        /// <param name="previousFloor">The prior complete state for the same depth and topology.</param>
        /// <returns>The complete party and updated floor transaction inputs.</returns>
        public static DungeonCurrentFloorCapture CaptureExisting(
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime,
            DungeonFloorSaveState previousFloor
        )
        {
            if (previousFloor == null)
                throw new ArgumentNullException(nameof(previousFloor));
            DungeonLevelDocument source = ParseStaticFloor(staticFloorJson);
            if (
                previousFloor.Depth != source.Generation.Depth
                || !string.Equals(
                    previousFloor.StaticFloorJson,
                    DungeonLevelJsonSerializer.Serialize(source),
                    StringComparison.Ordinal
                )
            )
            {
                throw new ArgumentException(
                    "The prior floor state must own the same depth and static topology.",
                    nameof(previousFloor)
                );
            }
            return Capture(staticFloorJson, runtime, previousFloor.Creatures);
        }

        private static DungeonCurrentFloorCapture Capture(
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime,
            IEnumerable<DungeonEncounterCreatureSaveState> priorCreatures
        )
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (!runtime.IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon runtime must be initialized before it can be captured."
                );

            DungeonLevelDocument source = ParseStaticFloor(staticFloorJson);
            IReadOnlyList<ActionController> partyControllers = runtime.CapturePartyControllers();
            IReadOnlyList<DungeonEncounterCreatureCapture> encounterActors =
                runtime.CaptureMaterializedCreatures();
            if (
                partyControllers.Any(controller => controller == null)
                || encounterActors.Any(actor => actor.Controller == null)
            )
            {
                throw new InvalidOperationException(
                    "Dungeon autosave cannot capture a configured actor that is no longer materialized."
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
                    encounterActors.Select(actor => new DungeonActorCaptureTarget(
                        actor.Controller,
                        actor.InstanceId,
                        actor.CreatureContentId
                    ))
                )
                .ToArray();
            IReadOnlyDictionary<string, DungeonCreatureSaveState> stateByActorId =
                DungeonActorStateAdapter
                    .Capture(actorTargets)
                    .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal);

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
                    stateByActorId[target.Identity.ActorInstanceId]
                ))
            );

            DungeonEncounterLifecycleSnapshot lifecycle = runtime.CaptureSnapshot();
            IReadOnlyDictionary<string, DungeonEncounterGroupSnapshot> lifecycleById =
                lifecycle.Groups.ToDictionary(group => group.EncounterId, StringComparer.Ordinal);
            Dictionary<string, DungeonEncounterCreatureSaveState> currentById = encounterActors
                .Select(actor =>
                {
                    DungeonCreatureSaveState state = stateByActorId[actor.InstanceId];
                    if (
                        state.Cell != new DungeonSaveCell(actor.Cell.X, actor.Cell.Z)
                        || state.IsDefeated != actor.IsDefeated
                    )
                    {
                        throw new InvalidOperationException(
                            $"Captured actor '{actor.InstanceId}' changed during autosave capture."
                        );
                    }
                    return new DungeonEncounterCreatureSaveState(actor.EncounterId, state);
                })
                .ToDictionary(actor => actor.Creature.InstanceId, StringComparer.Ordinal);
            IReadOnlyDictionary<string, DungeonEncounterCreatureSaveState> priorById =
                priorCreatures.ToDictionary(
                    actor => actor.Creature.InstanceId,
                    StringComparer.Ordinal
                );

            List<DungeonEncounterCreatureSaveState> floorCreatures = new();
            foreach (DungeonEncounterPlan plan in source.EncounterPlans)
            {
                if (!lifecycleById.TryGetValue(plan.Id, out DungeonEncounterGroupSnapshot group))
                    throw new InvalidOperationException(
                        $"Encounter lifecycle omitted planned encounter '{plan.Id}'."
                    );
                if (group.State == DungeonEncounterGroupState.Dormant)
                    continue;

                HashSet<string> defeatedIds = new(
                    group.DefeatedCreatureInstanceIds,
                    StringComparer.Ordinal
                );
                for (int index = 0; index < plan.CreatureIds.Count; index++)
                {
                    string instanceId = DungeonCreatureInstanceIdentity.Create(plan.Id, index);
                    DungeonEncounterCreatureSaveState saved;
                    if (
                        currentById.TryGetValue(
                            instanceId,
                            out DungeonEncounterCreatureSaveState current
                        )
                    )
                    {
                        saved = current;
                    }
                    else if (
                        defeatedIds.Contains(instanceId)
                        && priorById.TryGetValue(
                            instanceId,
                            out DungeonEncounterCreatureSaveState prior
                        )
                        && prior.Creature.IsDefeated
                    )
                    {
                        saved = prior;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Touched encounter '{plan.Id}' has no exact state for actor '{instanceId}'."
                        );
                    }

                    if (
                        !string.Equals(saved.EncounterId, plan.Id, StringComparison.Ordinal)
                        || !string.Equals(
                            saved.Creature.CreatureContentId,
                            plan.CreatureIds[index],
                            StringComparison.Ordinal
                        )
                        || saved.Creature.IsDefeated != defeatedIds.Contains(instanceId)
                    )
                    {
                        throw new InvalidOperationException(
                            $"Saved actor '{instanceId}' does not match its immutable encounter plan."
                        );
                    }
                    floorCreatures.Add(saved);
                }
            }

            HashSet<string> openDoorIds = new(runtime.CaptureOpenDoorIds(), StringComparer.Ordinal);
            DungeonFloorSaveState floor = new(
                DungeonSaveSchema.FloorStateVersion,
                source.Generation.Depth,
                DungeonLevelJsonSerializer.Serialize(source),
                source.Doors.Select(door => new DungeonDoorSaveState(
                    door.Id,
                    openDoorIds.Contains(door.Id)
                )),
                lifecycle.Groups.Select(group => new DungeonEncounterSaveState(
                    group.EncounterId,
                    SaveStatus(group.State)
                )),
                floorCreatures
            );
            return new DungeonCurrentFloorCapture(party, floor);
        }

        private static DungeonLevelDocument ParseStaticFloor(string staticFloorJson)
        {
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(staticFloorJson);
            if (!parsed.IsSuccess)
            {
                throw new ArgumentException(
                    "Static floor JSON is invalid: "
                        + string.Join(" ", parsed.Diagnostics.Select(item => item.Message)),
                    nameof(staticFloorJson)
                );
            }
            if (parsed.Document.RuntimeState != null)
                throw new ArgumentException(
                    "Static floor JSON cannot contain mutable runtime state.",
                    nameof(staticFloorJson)
                );
            return parsed.Document;
        }

        private static PartyTarget RequirePartyTarget(ActionController controller)
        {
            if (controller == null)
                throw new InvalidOperationException("The dungeon party lost a configured actor.");
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

        private static DungeonEncounterSaveStatus SaveStatus(DungeonEncounterGroupState state) =>
            state switch
            {
                DungeonEncounterGroupState.Dormant => DungeonEncounterSaveStatus.Dormant,
                DungeonEncounterGroupState.Active => DungeonEncounterSaveStatus.Active,
                DungeonEncounterGroupState.Suspended => DungeonEncounterSaveStatus.Suspended,
                DungeonEncounterGroupState.Cleared => DungeonEncounterSaveStatus.Cleared,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(state),
                    state,
                    "Encounter lifecycle state is undefined."
                ),
            };

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
