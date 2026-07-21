using System;
using System.IO;
using Game.DungeonPersistence.Repository;
using UnityEngine;

namespace Game.DungeonPersistence.Autosave
{
    /// <summary>Builds the production dungeon autosave repository at one explicit persistent child.</summary>
    internal static class DungeonAutosaveProductionRepositoryFactory
    {
        /// <summary>The dedicated child below Unity's application-specific persistent data path.</summary>
        public const string AutosaveDirectoryName = "dungeon-run-autosave";

        /// <summary>Creates the production atomic repository for the current application.</summary>
        /// <returns>A repository rooted below <see cref="Application.persistentDataPath"/>.</returns>
        public static FileSystemDungeonSaveRepository Create() =>
            new(BuildAutosaveRootPath(Application.persistentDataPath));

        /// <summary>Builds the normalized dedicated autosave root below an injected persistent root.</summary>
        /// <param name="persistentDataRoot">
        /// Unity's application-specific persistent data directory in production, or an isolated
        /// test root in tests.
        /// </param>
        /// <returns>The normalized dedicated child path used by the repository.</returns>
        public static string BuildAutosaveRootPath(string persistentDataRoot)
        {
            if (string.IsNullOrWhiteSpace(persistentDataRoot))
                throw new ArgumentException(
                    "An explicit persistent data root is required.",
                    nameof(persistentDataRoot)
                );
            return Path.GetFullPath(Path.Combine(persistentDataRoot, AutosaveDirectoryName));
        }
    }
}
