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
        /// <summary>The current deterministic document version.</summary>
        public const int CurrentVersion = 2;

        /// <summary>Creates a complete version 2 level document.</summary>
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

        /// <summary>Gets the format version.</summary>
        public int Version => CurrentVersion;
        /// <summary>Gets generation provenance and seed states.</summary>
        public DungeonGenerationMetadata Generation { get; }
        /// <summary>Gets highest-Z-first row strings using space, wall, ground, and door symbols.</summary>
        public IReadOnlyList<string> Rows { get; }
        /// <summary>Gets stable rooms ordered by ID.</summary>
        public IReadOnlyList<DungeonRoom> Rooms { get; }
        /// <summary>Gets stable unlocked doors ordered by ID.</summary>
        public IReadOnlyList<DungeonDoor> Doors { get; }
        /// <summary>Gets up/down stairs in traversal order.</summary>
        public IReadOnlyList<DungeonStair> Stairs { get; }
        /// <summary>Gets the default player start and arrival fallback.</summary>
        public DungeonCell StartCell { get; }
        /// <summary>Gets ordered cells safe for arrival or fallback spawning.</summary>
        public IReadOnlyList<DungeonCell> SafeCells { get; }
        /// <summary>Gets deterministic object placements.</summary>
        public IReadOnlyList<DungeonObjectPlacement> Objects { get; }
        /// <summary>Gets deterministic encounter plans.</summary>
        public IReadOnlyList<DungeonEncounterPlan> EncounterPlans { get; }
        /// <summary>Gets optional mutable runtime state, or null for a pristine generated level.</summary>
        public DungeonRuntimeState RuntimeState { get; }
        /// <summary>Gets the row width, or zero when rows are absent.</summary>
        public int Width => Rows.Count == 0 ? 0 : Rows[0].Length;
        /// <summary>Gets the row count.</summary>
        public int Height => Rows.Count;

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameter)
        {
            if (values == null) throw new ArgumentNullException(parameter);
            return values.ToArray();
        }
    }
}
