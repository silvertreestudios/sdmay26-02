using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>Identifies the durable lifecycle of one room-scoped encounter.</summary>
    public enum DungeonEncounterSaveStatus
    {
        /// <summary>The room has never activated.</summary>
        Dormant,

        /// <summary>The encounter was active when saved and must start a fresh initiative round on restore.</summary>
        Active,

        /// <summary>The encounter retains state but currently has no running combat.</summary>
        Suspended,

        /// <summary>The encounter is permanently resolved.</summary>
        Cleared,
    }

    /// <summary>Records one encounter group's persistent lifecycle.</summary>
    public sealed class DungeonEncounterSaveState
    {
        /// <summary>Creates a lifecycle record.</summary>
        /// <param name="encounterId">The stable encounter-plan identifier.</param>
        /// <param name="status">The persistent lifecycle state.</param>
        public DungeonEncounterSaveState(string encounterId, DungeonEncounterSaveStatus status)
        {
            EncounterId = DungeonSaveContractGuard.RequiredId(encounterId, nameof(encounterId));
            if (!Enum.IsDefined(typeof(DungeonEncounterSaveStatus), status))
                throw new ArgumentOutOfRangeException(
                    nameof(status),
                    "Encounter status is undefined."
                );
            Status = status;
        }

        /// <summary>Gets the stable encounter-plan identifier.</summary>
        public string EncounterId { get; }

        /// <summary>Gets the persistent lifecycle state.</summary>
        public DungeonEncounterSaveStatus Status { get; }
    }

    /// <summary>Associates one persisted enemy creature with its owning encounter.</summary>
    public sealed class DungeonEncounterCreatureSaveState
    {
        /// <summary>Creates an encounter creature record.</summary>
        /// <param name="encounterId">The stable owning encounter identifier.</param>
        /// <param name="creature">The complete enemy actor state.</param>
        public DungeonEncounterCreatureSaveState(
            string encounterId,
            DungeonCreatureSaveState creature
        )
        {
            EncounterId = DungeonSaveContractGuard.RequiredId(encounterId, nameof(encounterId));
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
        }

        /// <summary>Gets the stable owning encounter identifier.</summary>
        public string EncounterId { get; }

        /// <summary>Gets the complete enemy actor state.</summary>
        public DungeonCreatureSaveState Creature { get; }
    }

    /// <summary>Records one generated door's mutable state.</summary>
    public sealed class DungeonDoorSaveState
    {
        /// <summary>Creates a door state record.</summary>
        /// <param name="doorId">The stable generated door identifier.</param>
        /// <param name="isOpen">Whether the door has been opened.</param>
        public DungeonDoorSaveState(string doorId, bool isOpen)
        {
            DoorId = DungeonSaveContractGuard.RequiredId(doorId, nameof(doorId));
            IsOpen = isOpen;
        }

        /// <summary>Gets the stable generated door identifier.</summary>
        public string DoorId { get; }

        /// <summary>Gets whether the door has been opened.</summary>
        public bool IsOpen { get; }
    }

    /// <summary>Represents one versioned mutable floor-state document.</summary>
    public sealed class DungeonFloorSaveState
    {
        /// <summary>Creates a complete per-depth state document.</summary>
        /// <param name="documentVersion">The explicit floor schema version.</param>
        /// <param name="depth">The nonnegative generated depth.</param>
        /// <param name="staticFloorJson">Canonical generator JSON used to reconstruct topology, with no mutable runtime state.</param>
        /// <param name="doors">Every generated door state.</param>
        /// <param name="encounters">Every planned encounter lifecycle state.</param>
        /// <param name="creatures">Every materialized encounter creature, including defeated creatures.</param>
        public DungeonFloorSaveState(
            int documentVersion,
            int depth,
            string staticFloorJson,
            IEnumerable<DungeonDoorSaveState> doors,
            IEnumerable<DungeonEncounterSaveState> encounters,
            IEnumerable<DungeonEncounterCreatureSaveState> creatures
        )
        {
            if (documentVersion <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(documentVersion),
                    "Document version must be positive."
                );
            if (depth < 0)
                throw new ArgumentOutOfRangeException(nameof(depth), "Depth cannot be negative.");
            DocumentVersion = documentVersion;
            Depth = depth;
            StaticFloorJson = DungeonSaveContractGuard.CanonicalStaticFloorJson(
                staticFloorJson,
                nameof(staticFloorJson)
            );
            Doors = DungeonSaveContractGuard.UniqueSorted(
                doors,
                door => door.DoorId,
                nameof(doors)
            );
            Encounters = DungeonSaveContractGuard.UniqueSorted(
                encounters,
                encounter => encounter.EncounterId,
                nameof(encounters)
            );
            Creatures = DungeonSaveContractGuard.UniqueSorted(
                creatures,
                creature => creature.Creature.InstanceId,
                nameof(creatures)
            );

            Dictionary<string, DungeonEncounterSaveStatus> statusByEncounter =
                Encounters.ToDictionary(
                    encounter => encounter.EncounterId,
                    encounter => encounter.Status,
                    StringComparer.Ordinal
                );
            if (Creatures.Any(creature => !statusByEncounter.ContainsKey(creature.EncounterId)))
                throw new ArgumentException(
                    "Every floor creature must belong to a persisted encounter.",
                    nameof(creatures)
                );
            DungeonSaveContractGuard.RequireUnique(
                Creatures
                    .Where(creature => !creature.Creature.IsDefeated)
                    .Select(creature => creature.Creature.Cell.X + ":" + creature.Creature.Cell.Z),
                nameof(creatures)
            );
            foreach (DungeonEncounterSaveState encounter in Encounters)
            {
                DungeonEncounterCreatureSaveState[] members = Creatures
                    .Where(creature => creature.EncounterId == encounter.EncounterId)
                    .ToArray();
                if (encounter.Status == DungeonEncounterSaveStatus.Dormant && members.Length > 0)
                    throw new ArgumentException(
                        "Dormant encounters cannot contain materialized creatures.",
                        nameof(creatures)
                    );
                if (
                    encounter.Status == DungeonEncounterSaveStatus.Cleared
                    && members.Any(member => !member.Creature.IsDefeated)
                )
                {
                    throw new ArgumentException(
                        "Cleared encounters cannot contain living creatures.",
                        nameof(creatures)
                    );
                }
                if (
                    (
                        encounter.Status == DungeonEncounterSaveStatus.Active
                        || encounter.Status == DungeonEncounterSaveStatus.Suspended
                    ) && !members.Any(member => !member.Creature.IsDefeated)
                )
                {
                    throw new ArgumentException(
                        "Active or suspended encounters require a living materialized creature.",
                        nameof(creatures)
                    );
                }
            }
        }

        /// <summary>Gets the explicit floor document version.</summary>
        public int DocumentVersion { get; }

        /// <summary>Gets the nonnegative generated depth.</summary>
        public int Depth { get; }

        /// <summary>
        /// Gets canonical generator JSON sufficient to reconstruct static topology. Mutable door,
        /// encounter, and creature state is intentionally excluded and stored by this document.
        /// </summary>
        public string StaticFloorJson { get; }

        /// <summary>Gets doors ordered by stable ID.</summary>
        public IReadOnlyList<DungeonDoorSaveState> Doors { get; }

        /// <summary>Gets encounter groups ordered by stable ID.</summary>
        public IReadOnlyList<DungeonEncounterSaveState> Encounters { get; }

        /// <summary>Gets materialized creatures ordered by stable instance ID.</summary>
        public IReadOnlyList<DungeonEncounterCreatureSaveState> Creatures { get; }
    }

    /// <summary>Indexes one per-depth JSON document from the run manifest.</summary>
    public sealed class DungeonFloorSaveReference
    {
        /// <summary>Creates a generated-depth index entry.</summary>
        /// <param name="depth">The nonnegative generated depth.</param>
        /// <param name="documentVersion">The expected floor document version.</param>
        /// <param name="relativePath">The repository-relative JSON path.</param>
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

        /// <summary>Gets the repository-relative JSON path.</summary>
        public string RelativePath { get; }

        /// <summary>Creates the canonical current-schema reference for a depth.</summary>
        /// <param name="depth">The nonnegative generated depth.</param>
        /// <returns>A reference using the current version and stable depth path.</returns>
        public static DungeonFloorSaveReference Current(int depth) =>
            new(depth, DungeonSaveSchema.FloorStateVersion, CanonicalPath(depth));

        internal static string CanonicalPath(int depth) =>
            $"floors/depth-{depth.ToString("D4", CultureInfo.InvariantCulture)}.json";
    }

    /// <summary>Represents the versioned run manifest published as one atomic autosave.</summary>
    public sealed class DungeonRunSaveManifest
    {
        /// <summary>Creates a complete immutable run manifest.</summary>
        /// <param name="documentVersion">The explicit manifest schema version.</param>
        /// <param name="startingSeed">The seed that owns the generated run.</param>
        /// <param name="generatorVersion">The stable generator algorithm/version identifier.</param>
        /// <param name="currentDepth">The depth containing the saved party.</param>
        /// <param name="party">The complete current party state.</param>
        /// <param name="generatedFloors">The generated-depth JSON index.</param>
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

        /// <summary>Gets the explicit manifest document version.</summary>
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

    /// <summary>
    /// Represents one complete repository transaction: a run manifest and every floor document it
    /// indexes. Repositories must validate the whole value before publishing any part of it.
    /// </summary>
    public sealed class DungeonRunSave
    {
        /// <summary>Creates a complete autosave transaction.</summary>
        /// <param name="manifest">The run manifest and generated-depth index.</param>
        /// <param name="floors">Exactly one floor document per manifest index entry.</param>
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

        /// <summary>Gets floor documents in ascending depth order.</summary>
        public IReadOnlyList<DungeonFloorSaveState> Floors { get; }
    }
}
