using System;
using System.IO;
using System.Text;

namespace Game.DungeonPersistence.Repository
{
    internal interface IDungeonSaveRepository
    {
        DungeonSaveResult<DungeonRunSave> Load();
        DungeonSaveResult<bool> Save(DungeonRunSave save);
    }

    /// <summary>
    /// Publishes one complete, validated run autosave with an atomic filesystem replacement.
    /// </summary>
    /// <remarks>
    /// Loading accepts only the current complete schema. It never migrates, repairs, regenerates,
    /// salvages, or partially accepts a run, and it never falls back to another file.
    /// </remarks>
    internal sealed class FileSystemDungeonSaveRepository : IDungeonSaveRepository
    {
        private const string AutosaveFileName = "autosave.json";
        private readonly string directory;
        private readonly Action<string, string> publish;
        private readonly Action<string> staged;

        internal FileSystemDungeonSaveRepository(
            string directory,
            Action<string, string> publish = null,
            Action<string> staged = null
        )
        {
            this.directory = string.IsNullOrWhiteSpace(directory)
                ? throw new ArgumentException(
                    "An autosave directory is required.",
                    nameof(directory)
                )
                : Path.GetFullPath(directory);
            this.publish = publish ?? PublishAtomically;
            this.staged = staged;
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
            return LoadPath(AutosavePath);
        }

        public DungeonSaveResult<bool> Save(DungeonRunSave save)
        {
            string json;
            try
            {
                json = DungeonSaveJson.Serialize(save);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return DungeonSaveResult<bool>.Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "autosave.capture",
                    exception.Message
                );
            }

            string temporaryPath = AutosavePath + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                using (
                    FileStream stream = new(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    )
                )
                using (
                    StreamWriter writer = new(
                        stream,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        bufferSize: 1024,
                        leaveOpen: true
                    )
                )
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                staged?.Invoke(temporaryPath);
                DungeonSaveResult<DungeonRunSave> validation = LoadPath(temporaryPath);
                if (!validation.IsSuccess)
                {
                    DungeonSaveDiagnostic diagnostic = validation.Diagnostics[0];
                    return DungeonSaveResult<bool>.Failure(
                        diagnostic.Code,
                        diagnostic.Path,
                        "The staged autosave failed validation: " + diagnostic.Message
                    );
                }

                publish(temporaryPath, AutosavePath);
                return DungeonSaveResult<bool>.Success(true);
            }
            catch (Exception exception)
                when (exception is IOException || exception is UnauthorizedAccessException)
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

        private static DungeonSaveResult<DungeonRunSave> LoadPath(string path)
        {
            try
            {
                return DungeonSaveJson.Parse(File.ReadAllText(path, Encoding.UTF8), path);
            }
            catch (Exception exception)
                when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return DungeonSaveResult<DungeonRunSave>.Failure(
                    DungeonSaveDiagnosticCode.IoFailure,
                    path,
                    exception.Message
                );
            }
        }

        private static void PublishAtomically(string temporaryPath, string autosavePath)
        {
            if (File.Exists(autosavePath))
                File.Replace(temporaryPath, autosavePath, null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, autosavePath);
        }
    }
}
