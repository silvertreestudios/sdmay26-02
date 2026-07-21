using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.DungeonPersistence.Repository
{
    internal static class DungeonRunSaveValidator
    {
        internal static IReadOnlyList<DungeonSaveDiagnostic> Validate(DungeonRunSave save)
        {
            List<DungeonSaveDiagnostic> diagnostics = new();
            if (save == null)
            {
                diagnostics.Add(Invalid("run", "A complete run transaction is required."));
                return diagnostics;
            }

            DungeonRunSaveManifest manifest = save.Manifest;
            if (manifest.DocumentVersion != DungeonSaveSchema.RunManifestVersion)
            {
                diagnostics.Add(
                    Incompatible(
                        "manifest.documentVersion",
                        manifest.DocumentVersion,
                        DungeonSaveSchema.RunManifestVersion
                    )
                );
            }

            Dictionary<int, DungeonFloorSaveState> floorByDepth = save.Floors.ToDictionary(floor =>
                floor.Depth
            );
            Dictionary<int, DungeonLevelDocument> staticFloorByDepth = new();
            HashSet<int> indexedDepths = new();
            foreach (DungeonFloorSaveReference reference in manifest.GeneratedFloors)
            {
                string path = $"manifest.generatedFloors[depth={reference.Depth}]";
                indexedDepths.Add(reference.Depth);
                if (reference.DocumentVersion != DungeonSaveSchema.FloorStateVersion)
                {
                    diagnostics.Add(
                        Incompatible(
                            path + ".documentVersion",
                            reference.DocumentVersion,
                            DungeonSaveSchema.FloorStateVersion
                        )
                    );
                }
                string expectedPath = DungeonFloorSaveReference.CanonicalPath(reference.Depth);
                if (!string.Equals(reference.RelativePath, expectedPath, StringComparison.Ordinal))
                {
                    diagnostics.Add(
                        Invalid(
                            path + ".relativePath",
                            $"Floor path must be the canonical repository path '{expectedPath}'."
                        )
                    );
                }
                if (!floorByDepth.TryGetValue(reference.Depth, out DungeonFloorSaveState floor))
                {
                    diagnostics.Add(
                        Invalid(path, "The generated-floor index has no matching floor document.")
                    );
                    continue;
                }
                if (floor.DocumentVersion != reference.DocumentVersion)
                {
                    diagnostics.Add(
                        Invalid(
                            path + ".documentVersion",
                            "The floor document version does not match its manifest index entry."
                        )
                    );
                }
            }

            foreach (DungeonFloorSaveState floor in save.Floors)
            {
                ValidateFloor(floor, manifest, indexedDepths, diagnostics, staticFloorByDepth);
            }

            DungeonFloorSaveState currentFloor = save.Floors.FirstOrDefault(floor =>
                floor.Depth == manifest.CurrentDepth
            );
            if (currentFloor != null)
            {
                ValidateCurrentParty(manifest, currentFloor, staticFloorByDepth, diagnostics);
            }
            return diagnostics;
        }

        internal static void RequireValid(DungeonRunSave save)
        {
            IReadOnlyList<DungeonSaveDiagnostic> diagnostics = Validate(save);
            if (diagnostics.Count > 0)
                throw new ArgumentException(diagnostics[0].Message, diagnostics[0].Path);
        }

        private static void ValidateFloor(
            DungeonFloorSaveState floor,
            DungeonRunSaveManifest manifest,
            ISet<int> indexedDepths,
            ICollection<DungeonSaveDiagnostic> diagnostics,
            IDictionary<int, DungeonLevelDocument> staticFloorByDepth
        )
        {
            string floorPath = $"floors[depth={floor.Depth}]";
            if (floor.DocumentVersion != DungeonSaveSchema.FloorStateVersion)
            {
                diagnostics.Add(
                    Incompatible(
                        floorPath + ".documentVersion",
                        floor.DocumentVersion,
                        DungeonSaveSchema.FloorStateVersion
                    )
                );
            }
            if (!indexedDepths.Contains(floor.Depth))
                diagnostics.Add(
                    Invalid(floorPath, "A floor document is not present in the manifest index.")
                );

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(floor.StaticFloorJson);
            if (!parsed.IsSuccess)
            {
                diagnostics.Add(
                    Invalid(
                        floorPath + ".staticFloorJson",
                        "Static topology JSON is not a valid generator document."
                    )
                );
                return;
            }

            DungeonLevelDocument staticFloor = parsed.Document;
            if (
                staticFloor.RuntimeState != null
                || staticFloor.Generation.Depth != floor.Depth
                || staticFloor.Generation.RunSeed != manifest.StartingSeed
                || !string.Equals(
                    staticFloor.Generation.Algorithm,
                    manifest.GeneratorVersion,
                    StringComparison.Ordinal
                )
            )
            {
                diagnostics.Add(
                    Invalid(
                        floorPath + ".staticFloorJson",
                        "Static topology must omit runtime state and match the floor depth, run seed, and generator version."
                    )
                );
                return;
            }
            staticFloorByDepth.Add(floor.Depth, staticFloor);

            HashSet<string> staticDoorIds = new(
                staticFloor.Doors.Select(door => door.Id),
                StringComparer.Ordinal
            );
            HashSet<string> mutableDoorIds = new(
                floor.Doors.Select(door => door.DoorId),
                StringComparer.Ordinal
            );
            HashSet<string> staticEncounterIds = new(
                staticFloor.EncounterPlans.Select(encounter => encounter.Id),
                StringComparer.Ordinal
            );
            HashSet<string> mutableEncounterIds = new(
                floor.Encounters.Select(encounter => encounter.EncounterId),
                StringComparer.Ordinal
            );
            if (
                staticFloor.Doors.Any(door => door.IsOpen)
                || staticFloor.EncounterPlans.Any(encounter => encounter.IsResolved)
                || !staticDoorIds.SetEquals(mutableDoorIds)
                || !staticEncounterIds.SetEquals(mutableEncounterIds)
            )
            {
                diagnostics.Add(
                    Invalid(
                        floorPath,
                        "Static topology must be pristine and its door and encounter IDs must exactly match mutable floor state."
                    )
                );
            }

            Dictionary<string, DungeonEncounterPlan> planById =
                staticFloor.EncounterPlans.ToDictionary(plan => plan.Id, StringComparer.Ordinal);
            foreach (DungeonEncounterSaveState encounter in floor.Encounters)
            {
                if (!planById.TryGetValue(encounter.EncounterId, out DungeonEncounterPlan plan))
                    continue;
                DungeonEncounterCreatureSaveState[] persisted = floor
                    .Creatures.Where(creature => creature.EncounterId == encounter.EncounterId)
                    .ToArray();
                if (encounter.Status == DungeonEncounterSaveStatus.Dormant)
                    continue;
                if (persisted.Length != plan.CreatureIds.Count)
                {
                    diagnostics.Add(
                        Invalid(
                            floorPath + ".creatures",
                            $"Materialized encounter '{plan.Id}' must persist every planned creature exactly once."
                        )
                    );
                    continue;
                }
                Dictionary<string, DungeonEncounterCreatureSaveState> persistedById =
                    persisted.ToDictionary(
                        creature => creature.Creature.InstanceId,
                        StringComparer.Ordinal
                    );
                for (int index = 0; index < plan.CreatureIds.Count; index++)
                {
                    string expectedId = DungeonCreatureInstanceIdentity.Create(plan.Id, index);
                    if (
                        !persistedById.TryGetValue(
                            expectedId,
                            out DungeonEncounterCreatureSaveState creature
                        )
                        || creature.EncounterId != plan.Id
                        || creature.Creature.CreatureContentId != plan.CreatureIds[index]
                    )
                    {
                        diagnostics.Add(
                            Invalid(
                                floorPath + ".creatures",
                                $"Encounter '{plan.Id}' creature {index} must preserve its plan-derived instance, encounter, and content identities."
                            )
                        );
                    }
                }
            }
            if (
                floor
                    .Creatures.Where(creature => !creature.Creature.IsDefeated)
                    .Any(creature => !IsWalkable(staticFloor, floor, creature.Creature.Cell))
            )
            {
                diagnostics.Add(
                    Invalid(
                        floorPath + ".creatures",
                        "Every living encounter creature must occupy a walkable static-floor cell."
                    )
                );
            }
        }

        private static void ValidateCurrentParty(
            DungeonRunSaveManifest manifest,
            DungeonFloorSaveState currentFloor,
            IReadOnlyDictionary<int, DungeonLevelDocument> staticFloorByDepth,
            ICollection<DungeonSaveDiagnostic> diagnostics
        )
        {
            string[] occupiedCells = manifest
                .Party.Members.Where(member => !member.Creature.IsDefeated)
                .Select(member => CellKey(member.Creature.Cell))
                .Concat(
                    currentFloor
                        .Creatures.Where(creature => !creature.Creature.IsDefeated)
                        .Select(creature => CellKey(creature.Creature.Cell))
                )
                .ToArray();
            if (occupiedCells.Distinct(StringComparer.Ordinal).Count() != occupiedCells.Length)
            {
                diagnostics.Add(
                    Invalid(
                        "manifest.currentDepth",
                        "Living party and encounter creatures cannot occupy the same current-floor cell."
                    )
                );
            }

            HashSet<string> partyIds = new(
                manifest.Party.Members.Select(member => member.Creature.InstanceId),
                StringComparer.Ordinal
            );
            if (
                currentFloor.Creatures.Any(creature =>
                    partyIds.Contains(creature.Creature.InstanceId)
                )
            )
            {
                diagnostics.Add(
                    Invalid(
                        "manifest.currentDepth",
                        "Party IDs cannot collide with current-floor encounter creature IDs."
                    )
                );
            }
            if (
                staticFloorByDepth.TryGetValue(
                    manifest.CurrentDepth,
                    out DungeonLevelDocument staticCurrentFloor
                )
                && manifest
                    .Party.Members.Where(member => !member.Creature.IsDefeated)
                    .Any(member =>
                        !IsWalkable(staticCurrentFloor, currentFloor, member.Creature.Cell)
                    )
            )
            {
                diagnostics.Add(
                    Invalid(
                        "manifest.party",
                        "Every living party member must occupy a walkable current-floor cell."
                    )
                );
            }
        }

        private static DungeonSaveDiagnostic Invalid(string path, string message) =>
            new(
                DungeonSaveDiagnosticCode.InvalidSnapshot,
                DungeonSaveDiagnosticSeverity.Error,
                path,
                message
            );

        private static DungeonSaveDiagnostic Incompatible(string path, int actual, int expected) =>
            new(
                DungeonSaveDiagnosticCode.IncompatibleVersion,
                DungeonSaveDiagnosticSeverity.Error,
                path,
                $"Document version {actual} is incompatible with required version {expected}."
            );

        private static string CellKey(DungeonSaveCell cell) => cell.X + ":" + cell.Z;

        private static bool IsWalkable(
            DungeonLevelDocument document,
            DungeonFloorSaveState floor,
            DungeonSaveCell cell
        )
        {
            if (cell.X < 0 || cell.X >= document.Width || cell.Z < 0 || cell.Z >= document.Height)
                return false;
            char symbol = document.Rows[document.Height - 1 - cell.Z][cell.X];
            if (symbol == '.')
                return true;
            if (symbol != 'D')
                return false;
            DungeonDoor door = document.Doors.FirstOrDefault(candidate =>
                candidate.Cell.X == cell.X && candidate.Cell.Z == cell.Z
            );
            return door != null
                && floor.Doors.Any(state => state.DoorId == door.Id && state.IsOpen);
        }
    }
}
