using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Game.DungeonGeneration;

namespace Game.DungeonPersistence.Repository
{
    internal interface IDungeonSaveRepository
    {
        DungeonSaveResult<DungeonRunSave> Load();
        DungeonSaveResult<bool> Save(DungeonRunSave save);
    }

    /// <summary>
    /// Publishes one validated manifest-and-floor ZIP with an atomic filesystem replacement.
    /// </summary>
    internal sealed class FileSystemDungeonSaveRepository : IDungeonSaveRepository
    {
        private const string AutosaveFileName = "autosave.zip";
        private readonly string directory;
        private readonly Action<string, string> publish;

        internal FileSystemDungeonSaveRepository(
            string directory,
            Action<string, string> publish = null
        )
        {
            this.directory = string.IsNullOrWhiteSpace(directory)
                ? throw new ArgumentException(
                    "An autosave directory is required.",
                    nameof(directory)
                )
                : Path.GetFullPath(directory);
            this.publish = publish ?? PublishAtomically;
        }

        internal string AutosavePath => Path.Combine(directory, AutosaveFileName);

        public DungeonSaveResult<DungeonRunSave> Load()
        {
            if (!File.Exists(AutosavePath))
            {
                return DungeonSaveResult<DungeonRunSave>.Failure(
                    DungeonSaveDiagnosticCode.MissingSave,
                    AutosavePath,
                    "No dungeon autosave exists."
                );
            }

            return LoadArchive(AutosavePath);
        }

        public DungeonSaveResult<bool> Save(DungeonRunSave save)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));

            Directory.CreateDirectory(directory);
            string temporaryPath = AutosavePath + ".tmp";
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                using (
                    FileStream stream = new(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None
                    )
                )
                {
                    using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        WriteEntry(
                            archive,
                            DungeonSaveSchema.ManifestPath,
                            DungeonSaveJson.SerializeManifest(save.Manifest)
                        );
                        WriteEntry(
                            archive,
                            DungeonSaveSchema.FloorPath,
                            DungeonLevelJsonSerializer.Serialize(save.FloorDocument)
                        );
                    }
                    stream.Flush(flushToDisk: true);
                }

                DungeonSaveResult<DungeonRunSave> staged = LoadArchive(temporaryPath);
                if (!staged.IsSuccess)
                {
                    return DungeonSaveResult<bool>.Failure(
                        staged.Diagnostics[0].Code,
                        staged.Diagnostics[0].Path,
                        "The staged autosave failed validation: " + staged.Diagnostics[0].Message
                    );
                }

                publish(temporaryPath, AutosavePath);
                return DungeonSaveResult<bool>.Success(true);
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is InvalidDataException
                )
            {
                return DungeonSaveResult<bool>.Failure(
                    DungeonSaveDiagnosticCode.IoFailure,
                    AutosavePath,
                    exception.Message
                );
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static DungeonSaveResult<DungeonRunSave> LoadArchive(string path)
        {
            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using ZipArchive archive = new(stream, ZipArchiveMode.Read);
                Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!entries.TryAdd(entry.FullName, entry))
                    {
                        return DungeonSaveResult<DungeonRunSave>.Failure(
                            DungeonSaveDiagnosticCode.CorruptSave,
                            entry.FullName,
                            "The archive contains a duplicate entry."
                        );
                    }
                }

                string[] expected = { DungeonSaveSchema.ManifestPath, DungeonSaveSchema.FloorPath };
                if (
                    entries.Count != expected.Length
                    || expected.Any(entry => !entries.ContainsKey(entry))
                )
                {
                    return DungeonSaveResult<DungeonRunSave>.Failure(
                        DungeonSaveDiagnosticCode.CorruptSave,
                        path,
                        "The autosave must contain exactly manifest.json and floor.json."
                    );
                }

                DungeonSaveResult<DungeonRunSaveManifest> manifest = DungeonSaveJson.ParseManifest(
                    ReadEntry(entries[DungeonSaveSchema.ManifestPath])
                );
                if (!manifest.IsSuccess)
                {
                    DungeonSaveDiagnostic diagnostic = manifest.Diagnostics[0];
                    return DungeonSaveResult<DungeonRunSave>.Failure(
                        diagnostic.Code,
                        diagnostic.Path,
                        diagnostic.Message
                    );
                }

                DungeonLevelParseResult floor = DungeonLevelJsonParser.Parse(
                    ReadEntry(entries[DungeonSaveSchema.FloorPath])
                );
                if (!floor.IsSuccess)
                {
                    return DungeonSaveResult<DungeonRunSave>.Failure(
                        DungeonSaveDiagnosticCode.CorruptSave,
                        DungeonSaveSchema.FloorPath,
                        string.Join(" ", floor.Diagnostics.Select(item => item.Message))
                    );
                }

                return DungeonSaveResult<DungeonRunSave>.Success(
                    new DungeonRunSave(manifest.Value, floor.Document)
                );
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is InvalidDataException
                    || exception is ArgumentException
                )
            {
                return DungeonSaveResult<DungeonRunSave>.Failure(
                    exception is IOException || exception is UnauthorizedAccessException
                        ? DungeonSaveDiagnosticCode.IoFailure
                        : DungeonSaveDiagnosticCode.CorruptSave,
                    path,
                    exception.Message
                );
            }
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using Stream output = entry.Open();
            using StreamWriter writer = new(
                output,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: false
            );
            writer.Write(content);
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using Stream input = entry.Open();
            using StreamReader reader = new(
                input,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true
            );
            return reader.ReadToEnd();
        }

        private static void PublishAtomically(string temporaryPath, string autosavePath)
        {
            if (File.Exists(autosavePath))
                File.Replace(temporaryPath, autosavePath, destinationBackupFileName: null, true);
            else
                File.Move(temporaryPath, autosavePath);
        }
    }
}
