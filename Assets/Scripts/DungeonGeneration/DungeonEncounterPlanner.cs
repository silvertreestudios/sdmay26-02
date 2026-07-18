using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Adds deterministic room encounter plans to a pristine topology document without changing
    /// its topology, decorations, or runtime state.
    /// </summary>
    public sealed class DungeonEncounterPlanner
    {
        private static readonly DungeonCell[] Directions =
        {
            new(1, 0),
            new(0, 1),
            new(0, -1),
            new(-1, 0)
        };

        private readonly IEncounterBuilder builder;

        /// <summary>Creates a planner using <see cref="DungeonEncounterBuilder"/>.</summary>
        public DungeonEncounterPlanner()
            : this(new DungeonEncounterBuilder())
        {
        }

        /// <summary>Creates a planner with an explicit testable composition service.</summary>
        /// <param name="builder">The non-null encounter composition service.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        public DungeonEncounterPlanner(IEncounterBuilder builder)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>
        /// Plans every non-arrival room using the document's isolated encounter seed substream.
        /// </summary>
        /// <param name="source">A pristine generated document with no existing encounter plans.</param>
        /// <param name="partyLevel">The party's PF2e level.</param>
        /// <param name="partySize">The positive player-character count.</param>
        /// <param name="candidates">Unique existing enemy definitions.</param>
        /// <returns>
        /// A new document that preserves all source sections and adds stable encounter plans.
        /// Empty or unsatisfiable catalogs still produce an empty plan for each eligible room.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="candidates"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The source already contains encounter plans or mutable runtime state.
        /// </exception>
        public DungeonLevelDocument Plan(
            DungeonLevelDocument source,
            int partyLevel,
            int partySize,
            IReadOnlyList<DungeonEncounterCandidate> candidates)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            IDungeonRandom random = new SystemDungeonRandom(
                DungeonSeedSequence.ForSubstream(
                    source.Generation.RunSeed,
                    source.Generation.Depth,
                    DungeonSeedSubstream.Encounter));
            return Plan(source, partyLevel, partySize, candidates, random);
        }

        internal DungeonLevelDocument Plan(
            DungeonLevelDocument source,
            int partyLevel,
            int partySize,
            IReadOnlyList<DungeonEncounterCandidate> candidates,
            IDungeonRandom random)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (partySize <= 0)
                throw new ArgumentOutOfRangeException(nameof(partySize));
            if (source.EncounterPlans.Count != 0 || source.RuntimeState != null)
            {
                throw new InvalidOperationException(
                    "Encounter planning requires a pristine document without plans or runtime state.");
            }

            HashSet<DungeonCell> reachable = ReachableCells(source.Rows, source.StartCell);
            HashSet<DungeonCell> reserved = new(source.SafeCells);
            reserved.Add(source.StartCell);
            foreach (DungeonDoor door in source.Doors)
                reserved.Add(door.Cell);
            foreach (DungeonStair stair in source.Stairs)
            {
                reserved.Add(stair.Cell);
                reserved.Add(stair.ArrivalCell);
            }
            foreach (DungeonObjectPlacement placement in source.Objects)
                reserved.Add(placement.Cell);

            DungeonCell arrivalAnchor = source.StartCell;
            if (source.Generation.Depth > 0)
            {
                DungeonStair up = source.Stairs.FirstOrDefault(
                    stair => stair.Kind == DungeonStairKind.Up);
                if (up != null)
                    arrivalAnchor = up.ArrivalCell;
            }
            int? excludedRoomId = FindNearestRoomId(source, arrivalAnchor);

            List<DungeonEncounterPlan> plans = new();
            foreach (DungeonRoom room in source.Rooms.OrderBy(candidate => candidate.Id))
            {
                if (room.Id == excludedRoomId)
                    continue;

                List<DungeonCell> cells = CellsInRoom(room)
                    .Where(cell => reachable.Contains(cell) && !reserved.Contains(cell))
                    .OrderBy(cell => cell.Z)
                    .ThenBy(cell => cell.X)
                    .ToList();
                DungeonEncounterThreat threat = DungeonEncounterRules.SelectThreat(
                    source.Generation.Depth,
                    random);
                DungeonEncounterBuildResult composition = builder.Build(
                    partyLevel,
                    partySize,
                    threat,
                    candidates,
                    cells.Count,
                    random);
                Shuffle(cells, random);
                DungeonCell[] spawnCells = cells
                    .Take(composition.CreatureIds.Count)
                    .ToArray();
                plans.Add(new DungeonEncounterPlan(
                    "encounter-" + room.Id.ToString("D4", CultureInfo.InvariantCulture),
                    room.Id,
                    threat,
                    composition.Budget,
                    spawnCells,
                    composition.CreatureIds));
            }

            return new DungeonLevelDocument(
                source.Generation,
                source.Rows,
                source.Rooms,
                source.Doors,
                source.Stairs,
                source.StartCell,
                source.SafeCells,
                source.Objects,
                plans);
        }

        private static int? FindNearestRoomId(
            DungeonLevelDocument source,
            DungeonCell anchor)
        {
            Dictionary<DungeonCell, int> distances = Distances(source.Rows, anchor);
            return source.Rooms
                .Select(room => new
                {
                    room.Id,
                    Distance = CellsInRoom(room)
                        .Where(distances.ContainsKey)
                        .Select(cell => distances[cell])
                        .DefaultIfEmpty(int.MaxValue)
                        .Min()
                })
                .Where(candidate => candidate.Distance != int.MaxValue)
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Id)
                .Select(candidate => (int?)candidate.Id)
                .FirstOrDefault();
        }

        private static HashSet<DungeonCell> ReachableCells(
            IReadOnlyList<string> rows,
            DungeonCell start) => new(Distances(rows, start).Keys);

        private static Dictionary<DungeonCell, int> Distances(
            IReadOnlyList<string> rows,
            DungeonCell start)
        {
            Dictionary<DungeonCell, int> distances = new();
            if (!IsWalkable(rows, start))
                return distances;

            Queue<DungeonCell> pending = new();
            distances.Add(start, 0);
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                DungeonCell current = pending.Dequeue();
                foreach (DungeonCell direction in Directions)
                {
                    DungeonCell next = new(
                        current.X + direction.X,
                        current.Z + direction.Z);
                    if (IsWalkable(rows, next) && !distances.ContainsKey(next))
                    {
                        distances.Add(next, distances[current] + 1);
                        pending.Enqueue(next);
                    }
                }
            }

            return distances;
        }

        private static IEnumerable<DungeonCell> CellsInRoom(DungeonRoom room)
        {
            for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
            for (int x = room.MinimumX; x <= room.MaximumX; x++)
                yield return new DungeonCell(x, z);
        }

        private static bool IsWalkable(
            IReadOnlyList<string> rows,
            DungeonCell cell)
        {
            if (rows.Count == 0 || cell.Z < 0 || cell.Z >= rows.Count || cell.X < 0)
                return false;
            string row = rows[rows.Count - 1 - cell.Z];
            if (cell.X >= row.Length)
                return false;
            return row[cell.X] == '.' || row[cell.X] == 'D';
        }

        private static void Shuffle<T>(IList<T> values, IDungeonRandom random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swapIndex = random.NextInt(index + 1);
                (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
            }
        }
    }
}
