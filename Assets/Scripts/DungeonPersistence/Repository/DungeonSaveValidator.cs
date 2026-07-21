using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;

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

            Dictionary<int, DungeonFloorSaveState> floors = save.Floors.ToDictionary(floor =>
                floor.Depth
            );
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
                    diagnostics.Add(Invalid(path + ".relativePath", $"Expected '{expectedPath}'."));
                if (!floors.TryGetValue(reference.Depth, out DungeonFloorSaveState floor))
                {
                    diagnostics.Add(Invalid(path, "The indexed floor document is missing."));
                    continue;
                }
                if (floor.DocumentVersion != reference.DocumentVersion)
                {
                    diagnostics.Add(
                        Invalid(
                            path + ".documentVersion",
                            "The floor document version does not match its index."
                        )
                    );
                }
            }
            foreach (DungeonFloorSaveState floor in save.Floors)
            {
                if (!indexedDepths.Contains(floor.Depth))
                    diagnostics.Add(Invalid($"floors[{floor.Depth}]", "The floor is not indexed."));
            }
            if (indexedDepths.Count != floors.Count)
                diagnostics.Add(
                    Invalid("manifest.generatedFloors", "Floor coverage is incomplete.")
                );

            Dictionary<int, DungeonLevelDocument> documents = new();
            Dictionary<int, IReadOnlyList<DungeonCreatureSaveState>> enemies = new();
            foreach (DungeonFloorSaveState floor in save.Floors)
                ValidateFloor(floor, manifest, documents, enemies, diagnostics);

            if (
                documents.TryGetValue(manifest.CurrentDepth, out DungeonLevelDocument current)
                && enemies.TryGetValue(
                    manifest.CurrentDepth,
                    out IReadOnlyList<DungeonCreatureSaveState> currentEnemies
                )
            )
            {
                ValidateCurrentParty(manifest.Party, current, currentEnemies, diagnostics);
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
            IDictionary<int, DungeonLevelDocument> documents,
            IDictionary<int, IReadOnlyList<DungeonCreatureSaveState>> enemies,
            ICollection<DungeonSaveDiagnostic> diagnostics
        )
        {
            string path = $"floors[depth={floor.Depth}]";
            if (floor.DocumentVersion != DungeonSaveSchema.FloorStateVersion)
            {
                diagnostics.Add(
                    Incompatible(
                        path + ".documentVersion",
                        floor.DocumentVersion,
                        DungeonSaveSchema.FloorStateVersion
                    )
                );
            }

            DungeonLevelDocument document;
            try
            {
                document = floor.ParseDocument();
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                diagnostics.Add(
                    Invalid(path, "The floor document is invalid: " + exception.Message)
                );
                return;
            }

            if (
                document.Generation.Depth != floor.Depth
                || document.Generation.RunSeed != manifest.StartingSeed
                || !string.Equals(
                    document.Generation.Algorithm,
                    manifest.GeneratorVersion,
                    StringComparison.Ordinal
                )
            )
            {
                diagnostics.Add(
                    Invalid(
                        path,
                        "The floor must match the indexed depth, run seed, and generator version."
                    )
                );
                return;
            }

            List<DungeonCreatureSaveState> actorStates = new();
            foreach (DungeonCreatureRuntimeState creature in document.RuntimeState.Creatures)
            {
                DungeonSaveResult<DungeonCreatureSaveState> parsed =
                    DungeonSaveJsonCodec.ParseCreature(creature.State);
                if (!parsed.IsSuccess)
                {
                    diagnostics.Add(
                        Invalid(
                            path + ".runtimeState.creatures",
                            $"Creature '{creature.InstanceId}' has invalid child state."
                        )
                    );
                    continue;
                }

                DungeonCreatureSaveState actor = parsed.Value;
                if (
                    actor.InstanceId != creature.InstanceId
                    || actor.CreatureContentId != creature.CreatureId
                    || actor.Cell != new DungeonSaveCell(creature.Cell.X, creature.Cell.Z)
                    || actor.Health.CurrentHitPoints != creature.HitPoints
                    || actor.IsDefeated
                )
                {
                    diagnostics.Add(
                        Invalid(
                            path + ".runtimeState.creatures",
                            $"Creature '{creature.InstanceId}' child state disagrees with the floor."
                        )
                    );
                    continue;
                }
                actorStates.Add(actor);
            }

            try
            {
                DungeonActorStateAdapter.ValidateForRestore(actorStates);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                diagnostics.Add(
                    Invalid(
                        path + ".runtimeState.creatures",
                        "Actor state is invalid: " + exception.Message
                    )
                );
            }
            documents[floor.Depth] = document;
            enemies[floor.Depth] = actorStates.AsReadOnly();
        }

        private static void ValidateCurrentParty(
            DungeonPartySaveState party,
            DungeonLevelDocument document,
            IReadOnlyList<DungeonCreatureSaveState> enemies,
            ICollection<DungeonSaveDiagnostic> diagnostics
        )
        {
            DungeonCreatureSaveState[] partyActors = party
                .Members.Select(member => member.Creature)
                .ToArray();
            try
            {
                DungeonActorStateAdapter.ValidateForRestore(partyActors.Concat(enemies));
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                diagnostics.Add(
                    Invalid("manifest.party", "Actor state is invalid: " + exception.Message)
                );
            }

            DungeonCreatureSaveState[] livingParty = partyActors
                .Where(actor => !actor.IsDefeated)
                .ToArray();
            if (livingParty.Any(actor => !IsWalkable(document, actor.Cell)))
            {
                diagnostics.Add(
                    Invalid(
                        "manifest.party",
                        "Every living party member must occupy a walkable current-floor cell."
                    )
                );
            }

            string[] livingCells = livingParty
                .Select(actor => CellKey(actor.Cell))
                .Concat(enemies.Select(actor => CellKey(actor.Cell)))
                .ToArray();
            if (livingCells.Distinct(StringComparer.Ordinal).Count() != livingCells.Length)
            {
                diagnostics.Add(
                    Invalid(
                        "manifest.currentDepth",
                        "Living party and encounter actors cannot share a cell."
                    )
                );
            }

            HashSet<string> partyIds = new(
                partyActors.Select(actor => actor.InstanceId),
                StringComparer.Ordinal
            );
            if (enemies.Any(actor => partyIds.Contains(actor.InstanceId)))
            {
                diagnostics.Add(
                    Invalid(
                        "manifest.currentDepth",
                        "Party and encounter actor identities cannot collide."
                    )
                );
            }
        }

        private static bool IsWalkable(DungeonLevelDocument document, DungeonSaveCell cell)
        {
            if (cell.X < 0 || cell.X >= document.Width || cell.Z < 0 || cell.Z >= document.Height)
                return false;
            char symbol = document.Rows[document.Height - 1 - cell.Z][cell.X];
            if (symbol == '.')
                return true;
            if (symbol != 'D')
                return false;
            return document.Doors.Any(door =>
                door.IsOpen && door.Cell.X == cell.X && door.Cell.Z == cell.Z
            );
        }

        private static string CellKey(DungeonSaveCell cell) => cell.X + ":" + cell.Z;

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
    }
}
