using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Removes duplicate room entrances while preserving distinct outside regions and substantial loops.
    /// </summary>
    internal static class DungeonDoorPostProcessor
    {
        internal const int MinimumLoopPathLengthCells = 8;
        private static readonly DungeonCell[] Directions =
        {
            new(1, 0),
            new(0, 1),
            new(0, -1),
            new(-1, 0)
        };

        /// <summary>
        /// Selects the doors that remain after entrances to the same nearby outside path are collapsed.
        /// </summary>
        /// <param name="rows">Highest-Z-first dungeon rows.</param>
        /// <param name="rooms">Generated rooms in stable order.</param>
        /// <param name="doors">Generated doors in stable order.</param>
        /// <param name="minimumLoopPathLength">
        /// Minimum room-excluding path distance that makes another entrance a meaningful loop.
        /// </param>
        /// <returns>Retained door cells in the original door order.</returns>
        internal static IReadOnlyList<DungeonCell> SelectRequiredDoors(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IReadOnlyList<DungeonDoor> doors,
            int minimumLoopPathLength)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (rooms == null)
                throw new ArgumentNullException(nameof(rooms));
            if (doors == null)
                throw new ArgumentNullException(nameof(doors));
            if (minimumLoopPathLength < 1)
                throw new ArgumentOutOfRangeException(nameof(minimumLoopPathLength));

            HashSet<DungeonCell> activeDoorCells = new(doors.Select(door => door.Cell));
            foreach (DungeonRoom room in rooms.OrderBy(room => room.Id))
            {
                List<DungeonCell> roomDoors = doors
                    .Where(door =>
                        activeDoorCells.Contains(door.Cell) &&
                        IsAdjacentToRoom(door.Cell, room))
                    .Select(door => door.Cell)
                    .ToList();
                if (roomDoors.Count < 2)
                    continue;

                HashSet<DungeonCell> excludedEntrances = new(roomDoors);
                List<DungeonCell> retainedOutsideCells = new();
                foreach (DungeonCell doorCell in roomDoors)
                {
                    if (!TryGetOutsideNeighbor(rows, room, doorCell, out DungeonCell outsideCell))
                        continue;
                    if (retainedOutsideCells.Count == 0)
                    {
                        retainedOutsideCells.Add(outsideCell);
                        continue;
                    }

                    Dictionary<DungeonCell, int> distances = DistancesFrom(
                        rows,
                        outsideCell,
                        room,
                        activeDoorCells,
                        excludedEntrances);
                    bool duplicatesNearbyPath = retainedOutsideCells.Any(retained =>
                        distances.TryGetValue(retained, out int distance) &&
                        distance < minimumLoopPathLength);
                    if (duplicatesNearbyPath)
                    {
                        activeDoorCells.Remove(doorCell);
                    }
                    else
                    {
                        retainedOutsideCells.Add(outsideCell);
                    }
                }
            }

            return Array.AsReadOnly(
                doors
                    .Where(door => activeDoorCells.Contains(door.Cell))
                    .Select(door => door.Cell)
                    .ToArray());
        }

        private static Dictionary<DungeonCell, int> DistancesFrom(
            IReadOnlyList<string> rows,
            DungeonCell start,
            DungeonRoom excludedRoom,
            HashSet<DungeonCell> activeDoorCells,
            HashSet<DungeonCell> excludedEntrances)
        {
            Dictionary<DungeonCell, int> distances = new() { [start] = 0 };
            Queue<DungeonCell> queue = new();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                DungeonCell current = queue.Dequeue();
                foreach (DungeonCell direction in Directions)
                {
                    DungeonCell next = new(
                        current.X + direction.X,
                        current.Z + direction.Z);
                    if (distances.ContainsKey(next) ||
                        !CanTraverse(
                            rows,
                            next,
                            excludedRoom,
                            activeDoorCells,
                            excludedEntrances))
                    {
                        continue;
                    }

                    distances[next] = distances[current] + 1;
                    queue.Enqueue(next);
                }
            }

            return distances;
        }

        private static bool CanTraverse(
            IReadOnlyList<string> rows,
            DungeonCell cell,
            DungeonRoom excludedRoom,
            HashSet<DungeonCell> activeDoorCells,
            HashSet<DungeonCell> excludedEntrances)
        {
            if (!InBounds(rows, cell) ||
                Contains(excludedRoom, cell) ||
                excludedEntrances.Contains(cell))
            {
                return false;
            }

            char symbol = Symbol(rows, cell);
            return symbol == '.' ||
                   symbol == 'D' && activeDoorCells.Contains(cell);
        }

        private static bool TryGetOutsideNeighbor(
            IReadOnlyList<string> rows,
            DungeonRoom room,
            DungeonCell door,
            out DungeonCell outside)
        {
            foreach (DungeonCell direction in Directions)
            {
                DungeonCell inside = new(
                    door.X + direction.X,
                    door.Z + direction.Z);
                if (!Contains(room, inside))
                    continue;

                DungeonCell candidate = new(
                    door.X - direction.X,
                    door.Z - direction.Z);
                if (InBounds(rows, candidate) && IsSerializedWalkable(rows, candidate))
                {
                    outside = candidate;
                    return true;
                }
            }

            outside = default;
            return false;
        }

        private static bool IsAdjacentToRoom(DungeonCell cell, DungeonRoom room)
        {
            return cell.X >= room.MinimumX && cell.X <= room.MaximumX &&
                   (cell.Z == room.MinimumZ - 1 || cell.Z == room.MaximumZ + 1) ||
                   cell.Z >= room.MinimumZ && cell.Z <= room.MaximumZ &&
                   (cell.X == room.MinimumX - 1 || cell.X == room.MaximumX + 1);
        }

        private static bool Contains(DungeonRoom room, DungeonCell cell)
        {
            return cell.X >= room.MinimumX && cell.X <= room.MaximumX &&
                   cell.Z >= room.MinimumZ && cell.Z <= room.MaximumZ;
        }

        private static bool IsSerializedWalkable(
            IReadOnlyList<string> rows,
            DungeonCell cell)
        {
            char symbol = Symbol(rows, cell);
            return symbol == '.' || symbol == 'D';
        }

        private static bool InBounds(IReadOnlyList<string> rows, DungeonCell cell)
        {
            return cell.Z >= 0 && cell.Z < rows.Count &&
                   cell.X >= 0 && cell.X < rows[rows.Count - 1 - cell.Z].Length;
        }

        private static char Symbol(IReadOnlyList<string> rows, DungeonCell cell)
        {
            return rows[rows.Count - 1 - cell.Z][cell.X];
        }
    }
}
