using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>
    /// Stores one run as an atomic archive containing <c>manifest.json</c> and one versioned JSON
    /// entry per generated depth.
    /// </summary>
    /// <remarks>
    /// A save is written beside the current archive, reopened through the normal load path, and
    /// only then atomically replaces the previous archive. The immediately previous valid archive
    /// remains available as recovery data.
    /// </remarks>
    internal sealed class FileSystemDungeonSaveRepository : IDungeonSaveRepository
    {
        private const string CurrentFileName = "autosave.zip";
        private const string BackupFileName = "autosave.backup.zip";
        private const string ManifestEntryName = "manifest.json";

        private readonly object sync = new();
        private readonly string rootPath;

        /// <summary>Creates a repository rooted at an explicitly supplied autosave directory.</summary>
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
        public DungeonSaveResult<bool> Save(DungeonRunSave save)
        {
            lock (sync)
            {
                IReadOnlyList<DungeonSaveDiagnostic> validation = DungeonRunSaveValidator.Validate(
                    save
                );
                if (validation.Count > 0)
                    return DungeonSaveResult<bool>.Failure(validation);

                string temporaryPath = string.Empty;
                try
                {
                    Directory.CreateDirectory(rootPath);
                    temporaryPath = Path.Combine(rootPath, $"autosave-{Guid.NewGuid():N}.tmp");
                    WriteArchive(temporaryPath, save);

                    DungeonSaveResult<DungeonRunSave> staged = ReadArchive(
                        temporaryPath,
                        DungeonSaveDiagnosticCode.CorruptSave,
                        "staged autosave"
                    );
                    if (!staged.IsSuccess)
                        return DungeonSaveResult<bool>.Failure(staged.Diagnostics);

                    Publish(temporaryPath);
                    temporaryPath = string.Empty;
                    return DungeonSaveResult<bool>.Success(true);
                }
                catch (Exception exception) when (IsFileSystemException(exception))
                {
                    return DungeonSaveResult<bool>.Failure(
                        new[]
                        {
                            Diagnostic(
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
                    TryDelete(temporaryPath);
                }
            }
        }

        /// <inheritdoc/>
        public DungeonSaveResult<DungeonRunSave> Load()
        {
            lock (sync)
            {
                DungeonSaveResult<DungeonRunSave> current = ReadArchive(
                    CurrentPath,
                    DungeonSaveDiagnosticCode.MissingSave,
                    CurrentFileName
                );
                if (current.IsSuccess)
                    return current;
                if (!File.Exists(BackupPath))
                    return current;

                DungeonSaveResult<DungeonRunSave> backup = ReadArchive(
                    BackupPath,
                    DungeonSaveDiagnosticCode.CorruptSave,
                    BackupFileName
                );
                if (!backup.IsSuccess)
                    return DungeonSaveResult<DungeonRunSave>.Failure(
                        current.Diagnostics.Concat(backup.Diagnostics)
                    );

                DungeonSaveDiagnostic currentProblem = current.Diagnostics[0];
                return DungeonSaveResult<DungeonRunSave>.Success(
                    backup.Value,
                    new[]
                    {
                        Diagnostic(
                            currentProblem.Code,
                            DungeonSaveDiagnosticSeverity.Warning,
                            currentProblem.Path,
                            currentProblem.Message
                        ),
                        Diagnostic(
                            DungeonSaveDiagnosticCode.RecoveredPreviousGeneration,
                            DungeonSaveDiagnosticSeverity.Warning,
                            BackupFileName,
                            "The current autosave was unusable, so the previous valid archive was recovered."
                        ),
                    }
                );
            }
        }

        private string CurrentPath => Path.Combine(rootPath, CurrentFileName);

        private string BackupPath => Path.Combine(rootPath, BackupFileName);

        private void Publish(string temporaryPath)
        {
            if (!File.Exists(CurrentPath))
            {
                File.Move(temporaryPath, CurrentPath);
                return;
            }

            bool currentIsValid = ReadArchive(
                CurrentPath,
                DungeonSaveDiagnosticCode.CorruptSave,
                CurrentFileName
            ).IsSuccess;
            bool backupIsValid =
                File.Exists(BackupPath)
                && ReadArchive(
                    BackupPath,
                    DungeonSaveDiagnosticCode.CorruptSave,
                    BackupFileName
                ).IsSuccess;

            string backupPath = !currentIsValid && backupIsValid ? null : BackupPath;
            File.Replace(temporaryPath, CurrentPath, backupPath, ignoreMetadataErrors: true);
        }

        private static void WriteArchive(string path, DungeonRunSave save)
        {
            using FileStream stream = new(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
            using (
                ZipArchive archive = new(
                    stream,
                    ZipArchiveMode.Create,
                    leaveOpen: true,
                    Encoding.UTF8
                )
            )
            {
                WriteEntry(
                    archive,
                    ManifestEntryName,
                    DungeonSaveJsonCodec.SerializeRunManifest(save.Manifest)
                );
                IReadOnlyDictionary<int, DungeonFloorSaveState> floors = save.Floors.ToDictionary(
                    floor => floor.Depth
                );
                foreach (DungeonFloorSaveReference reference in save.Manifest.GeneratedFloors)
                {
                    WriteEntry(
                        archive,
                        reference.RelativePath,
                        DungeonSaveJsonCodec.SerializeFloor(floors[reference.Depth])
                    );
                }
            }
            stream.Flush(flushToDisk: true);
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using StreamWriter writer = new(
                entry.Open(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            writer.Write(content);
        }

        private static DungeonSaveResult<DungeonRunSave> ReadArchive(
            string path,
            DungeonSaveDiagnosticCode missingCode,
            string logicalPath
        )
        {
            if (!File.Exists(path))
            {
                return DungeonSaveResult<DungeonRunSave>.Failure(
                    new[]
                    {
                        Diagnostic(
                            missingCode,
                            DungeonSaveDiagnosticSeverity.Error,
                            logicalPath,
                            missingCode == DungeonSaveDiagnosticCode.MissingSave
                                ? "No committed dungeon autosave exists."
                                : "The recovery autosave archive is missing."
                        ),
                    }
                );
            }

            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using ZipArchive archive = new(
                    stream,
                    ZipArchiveMode.Read,
                    leaveOpen: false,
                    Encoding.UTF8
                );
                string duplicate = archive
                    .Entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .FirstOrDefault();
                if (duplicate != null)
                    return Corrupt(logicalPath, $"Archive entry '{duplicate}' is duplicated.");

                ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntryName);
                if (manifestEntry == null)
                    return Corrupt(logicalPath, "The run manifest is missing.");
                DungeonSaveResult<DungeonRunSaveManifest> manifestResult =
                    DungeonSaveJsonCodec.ParseRunManifest(ReadEntry(manifestEntry));
                if (!manifestResult.IsSuccess)
                    return DungeonSaveResult<DungeonRunSave>.Failure(manifestResult.Diagnostics);

                HashSet<string> expectedEntries = new(StringComparer.Ordinal) { ManifestEntryName };
                DungeonRunSaveManifest manifest = manifestResult.Value;
                List<DungeonFloorSaveState> floors = new(manifest.GeneratedFloors.Count);
                foreach (DungeonFloorSaveReference reference in manifest.GeneratedFloors)
                {
                    expectedEntries.Add(reference.RelativePath);
                    ZipArchiveEntry floorEntry = archive.GetEntry(reference.RelativePath);
                    if (floorEntry == null)
                        return Corrupt(reference.RelativePath, "The indexed floor is missing.");

                    DungeonSaveResult<DungeonFloorSaveState> floorResult =
                        DungeonSaveJsonCodec.ParseFloor(ReadEntry(floorEntry));
                    if (!floorResult.IsSuccess)
                        return DungeonSaveResult<DungeonRunSave>.Failure(floorResult.Diagnostics);
                    DungeonFloorSaveState floor = floorResult.Value;
                    if (
                        floor.Depth != reference.Depth
                        || floor.DocumentVersion != reference.DocumentVersion
                    )
                    {
                        return Corrupt(
                            reference.RelativePath,
                            "The floor depth or version does not match its manifest index."
                        );
                    }
                    floors.Add(floor);
                }

                string unexpected = archive
                    .Entries.Select(entry => entry.FullName)
                    .FirstOrDefault(name => !expectedEntries.Contains(name));
                if (unexpected != null)
                    return Corrupt(logicalPath, $"Archive entry '{unexpected}' is not indexed.");

                DungeonRunSave save = new(manifest, floors);
                IReadOnlyList<DungeonSaveDiagnostic> validation = DungeonRunSaveValidator.Validate(
                    save
                );
                return validation.Count == 0
                    ? DungeonSaveResult<DungeonRunSave>.Success(save)
                    : DungeonSaveResult<DungeonRunSave>.Failure(validation);
            }
            catch (Exception exception)
                when (exception is InvalidDataException
                    || exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is FormatException
                    || exception is OverflowException
                )
            {
                return Corrupt(
                    logicalPath,
                    "The autosave archive is incomplete or invalid: " + exception.Message
                );
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                return DungeonSaveResult<DungeonRunSave>.Failure(
                    new[]
                    {
                        Diagnostic(
                            DungeonSaveDiagnosticCode.IoFailure,
                            DungeonSaveDiagnosticSeverity.Error,
                            logicalPath,
                            "The autosave archive could not be read: " + exception.Message
                        ),
                    }
                );
            }
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using StreamReader reader = new(
                entry.Open(),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true
            );
            return reader.ReadToEnd();
        }

        private static DungeonSaveResult<DungeonRunSave> Corrupt(string path, string message) =>
            DungeonSaveResult<DungeonRunSave>.Failure(
                new[]
                {
                    Diagnostic(
                        DungeonSaveDiagnosticCode.CorruptSave,
                        DungeonSaveDiagnosticSeverity.Error,
                        path,
                        message
                    ),
                }
            );

        private static DungeonSaveDiagnostic Diagnostic(
            DungeonSaveDiagnosticCode code,
            DungeonSaveDiagnosticSeverity severity,
            string path,
            string message
        ) => new(code, severity, path, message);

        private static bool IsFileSystemException(Exception exception) =>
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is NotSupportedException
            || exception is SecurityException;

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception) when (IsFileSystemException(exception)) { }
        }
    }
}
