using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    public sealed partial class FileSystemDungeonSaveRepository
    {
        /// <inheritdoc/>
        public DungeonSaveLoadResult Load()
        {
            lock (sync)
            {
                GenerationAttempt current = LoadPointerGeneration(
                    CurrentPointerPath,
                    missingIsCorrupt: false
                );
                if (current is GenerationSuccess currentSuccess)
                {
                    return new DungeonSaveLoadSuccess(
                        currentSuccess.Save,
                        Array.Empty<DungeonSaveDiagnostic>()
                    );
                }

                GenerationFailure currentFailure = (GenerationFailure)current;
                if (!File.Exists(PreviousPointerPath))
                    return new DungeonSaveLoadFailure(new[] { currentFailure.Diagnostic });

                GenerationAttempt previous = LoadPointerGeneration(
                    PreviousPointerPath,
                    missingIsCorrupt: true
                );
                if (previous is GenerationSuccess previousSuccess)
                {
                    return new DungeonSaveLoadSuccess(
                        previousSuccess.Save,
                        new[]
                        {
                            AsWarning(currentFailure.Diagnostic),
                            new DungeonSaveDiagnostic(
                                DungeonSaveDiagnosticCode.RecoveredPreviousGeneration,
                                DungeonSaveDiagnosticSeverity.Warning,
                                "previous.json",
                                "The current autosave was unusable, so the prior committed generation was recovered."
                            ),
                        }
                    );
                }

                GenerationFailure previousFailure = (GenerationFailure)previous;
                return new DungeonSaveLoadFailure(
                    new[] { currentFailure.Diagnostic, previousFailure.Diagnostic }
                );
            }
        }

        private GenerationAttempt LoadPointerGeneration(string pointerPath, bool missingIsCorrupt)
        {
            PointerAttempt pointer = ReadPointer(pointerPath, missingIsCorrupt);
            if (pointer is PointerFailure failure)
                return new GenerationFailure(failure.Diagnostic);
            PointerSuccess success = (PointerSuccess)pointer;
            return LoadGeneration(
                Path.Combine(GenerationsPath, success.GenerationId),
                success.GenerationId
            );
        }

        private static PointerAttempt ReadPointer(string path, bool missingIsCorrupt)
        {
            string logicalPath = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                return new PointerFailure(
                    new DungeonSaveDiagnostic(
                        missingIsCorrupt
                            ? DungeonSaveDiagnosticCode.CorruptSave
                            : DungeonSaveDiagnosticCode.MissingSave,
                        DungeonSaveDiagnosticSeverity.Error,
                        logicalPath,
                        missingIsCorrupt
                            ? "A committed recovery pointer is missing."
                            : "No committed dungeon autosave exists."
                    )
                );
            }

            try
            {
                JObject root = JObject.Parse(
                    File.ReadAllText(path),
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    }
                );
                string[] properties = root.Properties().Select(property => property.Name).ToArray();
                if (
                    properties.Length != 2
                    || !properties.Contains("pointerVersion", StringComparer.Ordinal)
                    || !properties.Contains("generationId", StringComparer.Ordinal)
                    || root["pointerVersion"].Type != JTokenType.Integer
                    || root["generationId"].Type != JTokenType.String
                )
                {
                    throw new JsonException(
                        "The pointer requires exactly integer pointerVersion and string generationId properties."
                    );
                }

                int version = root["pointerVersion"].Value<int>();
                if (version != PointerVersion)
                {
                    return new PointerFailure(
                        new DungeonSaveDiagnostic(
                            DungeonSaveDiagnosticCode.IncompatibleVersion,
                            DungeonSaveDiagnosticSeverity.Error,
                            logicalPath + ".pointerVersion",
                            $"Pointer version {version} is incompatible with required version {PointerVersion}."
                        )
                    );
                }

                string generationId = root["generationId"].Value<string>();
                if (!IsGenerationId(generationId))
                    throw new JsonException("The generation ID must be a lowercase SHA-256 value.");
                return new PointerSuccess(generationId);
            }
            catch (Exception exception)
                when (exception is JsonException
                    || exception is FormatException
                    || exception is OverflowException
                )
            {
                return new PointerFailure(
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.CorruptSave,
                        DungeonSaveDiagnosticSeverity.Error,
                        logicalPath,
                        "The generation pointer is invalid: " + exception.Message
                    )
                );
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return new PointerFailure(
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.IoFailure,
                        DungeonSaveDiagnosticSeverity.Error,
                        logicalPath,
                        "The generation pointer could not be read: " + exception.Message
                    )
                );
            }
        }

        private static GenerationAttempt LoadGeneration(
            string generationPath,
            string expectedGenerationId
        )
        {
            try
            {
                if (!Directory.Exists(generationPath))
                {
                    return CorruptGeneration(
                        "generation",
                        "The committed generation directory is missing."
                    );
                }

                string manifestPath = Path.Combine(generationPath, ManifestFileName);
                if (!File.Exists(manifestPath))
                    return CorruptGeneration(ManifestFileName, "The run manifest is missing.");
                string manifestJson = File.ReadAllText(manifestPath);
                DungeonSaveParseResult<DungeonRunSaveManifest> manifestResult =
                    DungeonSaveJsonCodec.ParseRunManifest(manifestJson);
                if (manifestResult is DungeonSaveParseFailure<DungeonRunSaveManifest>)
                    return ParseFailure(manifestResult.Diagnostics[0], ManifestFileName);
                DungeonRunSaveManifest manifest = (
                    (DungeonSaveParseSuccess<DungeonRunSaveManifest>)manifestResult
                ).Value;

                Dictionary<string, string> floorJsonByPath = new(StringComparer.Ordinal);
                List<DungeonFloorSaveState> floors = new(manifest.GeneratedFloors.Count);
                foreach (DungeonFloorSaveReference reference in manifest.GeneratedFloors)
                {
                    string canonicalPath = DungeonFloorSaveReference.CanonicalPath(reference.Depth);
                    if (
                        !string.Equals(
                            reference.RelativePath,
                            canonicalPath,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return CorruptGeneration(
                            "manifest.generatedFloors",
                            $"Floor depth {reference.Depth} does not use canonical path '{canonicalPath}'."
                        );
                    }
                    string floorPath = ResolveGenerationPath(
                        generationPath,
                        reference.RelativePath
                    );
                    if (!File.Exists(floorPath))
                    {
                        return CorruptGeneration(
                            reference.RelativePath,
                            "An indexed floor document is missing."
                        );
                    }
                    string floorJson = File.ReadAllText(floorPath);
                    DungeonSaveParseResult<DungeonFloorSaveState> floorResult =
                        DungeonSaveJsonCodec.ParseFloor(floorJson);
                    if (floorResult is DungeonSaveParseFailure<DungeonFloorSaveState>)
                        return ParseFailure(floorResult.Diagnostics[0], reference.RelativePath);
                    DungeonFloorSaveState floor = (
                        (DungeonSaveParseSuccess<DungeonFloorSaveState>)floorResult
                    ).Value;
                    if (
                        floor.Depth != reference.Depth
                        || floor.DocumentVersion != reference.DocumentVersion
                    )
                    {
                        return CorruptGeneration(
                            reference.RelativePath,
                            "The floor depth or version does not match its manifest index entry."
                        );
                    }
                    floors.Add(floor);
                    floorJsonByPath.Add(
                        reference.RelativePath,
                        DungeonSaveJsonCodec.SerializeFloor(floor)
                    );
                }

                DungeonRunSave save = new(manifest, floors);
                IReadOnlyList<DungeonSaveDiagnostic> diagnostics = DungeonRunSaveValidator.Validate(
                    save
                );
                if (diagnostics.Count > 0)
                    return CorruptGeneration(diagnostics[0].Path, diagnostics[0].Message);
                string actualGenerationId = ComputeGenerationId(
                    DungeonSaveJsonCodec.SerializeRunManifest(manifest),
                    floorJsonByPath
                );
                if (
                    !string.Equals(
                        actualGenerationId,
                        expectedGenerationId,
                        StringComparison.Ordinal
                    )
                )
                {
                    return CorruptGeneration(
                        "generation",
                        "The generation content does not match its committed integrity identifier."
                    );
                }
                return new GenerationSuccess(save);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return CorruptGeneration(
                    "generation",
                    "The generation is incomplete or invalid: " + exception.Message
                );
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return new GenerationFailure(
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.IoFailure,
                        DungeonSaveDiagnosticSeverity.Error,
                        "generation",
                        "The generation could not be read: " + exception.Message
                    )
                );
            }
        }

        private static string ResolveGenerationPath(string generationPath, string relativePath)
        {
            string normalizedRoot =
                Path.GetFullPath(generationPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Path.Combine(generationPath, relativePath));
            if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A floor path escapes its committed generation.");
            return resolved;
        }

        private static bool IsGenerationId(string value) =>
            value != null
            && value.Length == 64
            && value.All(character =>
                (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')
            );

        private static GenerationFailure ParseFailure(
            DungeonSaveDiagnostic diagnostic,
            string path
        ) =>
            new(
                new DungeonSaveDiagnostic(
                    diagnostic.Code,
                    DungeonSaveDiagnosticSeverity.Error,
                    path,
                    diagnostic.Message
                )
            );

        private static GenerationFailure CorruptGeneration(string path, string message) =>
            new(
                new DungeonSaveDiagnostic(
                    DungeonSaveDiagnosticCode.CorruptSave,
                    DungeonSaveDiagnosticSeverity.Error,
                    path,
                    message
                )
            );

        private static DungeonSaveDiagnostic AsWarning(DungeonSaveDiagnostic diagnostic) =>
            new(
                diagnostic.Code,
                DungeonSaveDiagnosticSeverity.Warning,
                diagnostic.Path,
                diagnostic.Message
            );

        private abstract class PointerAttempt { }

        private sealed class PointerSuccess : PointerAttempt
        {
            internal PointerSuccess(string generationId) => GenerationId = generationId;

            internal string GenerationId { get; }
        }

        private sealed class PointerFailure : PointerAttempt
        {
            internal PointerFailure(DungeonSaveDiagnostic diagnostic) => Diagnostic = diagnostic;

            internal DungeonSaveDiagnostic Diagnostic { get; }
        }

        private abstract class GenerationAttempt { }

        private sealed class GenerationSuccess : GenerationAttempt
        {
            internal GenerationSuccess(DungeonRunSave save) => Save = save;

            internal DungeonRunSave Save { get; }
        }

        private sealed class GenerationFailure : GenerationAttempt
        {
            internal GenerationFailure(DungeonSaveDiagnostic diagnostic) => Diagnostic = diagnostic;

            internal DungeonSaveDiagnostic Diagnostic { get; }
        }
    }
}
