namespace Game.DungeonPersistence.Repository
{
    /// <summary>
    /// Defines the only persistence schema versions understood by this development build.
    /// Versions are explicit so incompatible data is rejected before any Unity scene is mutated.
    /// </summary>
    internal static class DungeonSaveSchema
    {
        /// <summary>Gets the current run-manifest document version.</summary>
        public const int RunManifestVersion = 1;

        /// <summary>Gets the current per-floor document version.</summary>
        public const int FloorStateVersion = 1;

        /// <summary>Gets the current standalone creature-state token version.</summary>
        public const int CreatureStateVersion = 1;
    }
}
