using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Version 2 deterministic level contract shared by generation, scene population, encounters,
    /// traversal, and persistence. Rows use highest-Z-first orientation.
    /// </summary>
    public sealed class DungeonLevelDocument
    {
        /// <summary>The current deterministic document version accepted by the strict parser.</summary>
        public const int CurrentVersion = 2;

        /// <summary>Creates a complete version 2 level document and snapshots every supplied collection.</summary>
        /// <param name="generation">Required generation provenance and stable seed states.</param>
        /// <param name="rows">Required highest-Z-first rows using space, <c>#</c>, <c>.</c>, and <c>D</c>.</param>
        /// <param name="rooms">Stable non-overlapping rooms ordered by positive ID.</param>
        /// <param name="doors">Stable door records with exactly one record for every <c>D</c> row cell.</param>
        /// <param name="stairs">At most one down and one up stair, in traversal order.</param>
        /// <param name="startCell">The default walkable player start and arrival fallback.</param>
        /// <param name="safeCells">Ordered unique walkable cells safe for party arrival.</param>
        /// <param name="objects">Deterministic object placements with unique stable IDs.</param>
        /// <param name="encounterPlans">Deterministic encounter plans with unique stable IDs.</param>
        /// <param name="runtimeState">Optional mutable state; absence represents a pristine generated level.</param>
        /// <exception cref="ArgumentNullException">A required object or collection is null.</exception>
        public DungeonLevelDocument(
            DungeonGenerationMetadata generation,
            IEnumerable<string> rows,
            IEnumerable<DungeonRoom> rooms,
            IEnumerable<DungeonDoor> doors,
            IEnumerable<DungeonStair> stairs,
            DungeonCell startCell,
            IEnumerable<DungeonCell> safeCells,
            IEnumerable<DungeonObjectPlacement> objects,
            IEnumerable<DungeonEncounterPlan> encounterPlans,
            DungeonRuntimeState runtimeState = null)
        {
            Generation = generation ?? throw new ArgumentNullException(nameof(generation));
            Rows = Copy(rows, nameof(rows)); Rooms = Copy(rooms, nameof(rooms)); Doors = Copy(doors, nameof(doors));
            Stairs = Copy(stairs, nameof(stairs)); StartCell = startCell; SafeCells = Copy(safeCells, nameof(safeCells));
            Objects = Copy(objects, nameof(objects)); EncounterPlans = Copy(encounterPlans, nameof(encounterPlans));
            RuntimeState = runtimeState;
        }

        /// <summary>Gets the format version, currently <see cref="CurrentVersion"/>.</summary>
        public int Version => CurrentVersion;
        /// <summary>Gets generation provenance and seed states.</summary>
        public DungeonGenerationMetadata Generation { get; }
        /// <summary>Gets the snapshotted highest-Z-first row strings using space, wall, ground, and door symbols.</summary>
        public IReadOnlyList<string> Rows { get; }
        /// <summary>Gets stable rooms ordered by ID.</summary>
        public IReadOnlyList<DungeonRoom> Rooms { get; }
        /// <summary>Gets stable unlocked doors, one for every <c>D</c> row cell, ordered by ID.</summary>
        public IReadOnlyList<DungeonDoor> Doors { get; }
        /// <summary>Gets stairs in traversal order; generated two-stair documents list down before up.</summary>
        public IReadOnlyList<DungeonStair> Stairs { get; }
        /// <summary>Gets the default player start and arrival fallback.</summary>
        public DungeonCell StartCell { get; }
        /// <summary>Gets ordered cells safe for arrival or fallback spawning.</summary>
        public IReadOnlyList<DungeonCell> SafeCells { get; }
        /// <summary>Gets deterministic object placements.</summary>
        public IReadOnlyList<DungeonObjectPlacement> Objects { get; }
        /// <summary>Gets deterministic encounter plans including threat, adjusted budget, creatures, and spawn cells.</summary>
        public IReadOnlyList<DungeonEncounterPlan> EncounterPlans { get; }
        /// <summary>
        /// Gets optional mutable runtime state whose door and encounter ID sets exactly mirror the
        /// corresponding persisted flags, or absence when all persisted facts are pristine.
        /// </summary>
        public DungeonRuntimeState RuntimeState { get; }
        /// <summary>Gets the row width, or zero when rows are absent; validated documents are rectangular.</summary>
        public int Width => Rows.Count == 0 ? 0 : Rows[0].Length;
        /// <summary>Gets the row count.</summary>
        public int Height => Rows.Count;

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameter)
        {
            if (values == null) throw new ArgumentNullException(parameter);
            return Array.AsReadOnly(values.ToArray());
        }
    }
}
