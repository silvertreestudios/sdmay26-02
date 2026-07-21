using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>
    /// Stores one autosave as immutable content-addressed generations and an atomically replaced
    /// current pointer. Callers inject the autosave root, normally a child of Unity's persistent
    /// data path; tests must inject an isolated temporary root.
    /// </summary>
    public sealed partial class FileSystemDungeonSaveRepository : IDungeonSaveRepository
    {
        private const int PointerVersion = 1;
        private const string CurrentPointerFileName = "current.json";
        private const string PreviousPointerFileName = "previous.json";
        private const string ManifestFileName = "manifest.json";
        private const string GenerationsDirectoryName = "generations";
        private const string StagingDirectoryName = ".staging";

        private readonly object sync = new();
        private readonly string rootPath;

        /// <summary>Creates a repository rooted at an explicitly supplied autosave directory.</summary>
        /// <param name="rootPath">
        /// The dedicated autosave root. The repository never substitutes
        /// <c>Application.persistentDataPath</c>, allowing production composition and tests to own
        /// that policy independently.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="rootPath"/> is blank or cannot be normalized.</exception>
        public FileSystemDungeonSaveRepository(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException(
                    "An explicit autosave root is required.",
                    nameof(rootPath)
                );
            this.rootPath = Path.GetFullPath(rootPath);
        }

        /// <summary>Gets the normalized autosave root supplied by the composition root.</summary>
        public string RootPath => rootPath;

        /// <inheritdoc/>
        public DungeonSaveWriteResult Save(DungeonRunSave save)
        {
            lock (sync)
            {
                IReadOnlyList<DungeonSaveDiagnostic> validation = DungeonRunSaveValidator.Validate(
                    save
                );
                if (validation.Count > 0)
                    return new DungeonSaveWriteResult(false, validation);

                string stagingRoot = string.Empty;
                string nextPointerPath = string.Empty;
                try
                {
                    Directory.CreateDirectory(rootPath);
                    Directory.CreateDirectory(GenerationsPath);
                    Directory.CreateDirectory(StagingPath);

                    string manifestJson = DungeonSaveJsonCodec.SerializeRunManifest(save.Manifest);
                    IReadOnlyDictionary<string, string> floors =
                        save.Manifest.GeneratedFloors.ToDictionary(
                            reference => reference.RelativePath,
                            reference =>
                                DungeonSaveJsonCodec.SerializeFloor(
                                    save.Floors.Single(floor => floor.Depth == reference.Depth)
                                ),
                            StringComparer.Ordinal
                        );
                    string generationId = ComputeGenerationId(manifestJson, floors);

                    string transactionId = Guid.NewGuid().ToString("N");
                    stagingRoot = Path.Combine(StagingPath, transactionId);
                    string stagedGeneration = stagingRoot;
                    Directory.CreateDirectory(stagedGeneration);
                    WriteDurableText(
                        Path.Combine(stagedGeneration, ManifestFileName),
                        manifestJson
                    );
                    foreach (DungeonFloorSaveReference reference in save.Manifest.GeneratedFloors)
                    {
                        string floorPath = ResolveGenerationPath(
                            stagedGeneration,
                            reference.RelativePath
                        );
                        Directory.CreateDirectory(Path.GetDirectoryName(floorPath));
                        WriteDurableText(floorPath, floors[reference.RelativePath]);
                    }

                    GenerationAttempt staged = LoadGeneration(stagedGeneration, generationId);
                    if (staged is GenerationFailure stagedFailure)
                    {
                        return new DungeonSaveWriteResult(
                            false,
                            new[] { stagedFailure.Diagnostic }
                        );
                    }

                    string committedGeneration = Path.Combine(GenerationsPath, generationId);
                    if (Directory.Exists(committedGeneration))
                    {
                        GenerationAttempt existing = LoadGeneration(
                            committedGeneration,
                            generationId
                        );
                        if (existing is GenerationFailure existingFailure)
                        {
                            return new DungeonSaveWriteResult(
                                false,
                                new[] { existingFailure.Diagnostic }
                            );
                        }
                    }
                    else
                    {
                        Directory.Move(stagedGeneration, committedGeneration);
                    }

                    nextPointerPath = Path.Combine(rootPath, $"current-{transactionId}.next");
                    WriteDurableText(nextPointerPath, SerializePointer(generationId));
                    PointerAttempt nextPointer = ReadPointer(
                        nextPointerPath,
                        missingIsCorrupt: true
                    );
                    if (nextPointer is PointerFailure pointerFailure)
                    {
                        return new DungeonSaveWriteResult(
                            false,
                            new[] { pointerFailure.Diagnostic }
                        );
                    }

                    bool currentPointerIsValid =
                        LoadPointerGeneration(CurrentPointerPath, missingIsCorrupt: false)
                        is GenerationSuccess;
                    PublishPointer(nextPointerPath, currentPointerIsValid, transactionId);
                    nextPointerPath = string.Empty;
                    IReadOnlyList<DungeonSaveDiagnostic> cleanupDiagnostics =
                        PruneUnreferencedGenerations();
                    return new DungeonSaveWriteResult(true, cleanupDiagnostics);
                }
                catch (Exception exception) when (IsFileSystemException(exception))
                {
                    return new DungeonSaveWriteResult(
                        false,
                        new[]
                        {
                            new DungeonSaveDiagnostic(
                                DungeonSaveDiagnosticCode.IoFailure,
                                DungeonSaveDiagnosticSeverity.Error,
                                "autosave",
                                "The autosave could not be committed: " + exception.Message
                            ),
                        }
                    );
                }
                finally
                {
                    TryDeleteFile(nextPointerPath);
                    TryDeleteDirectory(stagingRoot);
                }
            }
        }

        private string CurrentPointerPath => Path.Combine(rootPath, CurrentPointerFileName);

        private string PreviousPointerPath => Path.Combine(rootPath, PreviousPointerFileName);

        private string GenerationsPath => Path.Combine(rootPath, GenerationsDirectoryName);

        private string StagingPath => Path.Combine(rootPath, StagingDirectoryName);

        private void PublishPointer(
            string nextPointerPath,
            bool currentPointerIsValid,
            string transactionId
        )
        {
            if (File.Exists(CurrentPointerPath))
            {
                string backupPath = currentPointerIsValid
                    ? PreviousPointerPath
                    : Path.Combine(rootPath, $"invalid-current-{transactionId}.backup");
                File.Replace(
                    nextPointerPath,
                    CurrentPointerPath,
                    backupPath,
                    ignoreMetadataErrors: true
                );
                if (!currentPointerIsValid)
                    TryDeleteFile(backupPath);
                return;
            }
            File.Move(nextPointerPath, CurrentPointerPath);
        }

        private IReadOnlyList<DungeonSaveDiagnostic> PruneUnreferencedGenerations()
        {
            try
            {
                HashSet<string> retained = new(StringComparer.Ordinal);
                RetainPointerGeneration(CurrentPointerPath, retained);
                RetainPointerGeneration(PreviousPointerPath, retained);
                foreach (string directory in Directory.GetDirectories(GenerationsPath))
                {
                    string generationId = Path.GetFileName(directory);
                    if (IsGenerationId(generationId) && !retained.Contains(generationId))
                        Directory.Delete(directory, recursive: true);
                }
                return Array.Empty<DungeonSaveDiagnostic>();
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return new[]
                {
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.IoFailure,
                        DungeonSaveDiagnosticSeverity.Warning,
                        "autosave.generations",
                        "The autosave committed, but obsolete generations could not be pruned: "
                            + exception.Message
                    ),
                };
            }
        }

        private static void RetainPointerGeneration(string pointerPath, ISet<string> retained)
        {
            if (ReadPointer(pointerPath, missingIsCorrupt: true) is PointerSuccess pointer)
                retained.Add(pointer.GenerationId);
        }

        private static string SerializePointer(string generationId) =>
            new JObject
            {
                ["pointerVersion"] = PointerVersion,
                ["generationId"] = generationId,
            }.ToString(Formatting.None);

        private static string ComputeGenerationId(
            string manifestJson,
            IReadOnlyDictionary<string, string> floorJsonByPath
        )
        {
            StringBuilder content = new();
            AppendHashPart(content, ManifestFileName, manifestJson);
            foreach (
                KeyValuePair<string, string> floor in floorJsonByPath.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal
                )
            )
            {
                AppendHashPart(content, floor.Key, floor.Value);
            }
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content.ToString()));
            return string.Concat(hash.Select(value => value.ToString("x2")));
        }

        private static void AppendHashPart(StringBuilder target, string path, string json)
        {
            target
                .Append(path.Length)
                .Append(':')
                .Append(path)
                .Append(':')
                .Append(json.Length)
                .Append(':')
                .Append(json)
                .Append('\n');
        }

        private static void WriteDurableText(string path, string content)
        {
            using FileStream stream = new(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
            using StreamWriter writer = new(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        private static bool IsFileSystemException(Exception exception) =>
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is NotSupportedException
            || exception is SecurityException;

        private static void TryDeleteFile(string path)
        {
            if (path.Length == 0)
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception) when (IsFileSystemException(exception)) { }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (path.Length == 0)
                return;
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception exception) when (IsFileSystemException(exception)) { }
        }
    }
}
