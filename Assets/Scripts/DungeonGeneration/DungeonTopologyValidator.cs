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
        private static readonly DungeonLayout[] SupportedLayouts =
        {
            DungeonLayout.Box,
            DungeonLayout.Cross,
            DungeonLayout.Round
        };

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
        /// two-opposite-neighbor shape, with every room adjacent to at least one valid door.
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

            HashSet<int> roomIdsWithDoors = new();
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

                DungeonRoom[] adjacentRooms = rooms
                    .Where(room => openNeighbors.Any(neighbor => Contains(room, neighbor)))
                    .ToArray();
                if (adjacentRooms.Length == 0)
                    return false;
                foreach (DungeonRoom room in adjacentRooms)
                    roomIdsWithDoors.Add(room.Id);
            }

            return rooms.All(room => roomIdsWithDoors.Contains(room.Id));
        }

        /// <summary>
        /// Returns whether every serialized space matches exactly one supported initialization
        /// mask. Masked cells cannot be carved, while every allowed cell serializes as a wall,
        /// floor, or door even when later generation stages leave it unused.
        /// </summary>
        internal static bool HasProducibleLayoutMask(IReadOnlyList<string> rows)
        {
            int width = rows[0].Length;
            int height = rows.Count;
            foreach (DungeonLayout layout in SupportedLayouts)
            {
                bool matches = true;
                for (int z = 0; z < height && matches; z++)
                for (int x = 0; x < width; x++)
                {
                    bool serializedAsMasked = Symbol(rows, new DungeonCell(x, z)) == ' ';
                    if (serializedAsMasked != IsMaskedByLayout(layout, width, height, x, z))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return true;
            }

            return false;
        }

        /// <summary>Returns whether a cell is masked by one supported generator layout.</summary>
        internal static bool IsMaskedByLayout(
            DungeonLayout layout,
            int width,
            int height,
            int x,
            int z)
        {
            if (layout == DungeonLayout.Round)
            {
                int centerX = (width - 1) / 2;
                int centerZ = (height - 1) / 2;
                long deltaX = x - centerX;
                long deltaZ = z - centerZ;
                return deltaX * deltaX + deltaZ * deltaZ > (long)centerX * centerX;
            }

            int maskRow = z * 3 / height;
            int maskColumn = x * 3 / width;
            if (layout == DungeonLayout.Box)
                return maskRow == 1 && maskColumn == 1;
            if (layout == DungeonLayout.Cross)
                return maskRow != 1 && maskColumn != 1;
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "Dungeon layout is undefined.");
        }

        /// <summary>
        /// Returns whether door records retain the generator's ordered stable IDs and sill parity.
        /// Odd-aligned room sills always place a door on a cell with exactly one odd coordinate.
        /// </summary>
        internal static bool HasProducibleDoorRecords(IReadOnlyList<DungeonDoor> doors)
        {
            for (int index = 0; index < doors.Count; index++)
            {
                DungeonDoor door = doors[index];
                string expectedId = "door-" +
                    (index + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                bool xIsOdd = (door.Cell.X & 1) == 1;
                bool zIsOdd = (door.Cell.Z & 1) == 1;
                if (!string.Equals(door.Id, expectedId, StringComparison.Ordinal) ||
                    xIsOdd == zIsOdd)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the generator-owned safe-arrival sequence: stair arrivals in record order,
        /// followed by room centers in room order, or the first eight non-door walkable cells in
        /// Z-then-X order when neither source provides a cell.
        /// </summary>
        internal static IReadOnlyList<DungeonCell> BuildSafeCells(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IReadOnlyList<DungeonStair> stairs)
        {
            List<DungeonCell> safe = new();
            foreach (DungeonStair stair in stairs)
            {
                if (!safe.Contains(stair.ArrivalCell))
                    safe.Add(stair.ArrivalCell);
            }

            foreach (DungeonRoom room in rooms)
            {
                DungeonCell center = new(
                    (room.MinimumX + room.MaximumX) / 2,
                    (room.MinimumZ + room.MaximumZ) / 2);
                if (IsWalkable(rows, center) && !safe.Contains(center))
                    safe.Add(center);
            }

            if (safe.Count == 0)
            {
                for (int z = 0; z < rows.Count && safe.Count < 8; z++)
                for (int x = 0; x < rows[0].Length && safe.Count < 8; x++)
                {
                    DungeonCell cell = new(x, z);
                    if (Symbol(rows, cell) == '.')
                        safe.Add(cell);
                }
            }

            return Array.AsReadOnly(safe.ToArray());
        }

        /// <summary>Returns whether safe cells exactly preserve the generator-owned sequence.</summary>
        internal static bool HasProducibleSafeCells(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IReadOnlyList<DungeonStair> stairs,
            IReadOnlyList<DungeonCell> safeCells) =>
            safeCells.SequenceEqual(BuildSafeCells(rows, rooms, stairs));

        /// <summary>
        /// Returns whether room records use the dimensions, coarse-grid alignment, and stable ID
        /// sequence that the current generator can produce for the containing map.
        /// </summary>
        internal static bool HasProducibleRoomRecords(
            int mapWidth,
            int mapHeight,
            IReadOnlyList<DungeonRoom> rooms)
        {
            int maximumRoomSize = Math.Min(mapWidth, mapHeight) - 4;
            for (int index = 0; index < rooms.Count; index++)
            {
                DungeonRoom room = rooms[index];
                long width = (long)room.MaximumX - room.MinimumX + 1;
                long height = (long)room.MaximumZ - room.MinimumZ + 1;
                if (room.Id != index + 1 ||
                    room.MinimumX < 1 || room.MinimumZ < 1 ||
                    room.MaximumX > mapWidth - 2 || room.MaximumZ > mapHeight - 2 ||
                    (room.MinimumX & 1) == 0 || (room.MinimumZ & 1) == 0 ||
                    (room.MaximumX & 1) == 0 || (room.MaximumZ & 1) == 0 ||
                    width < 3 || height < 3 ||
                    width > maximumRoomSize || height > maximumRoomSize ||
                    (width & 1) == 0 || (height & 1) == 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns whether stair records use the current generator's count-dependent IDs, kinds,
        /// ordering, distinct geometry, and odd-coordinate endpoint scan.
        /// </summary>
        internal static bool HasProducibleStairRecords(IReadOnlyList<DungeonStair> stairs)
        {
            if (stairs.Count > 2)
                return false;

            HashSet<DungeonCell> endpoints = new();
            HashSet<DungeonCell> arrivals = new();
            for (int index = 0; index < stairs.Count; index++)
            {
                DungeonStair stair = stairs[index];
                DungeonStairKind expectedKind = index == 0
                    ? DungeonStairKind.Down
                    : DungeonStairKind.Up;
                string expectedId = index == 0 ? "stair-down" : "stair-up";
                if (stair.Kind != expectedKind ||
                    !string.Equals(stair.Id, expectedId, StringComparison.Ordinal) ||
                    (stair.Cell.X & 1) == 0 ||
                    (stair.Cell.Z & 1) == 0 ||
                    !endpoints.Add(stair.Cell) ||
                    !arrivals.Add(stair.ArrivalCell))
                {
                    return false;
                }
            }

            return !endpoints.Overlaps(arrivals);
        }

        /// <summary>
        /// Returns whether a stair endpoint uses the generator's straight three-cell corridor
        /// runway while every other surrounding endpoint cell remains blocked.
        /// </summary>
        internal static bool MatchesStairEnd(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            DungeonCell cell,
            DungeonCell arrival)
        {
            bool IsCorridor(DungeonCell candidate) =>
                InBounds(rows, candidate) &&
                Symbol(rows, candidate) == '.' &&
                !rooms.Any(room => Contains(room, candidate));
            return MatchesStairEnd(
                rows[0].Length,
                rows.Count,
                cell,
                arrival,
                IsCorridor,
                candidate => IsWalkable(rows, candidate));
        }

        /// <summary>
        /// Applies the shared stair-end geometry to generator state without requiring serialized rows.
        /// </summary>
        internal static bool MatchesStairEnd(
            int width,
            int height,
            DungeonCell cell,
            DungeonCell arrival,
            Func<DungeonCell, bool> isCorridor,
            Func<DungeonCell, bool> isWalkable)
        {
            DungeonCell direction = new(arrival.X - cell.X, arrival.Z - cell.Z);
            if (Math.Abs(direction.X) + Math.Abs(direction.Z) != 1 ||
                !InBounds(width, height, cell) ||
                !InBounds(width, height, arrival))
            {
                return false;
            }

            DungeonCell far = new(
                cell.X + direction.X * 2,
                cell.Z + direction.Z * 2);
            if (!InBounds(width, height, far) ||
                !isCorridor(cell) ||
                !isCorridor(arrival) ||
                !isCorridor(far))
            {
                return false;
            }

            for (int zOffset = -1; zOffset <= 1; zOffset++)
            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                if (xOffset == 0 && zOffset == 0)
                    continue;
                if (xOffset == direction.X && zOffset == direction.Z)
                    continue;

                DungeonCell neighbor = new(cell.X + xOffset, cell.Z + zOffset);
                if (InBounds(width, height, neighbor) && isWalkable(neighbor))
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
            InBounds(rows[0].Length, rows.Count, cell);

        private static bool InBounds(int width, int height, DungeonCell cell) =>
            cell.X >= 0 && cell.Z >= 0 && cell.X < width && cell.Z < height;

        private static char Symbol(IReadOnlyList<string> rows, DungeonCell cell) =>
            rows[rows.Count - 1 - cell.Z][cell.X];

        private static bool Contains(DungeonRoom room, DungeonCell cell) =>
            cell.X >= room.MinimumX && cell.X <= room.MaximumX &&
            cell.Z >= room.MinimumZ && cell.Z <= room.MaximumZ;
    }
}
