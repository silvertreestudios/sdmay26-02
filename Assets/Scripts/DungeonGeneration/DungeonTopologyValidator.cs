using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Mirrors the topology invariants owned by the current deterministic generator without
    /// coupling persistence validation to its mutable generation state.
    /// </summary>
    internal static class DungeonTopologyValidator
    {
        private static readonly DungeonCell[] Directions =
        {
            new(1, 0),
            new(0, 1),
            new(0, -1),
            new(-1, 0)
        };

        /// <summary>Returns whether every walkable cell belongs to the component containing <paramref name="start"/>.</summary>
        internal static bool AreAllWalkableCellsReachable(
            IReadOnlyList<string> rows,
            DungeonCell start)
        {
            if (!IsWalkable(rows, start))
                return false;

            HashSet<DungeonCell> walkable = WalkableCells(rows);
            HashSet<DungeonCell> reached = new() { start };
            Queue<DungeonCell> pending = new();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                DungeonCell current = pending.Dequeue();
                foreach (DungeonCell direction in Directions)
                {
                    DungeonCell next = new(
                        current.X + direction.X,
                        current.Z + direction.Z);
                    if (walkable.Contains(next) && reached.Add(next))
                        pending.Enqueue(next);
                }
            }

            return reached.Count == walkable.Count;
        }

        /// <summary>Returns whether the rows contain one non-empty connected walkable component.</summary>
        internal static bool HasSingleWalkableRegion(IReadOnlyList<string> rows)
        {
            HashSet<DungeonCell> walkable = WalkableCells(rows);
            return walkable.Count > 0 &&
                   AreAllWalkableCellsReachable(rows, walkable.First());
        }

        /// <summary>
        /// Returns whether every walkable crossing from a room interior is represented by exactly
        /// one persisted door on a <c>D</c> cell.
        /// </summary>
        internal static bool HasValidRoomBoundaryCrossings(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IReadOnlyList<DungeonDoor> doors)
        {
            Dictionary<DungeonCell, int> doorRecordsByCell = new();
            foreach (DungeonDoor door in doors)
            {
                doorRecordsByCell.TryGetValue(door.Cell, out int count);
                doorRecordsByCell[door.Cell] = count + 1;
            }

            foreach (DungeonRoom room in rooms)
            {
                for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
                for (int x = room.MinimumX; x <= room.MaximumX; x++)
                {
                    foreach (DungeonCell direction in Directions)
                    {
                        DungeonCell neighbor = new(x + direction.X, z + direction.Z);
                        if (Contains(room, neighbor) || !IsWalkable(rows, neighbor))
                            continue;

                        if (Symbol(rows, neighbor) != 'D' ||
                            !doorRecordsByCell.TryGetValue(neighbor, out int recordCount) ||
                            recordCount != 1)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Returns whether doors form a bijection with <c>D</c> cells and retain the generator's
        /// two-opposite-neighbor shape with at least one adjacent room interior.
        /// </summary>
        internal static bool HasValidDoors(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IReadOnlyList<DungeonDoor> doors)
        {
            if (doors.Select(door => door.Id).Distinct(StringComparer.Ordinal).Count() != doors.Count ||
                doors.Select(door => door.Cell).Distinct().Count() != doors.Count ||
                WalkableCells(rows).Count(cell => Symbol(rows, cell) == 'D') != doors.Count)
            {
                return false;
            }

            foreach (DungeonDoor door in doors)
            {
                if (!InBounds(rows, door.Cell) || Symbol(rows, door.Cell) != 'D')
                    return false;

                List<DungeonCell> openNeighbors = OpenNeighbors(rows, door.Cell);
                if (openNeighbors.Count != 2)
                    return false;
                if (openNeighbors[0].X != openNeighbors[1].X &&
                    openNeighbors[0].Z != openNeighbors[1].Z)
                {
                    return false;
                }

                if (!openNeighbors.Any(neighbor => rooms.Any(room => Contains(room, neighbor))))
                    return false;
            }

            return true;
        }

        private static HashSet<DungeonCell> WalkableCells(IReadOnlyList<string> rows)
        {
            HashSet<DungeonCell> result = new();
            for (int row = 0; row < rows.Count; row++)
            for (int x = 0; x < rows[row].Length; x++)
            {
                if (rows[row][x] == '.' || rows[row][x] == 'D')
                    result.Add(new DungeonCell(x, rows.Count - 1 - row));
            }

            return result;
        }

        private static List<DungeonCell> OpenNeighbors(
            IReadOnlyList<string> rows,
            DungeonCell cell)
        {
            List<DungeonCell> result = new();
            foreach (DungeonCell direction in Directions)
            {
                DungeonCell neighbor = new(cell.X + direction.X, cell.Z + direction.Z);
                if (IsWalkable(rows, neighbor))
                    result.Add(neighbor);
            }

            return result;
        }

        private static bool IsWalkable(IReadOnlyList<string> rows, DungeonCell cell) =>
            InBounds(rows, cell) && (Symbol(rows, cell) == '.' || Symbol(rows, cell) == 'D');

        private static bool InBounds(IReadOnlyList<string> rows, DungeonCell cell) =>
            cell.X >= 0 && cell.Z >= 0 && cell.X < rows[0].Length && cell.Z < rows.Count;

        private static char Symbol(IReadOnlyList<string> rows, DungeonCell cell) =>
            rows[rows.Count - 1 - cell.Z][cell.X];

        private static bool Contains(DungeonRoom room, DungeonCell cell) =>
            cell.X >= room.MinimumX && cell.X <= room.MaximumX &&
            cell.Z >= room.MinimumZ && cell.Z <= room.MaximumZ;
    }
}
