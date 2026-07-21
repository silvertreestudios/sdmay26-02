using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>
    /// Stores one generated depth as the complete validated dungeon document already understood
    /// by generation and scene population.
    /// </summary>
    /// <remarks>
    /// The document owns immutable topology and <see cref="DungeonRuntimeState"/> together. This
    /// deliberately avoids a second persistence-only model for doors, encounters, and creatures.
    /// </remarks>
    internal sealed class DungeonFloorSaveState
    {
        /// <summary>Creates a canonical per-depth document from complete generated-level JSON.</summary>
        /// <param name="documentVersion">The explicit persistence wrapper version.</param>
        /// <param name="depth">The nonnegative generated depth.</param>
        /// <param name="documentJson">Complete generated-level JSON including runtime state.</param>
        public DungeonFloorSaveState(int documentVersion, int depth, string documentJson)
        {
            if (documentVersion <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(documentVersion),
                    "Document version must be positive."
                );
            if (depth < 0)
                throw new ArgumentOutOfRangeException(nameof(depth), "Depth cannot be negative.");

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(documentJson);
            if (!parsed.IsSuccess)
            {
                throw new ArgumentException(
                    "The floor document is invalid: "
                        + string.Join(" ", parsed.Diagnostics.Select(item => item.Message)),
                    nameof(documentJson)
                );
            }
            if (parsed.Document.Generation.Depth != depth)
                throw new ArgumentException(
                    "The floor document depth does not match its index.",
                    nameof(documentJson)
                );
            if (parsed.Document.RuntimeState == null)
                throw new ArgumentException(
                    "A persisted floor requires complete runtime state.",
                    nameof(documentJson)
                );

            DocumentVersion = documentVersion;
            Depth = depth;
            DocumentJson = DungeonLevelJsonSerializer.Serialize(parsed.Document);
        }

        /// <summary>Gets the explicit floor persistence version.</summary>
        public int DocumentVersion { get; }

        /// <summary>Gets the nonnegative generated depth.</summary>
        public int Depth { get; }

        /// <summary>Gets canonical complete generated-level JSON including runtime state.</summary>
        public string DocumentJson { get; }

        internal DungeonLevelDocument ParseDocument()
        {
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(DocumentJson);
            if (!parsed.IsSuccess)
                throw new InvalidOperationException("A validated floor document became invalid.");
            return parsed.Document;
        }
    }

    /// <summary>Indexes one per-depth JSON entry from the run manifest.</summary>
    internal sealed class DungeonFloorSaveReference
    {
        /// <summary>Creates a generated-depth index entry.</summary>
        public DungeonFloorSaveReference(int depth, int documentVersion, string relativePath)
        {
            if (depth < 0)
                throw new ArgumentOutOfRangeException(nameof(depth), "Depth cannot be negative.");
            if (documentVersion <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(documentVersion),
                    "Document version must be positive."
                );
            Depth = depth;
            DocumentVersion = documentVersion;
            RelativePath = DungeonSaveContractGuard.RequiredId(relativePath, nameof(relativePath));
        }

        /// <summary>Gets the nonnegative generated depth.</summary>
        public int Depth { get; }

        /// <summary>Gets the expected floor document version.</summary>
        public int DocumentVersion { get; }

        /// <summary>Gets the archive-relative JSON path.</summary>
        public string RelativePath { get; }

        /// <summary>Creates the canonical current-schema reference for a depth.</summary>
        public static DungeonFloorSaveReference Current(int depth) =>
            new(depth, DungeonSaveSchema.FloorStateVersion, CanonicalPath(depth));

        internal static string CanonicalPath(int depth) =>
            $"floors/depth-{depth.ToString("D4", CultureInfo.InvariantCulture)}.json";
    }

    /// <summary>Represents the versioned run manifest published in one atomic autosave archive.</summary>
    internal sealed class DungeonRunSaveManifest
    {
        /// <summary>Creates a complete immutable run manifest.</summary>
        public DungeonRunSaveManifest(
            int documentVersion,
            int startingSeed,
            string generatorVersion,
            int currentDepth,
            DungeonPartySaveState party,
            IEnumerable<DungeonFloorSaveReference> generatedFloors
        )
        {
            if (documentVersion <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(documentVersion),
                    "Document version must be positive."
                );
            if (currentDepth < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(currentDepth),
                    "Current depth cannot be negative."
                );

            DocumentVersion = documentVersion;
            StartingSeed = startingSeed;
            GeneratorVersion = DungeonSaveContractGuard.RequiredId(
                generatorVersion,
                nameof(generatorVersion)
            );
            CurrentDepth = currentDepth;
            Party = party ?? throw new ArgumentNullException(nameof(party));
            GeneratedFloors = DungeonSaveContractGuard.UniqueSorted(
                generatedFloors,
                floor => floor.Depth.ToString("D10", CultureInfo.InvariantCulture),
                nameof(generatedFloors)
            );
            if (GeneratedFloors.Count == 0)
                throw new ArgumentException(
                    "A saved run requires at least one generated floor.",
                    nameof(generatedFloors)
                );
            if (!GeneratedFloors.Any(floor => floor.Depth == currentDepth))
                throw new ArgumentException(
                    "Current depth must be present in the generated-floor index.",
                    nameof(currentDepth)
                );
        }

        /// <summary>Gets the manifest schema version.</summary>
        public int DocumentVersion { get; }

        /// <summary>Gets the run's starting seed.</summary>
        public int StartingSeed { get; }

        /// <summary>Gets the stable generator algorithm/version identifier.</summary>
        public string GeneratorVersion { get; }

        /// <summary>Gets the depth containing the saved party.</summary>
        public int CurrentDepth { get; }

        /// <summary>Gets the complete current party state.</summary>
        public DungeonPartySaveState Party { get; }

        /// <summary>Gets the generated-depth index in ascending depth order.</summary>
        public IReadOnlyList<DungeonFloorSaveReference> GeneratedFloors { get; }
    }

    /// <summary>Represents one complete manifest-and-floor repository transaction.</summary>
    internal sealed class DungeonRunSave
    {
        /// <summary>Creates a complete autosave transaction.</summary>
        public DungeonRunSave(
            DungeonRunSaveManifest manifest,
            IEnumerable<DungeonFloorSaveState> floors
        )
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Floors = DungeonSaveContractGuard.UniqueSorted(
                floors,
                floor => floor.Depth.ToString("D10", CultureInfo.InvariantCulture),
                nameof(floors)
            );
        }

        /// <summary>Gets the immutable run manifest.</summary>
        public DungeonRunSaveManifest Manifest { get; }

        /// <summary>Gets complete generated-level documents in ascending depth order.</summary>
        public IReadOnlyList<DungeonFloorSaveState> Floors { get; }
    }
}
