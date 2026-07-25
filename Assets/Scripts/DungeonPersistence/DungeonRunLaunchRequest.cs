using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Repository;
using UnityEngine;

namespace Game.DungeonPersistence
{
    internal enum DungeonRunLaunchMode
    {
        None,
        NewRun,
        Continue,
    }

    /// <summary>
    /// Carries one player-approved dungeon launch across the asynchronous scene transition.
    /// The autosave directory is explicit so embedded hosts and tests never touch a player's save.
    /// </summary>
    internal sealed class DungeonRunLaunchRequest
    {
        private DungeonRunLaunchRequest(
            DungeonRunLaunchMode mode,
            int normalizedSeed,
            string autosaveDirectory
        )
        {
            Mode = mode;
            NormalizedSeed = normalizedSeed;
            AutosaveDirectory = autosaveDirectory ?? string.Empty;
        }

        internal static DungeonRunLaunchRequest None { get; } =
            new(DungeonRunLaunchMode.None, 0, string.Empty);

        internal DungeonRunLaunchMode Mode { get; }

        internal int NormalizedSeed { get; }

        internal string AutosaveDirectory { get; }

        internal bool IsPending => Mode != DungeonRunLaunchMode.None;

        internal static DungeonRunLaunchRequest NewRun(int seed, string autosaveDirectory) =>
            new(DungeonRunLaunchMode.NewRun, seed, autosaveDirectory);

        internal static DungeonRunLaunchRequest Continue(string autosaveDirectory) =>
            new(DungeonRunLaunchMode.Continue, 0, autosaveDirectory);
    }

    internal sealed class DungeonRunMenuStatus
    {
        internal DungeonRunMenuStatus(
            bool hasAutosave,
            bool canContinue,
            int startingSeed,
            int currentDepth,
            string message
        )
        {
            HasAutosave = hasAutosave;
            CanContinue = canContinue;
            StartingSeed = startingSeed;
            CurrentDepth = currentDepth;
            Message = message ?? string.Empty;
        }

        internal bool HasAutosave { get; }

        internal bool CanContinue { get; }

        internal int StartingSeed { get; }

        internal int CurrentDepth { get; }

        internal string Message { get; }
    }

    /// <summary>
    /// Validates player menu input and inspects the current-schema autosave without mutating it.
    /// </summary>
    internal sealed class DungeonRunMenuService
    {
        private readonly string autosaveDirectory;
        private readonly Func<long> createEntropySeed;

        internal DungeonRunMenuService(string autosaveDirectory, Func<long> createEntropySeed)
        {
            this.autosaveDirectory = string.IsNullOrWhiteSpace(autosaveDirectory)
                ? throw new ArgumentException(
                    "An autosave directory is required.",
                    nameof(autosaveDirectory)
                )
                : Path.GetFullPath(autosaveDirectory);
            this.createEntropySeed =
                createEntropySeed ?? throw new ArgumentNullException(nameof(createEntropySeed));
        }

        internal static DungeonRunMenuService CreateDefault() =>
            new(DefaultAutosaveDirectory, CreateSystemEntropySeed);

        internal static string DefaultAutosaveDirectory =>
            Path.Combine(Application.persistentDataPath, "DungeonAutosave");

        internal DungeonRunMenuStatus InspectAutosave()
        {
            FileSystemDungeonSaveRepository repository = new(autosaveDirectory);
            if (!File.Exists(repository.AutosavePath))
            {
                return new DungeonRunMenuStatus(false, false, 0, 0, "No saved dungeon run found.");
            }

            DungeonSaveResult<DungeonRunSave> loaded = repository.Load();
            if (loaded.IsSuccess)
            {
                DungeonRunSaveManifest manifest = loaded.Value.Manifest;
                return new DungeonRunMenuStatus(
                    true,
                    true,
                    manifest.StartingSeed,
                    manifest.CurrentDepth,
                    $"Continue depth {manifest.CurrentDepth} — seed {manifest.StartingSeed}."
                );
            }

            DungeonSaveDiagnostic diagnostic = loaded.Diagnostics[0];
            return new DungeonRunMenuStatus(
                true,
                false,
                0,
                0,
                diagnostic.Code switch
                {
                    DungeonSaveDiagnosticCode.IncompatibleVersion =>
                        "Continue unavailable: this save is from an incompatible version.",
                    DungeonSaveDiagnosticCode.CorruptSave =>
                        "Continue unavailable: the dungeon autosave is corrupt.",
                    DungeonSaveDiagnosticCode.MissingSave => "No saved dungeon run found.",
                    _ => "Continue unavailable: the dungeon autosave could not be read.",
                }
            );
        }

        internal bool TryCreateNewRunRequest(
            string seedText,
            out DungeonRunLaunchRequest request,
            out string error
        )
        {
            long suppliedSeed;
            if (string.IsNullOrWhiteSpace(seedText))
            {
                suppliedSeed = createEntropySeed();
            }
            else if (
                !long.TryParse(
                    seedText.Trim(),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out suppliedSeed
                )
            )
            {
                request = DungeonRunLaunchRequest.None;
                error =
                    "Enter a whole-number seed from -9223372036854775808 to 9223372036854775807.";
                return false;
            }

            int normalized = DungeonSeedSequence.NormalizeRunSeed(suppliedSeed);
            request = DungeonRunLaunchRequest.NewRun(normalized, autosaveDirectory);
            error = string.Empty;
            return true;
        }

        internal DungeonRunLaunchRequest CreateContinueRequest() =>
            DungeonRunLaunchRequest.Continue(autosaveDirectory);

        private static long CreateSystemEntropySeed()
        {
            byte[] bytes = new byte[sizeof(long)];
            using RandomNumberGenerator generator = RandomNumberGenerator.Create();
            generator.GetBytes(bytes);
            return BitConverter.ToInt64(bytes, 0);
        }
    }
}
