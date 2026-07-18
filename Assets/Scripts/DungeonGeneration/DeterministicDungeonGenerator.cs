using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Generates deterministic room-and-corridor levels using a project-owned translation of Donjon's
    /// logical dungeon stages. It has no Unity, scene, rendering, or global-random dependency.
    /// </summary>
    public sealed class DeterministicDungeonGenerator : IDungeonGenerator
    {
        /// <summary>Maximum deterministic topology attempts, including the initial attempt.</summary>
        public const int MaximumAttempts = 32;
        private static readonly DungeonCell[] Directions = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

        /// <inheritdoc/>
        public DungeonGenerationResult Generate(DungeonGenerationRequest request)
        {
            IReadOnlyList<DungeonGenerationDiagnostic> invalid = ValidateRequest(request);
            if (invalid.Count > 0) return new DungeonGenerationResult(null, invalid);

            DungeonGenerationDiagnostic last = null;
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                ulong topologyState = DungeonSeedSequence.ForTopologyAttempt(request.RunSeed, request.Depth, attempt);
                Attempt state = new(request, attempt, topologyState);
                if (state.TryGenerate(out DungeonLevelDocument document, out string rejection))
                    return new DungeonGenerationResult(document, Array.Empty<DungeonGenerationDiagnostic>());
                last = new DungeonGenerationDiagnostic(DungeonGenerationDiagnosticCode.TopologyRejected, "topology", rejection, attempt);
            }

            return new DungeonGenerationResult(null, new[]
            {
                last,
                new DungeonGenerationDiagnostic(
                    DungeonGenerationDiagnosticCode.RetryLimitExhausted,
                    "topology",
                    "All 32 deterministic topology attempts were rejected. Reduce minimum room/stair requirements, enlarge the map, or change the layout mask.")
            });
        }

        private static IReadOnlyList<DungeonGenerationDiagnostic> ValidateRequest(DungeonGenerationRequest request)
        {
            List<DungeonGenerationDiagnostic> errors = new();
            void Check(bool condition, string field, string message) { if (!condition) errors.Add(new DungeonGenerationDiagnostic(DungeonGenerationDiagnosticCode.InvalidRequest, field, message)); }
            if (request == null) { errors.Add(new DungeonGenerationDiagnostic(DungeonGenerationDiagnosticCode.InvalidRequest, "request", "A generation request is required.")); return errors; }
            Check(request.Depth >= 0, nameof(request.Depth), "Depth must be zero or greater.");
            Check(request.Width >= 15 && request.Width <= 101 && (request.Width & 1) == 1, nameof(request.Width), "Width must be an odd integer from 15 through 101.");
            Check(request.Height >= 15 && request.Height <= 101 && (request.Height & 1) == 1, nameof(request.Height), "Height must be an odd integer from 15 through 101.");
            Check(Enum.IsDefined(typeof(DungeonLayout), request.Layout), nameof(request.Layout), "Layout is not supported.");
            Check(Enum.IsDefined(typeof(DungeonRoomLayout), request.RoomLayout), nameof(request.RoomLayout), "Room layout is not supported.");
            Check(Enum.IsDefined(typeof(DungeonCorridorLayout), request.CorridorLayout), nameof(request.CorridorLayout), "Corridor layout is not supported.");
            Check(request.MinimumRoomSize >= 3 && (request.MinimumRoomSize & 1) == 1, nameof(request.MinimumRoomSize), "Minimum room size must be an odd integer of at least 3.");
            Check(request.MaximumRoomSize >= request.MinimumRoomSize && (request.MaximumRoomSize & 1) == 1, nameof(request.MaximumRoomSize), "Maximum room size must be odd and no smaller than the minimum.");
            Check(request.MaximumRoomSize <= Math.Min(request.Width, request.Height) - 4, nameof(request.MaximumRoomSize), "Maximum room size must leave a two-cell margin inside the map boundary.");
            Check(request.MinimumRoomCount >= 0 && request.MinimumRoomCount <= 999, nameof(request.MinimumRoomCount), "Minimum room count must be from 0 through 999.");
            Check(request.StairCount >= 0 && request.StairCount <= 2, nameof(request.StairCount), "Stair count must be zero, one, or two.");
            Check(request.DeadEndRemovalPercent >= 0 && request.DeadEndRemovalPercent <= 100, nameof(request.DeadEndRemovalPercent), "Dead-end removal must be a percentage from 0 through 100.");
            return errors;
        }

        private enum CellKind : byte { Masked, Empty, Room, Corridor, Door }

        private sealed class Attempt
        {
            private readonly DungeonGenerationRequest request;
            private readonly int attempt;
            private readonly ulong topologyState;
            private readonly IDungeonRandom random;
            private readonly CellKind[,] cells;
            private readonly List<DungeonRoom> rooms = new();
            private readonly List<DungeonDoor> doors = new();
            private readonly List<DungeonStair> stairs = new();

            internal Attempt(DungeonGenerationRequest request, int attempt, ulong topologyState)
            {
                this.request = request; this.attempt = attempt; this.topologyState = topologyState;
                random = new SplitMix64DungeonRandom(topologyState); cells = new CellKind[request.Width, request.Height];
            }

            internal bool TryGenerate(out DungeonLevelDocument document, out string rejection)
            {
                document = null; rejection = null; InitializeMask(); EmplaceRooms();
                if (rooms.Count < request.MinimumRoomCount) { rejection = $"Placed {rooms.Count.ToString(CultureInfo.InvariantCulture)} rooms, fewer than required {request.MinimumRoomCount.ToString(CultureInfo.InvariantCulture)}."; return false; }
                if (!OpenRooms()) { rejection = "At least one room had no structurally valid sill for an unlocked door."; return false; }
                TunnelCorridors();
                if (!ConnectRegions()) { rejection = "Rooms and corridor regions could not be joined without crossing the layout mask or a room wall."; return false; }
                if (!IsConnected()) { rejection = "Rooms and corridor regions did not form one connected walkable component."; return false; }
                if (!EmplaceStairs()) { rejection = $"Only {stairs.Count.ToString(CultureInfo.InvariantCulture)} structurally valid stair ends were available for {request.StairCount.ToString(CultureInfo.InvariantCulture)} requested stairs."; return false; }
                CleanDeadEnds();
                if (!IsConnected()) { rejection = "Dead-end cleanup disconnected walkable topology."; return false; }
                List<DungeonCell> safe = BuildSafeCells();
                if (safe.Count == 0) { rejection = "No valid safe arrival cell remained after cleanup."; return false; }
                DungeonCell start = stairs.Count > 0 ? stairs[0].ArrivalCell : safe[0];
                DungeonGenerationMetadata metadata = new(
                    "donjon-logical-splitmix64", 1, request.RunSeed, request.Depth, attempt,
                    DungeonSeedSequence.FormatState(DungeonSeedSequence.ForDepth(request.RunSeed, request.Depth)),
                    DungeonSeedSequence.FormatState(topologyState));
                document = new DungeonLevelDocument(metadata, BuildRows(), rooms, doors, stairs, start, safe,
                    Array.Empty<DungeonObjectPlacement>(), Array.Empty<DungeonEncounterPlan>());
                return true;
            }

            private void InitializeMask()
            {
                int centerX = request.Width / 2, centerZ = request.Height / 2;
                for (int z = 0; z < request.Height; z++)
                for (int x = 0; x < request.Width; x++)
                {
                    bool boundary = x == 0 || z == 0 || x == request.Width - 1 || z == request.Height - 1;
                    bool allowed = request.Layout switch
                    {
                        DungeonLayout.Box => !(Math.Abs(x - centerX) < request.Width / 6 && Math.Abs(z - centerZ) < request.Height / 6),
                        DungeonLayout.Cross => Math.Abs(x - centerX) <= request.Width / 6 || Math.Abs(z - centerZ) <= request.Height / 6,
                        DungeonLayout.Round => Squared(x - centerX) * Squared(request.Height - 1) + Squared(z - centerZ) * Squared(request.Width - 1) <= Squared(request.Width - 1) * Squared(request.Height - 1) / 4,
                        _ => false
                    };
                    cells[x, z] = boundary || !allowed ? CellKind.Masked : CellKind.Empty;
                }
            }

            private static long Squared(int value) => (long)value * value;

            private void EmplaceRooms()
            {
                if (request.RoomLayout == DungeonRoomLayout.Packed)
                {
                    for (int z = 1; z < request.Height - 1; z += 2)
                    for (int x = 1; x < request.Width - 1; x += 2)
                        if (random.NextPercent(70)) TryRoom(x, z);
                }
                else
                {
                    int target = Math.Max(request.MinimumRoomCount, request.Width * request.Height / (request.MaximumRoomSize * request.MaximumRoomSize));
                    for (int index = 0; index < target * 12; index++)
                        TryRoom(1 + 2 * random.NextInt((request.Width - 2) / 2), 1 + 2 * random.NextInt((request.Height - 2) / 2));
                }
            }

            private void TryRoom(int minimumX, int minimumZ)
            {
                int sizeSteps = (request.MaximumRoomSize - request.MinimumRoomSize) / 2 + 1;
                int width = request.MinimumRoomSize + 2 * random.NextInt(sizeSteps);
                int height = request.MinimumRoomSize + 2 * random.NextInt(sizeSteps);
                int maximumX = minimumX + width - 1, maximumZ = minimumZ + height - 1;
                if (maximumX >= request.Width - 1 || maximumZ >= request.Height - 1) return;
                for (int z = minimumZ - 1; z <= maximumZ + 1; z++)
                for (int x = minimumX - 1; x <= maximumX + 1; x++)
                    if (!InBounds(x, z) || cells[x, z] != CellKind.Empty) return;
                DungeonRoom room = new(rooms.Count + 1, minimumX, minimumZ, maximumX, maximumZ); rooms.Add(room);
                for (int z = minimumZ; z <= maximumZ; z++) for (int x = minimumX; x <= maximumX; x++) cells[x, z] = CellKind.Room;
            }

            private bool OpenRooms()
            {
                HashSet<DungeonCell> used = new();
                foreach (DungeonRoom room in rooms)
                {
                    List<(DungeonCell door, DungeonCell outside)> candidates = new();
                    for (int x = room.MinimumX; x <= room.MaximumX; x += 2) { AddSill(x, room.MinimumZ - 1, x, room.MinimumZ - 2); AddSill(x, room.MaximumZ + 1, x, room.MaximumZ + 2); }
                    for (int z = room.MinimumZ; z <= room.MaximumZ; z += 2) { AddSill(room.MinimumX - 1, z, room.MinimumX - 2, z); AddSill(room.MaximumX + 1, z, room.MaximumX + 2, z); }
                    if (candidates.Count == 0) return false;
                    Shuffle(candidates); int count = Math.Min(candidates.Count, 1 + random.NextInt(Math.Min(3, candidates.Count)));
                    for (int index = 0; index < count; index++)
                    {
                        var sill = candidates[index]; if (!used.Add(sill.door)) continue;
                        cells[sill.door.X, sill.door.Z] = CellKind.Door;
                        if (cells[sill.outside.X, sill.outside.Z] == CellKind.Empty) cells[sill.outside.X, sill.outside.Z] = CellKind.Corridor;
                        doors.Add(new DungeonDoor("door-" + (doors.Count + 1).ToString("D4", CultureInfo.InvariantCulture), sill.door));
                    }
                    if (!doors.Any(d => AdjacentToRoom(d.Cell, room))) return false;

                    void AddSill(int doorX, int doorZ, int outsideX, int outsideZ)
                    {
                        if (!InBounds(outsideX, outsideZ) || cells[doorX, doorZ] != CellKind.Empty) return;
                        CellKind outside = cells[outsideX, outsideZ]; if (outside == CellKind.Masked || outside == CellKind.Door) return;
                        candidates.Add((new DungeonCell(doorX, doorZ), new DungeonCell(outsideX, outsideZ)));
                    }
                }
                return true;
            }

            private bool AdjacentToRoom(DungeonCell cell, DungeonRoom room) =>
                (cell.X >= room.MinimumX && cell.X <= room.MaximumX && (cell.Z == room.MinimumZ - 1 || cell.Z == room.MaximumZ + 1)) ||
                (cell.Z >= room.MinimumZ && cell.Z <= room.MaximumZ && (cell.X == room.MinimumX - 1 || cell.X == room.MaximumX + 1));

            private void TunnelCorridors()
            {
                for (int z = 1; z < request.Height - 1; z += 2)
                for (int x = 1; x < request.Width - 1; x += 2)
                    if (cells[x, z] == CellKind.Empty || cells[x, z] == CellKind.Corridor) Carve(new DungeonCell(x, z), -1);
            }

            private void Carve(DungeonCell cell, int priorDirection)
            {
                if (cells[cell.X, cell.Z] == CellKind.Empty) cells[cell.X, cell.Z] = CellKind.Corridor;
                List<int> directions = new() { 0, 1, 2, 3 }; Shuffle(directions);
                int straightChance = request.CorridorLayout == DungeonCorridorLayout.Labyrinth ? 0 : request.CorridorLayout == DungeonCorridorLayout.Bent ? 50 : 100;
                if (priorDirection >= 0 && random.NextPercent(straightChance)) { directions.Remove(priorDirection); directions.Insert(0, priorDirection); }
                foreach (int direction in directions)
                {
                    DungeonCell delta = Directions[direction]; int middleX = cell.X + delta.X, middleZ = cell.Z + delta.Z;
                    int nextX = cell.X + 2 * delta.X, nextZ = cell.Z + 2 * delta.Z;
                    if (!InBounds(nextX, nextZ) || cells[middleX, middleZ] != CellKind.Empty || cells[nextX, nextZ] != CellKind.Empty || IsDoorFlank(middleX, middleZ) || IsDoorFlank(nextX, nextZ)) continue;
                    cells[middleX, middleZ] = CellKind.Corridor; cells[nextX, nextZ] = CellKind.Corridor;
                    Carve(new DungeonCell(nextX, nextZ), direction);
                }
            }

            private bool ConnectRegions()
            {
                for (int bridge = 0; bridge < request.Width * request.Height; bridge++)
                {
                    List<DungeonCell> walkable = WalkableCells().ToList();
                    if (walkable.Count == 0) return false;
                    HashSet<DungeonCell> connected = new(Distances(walkable[0]).Keys);
                    if (connected.Count == walkable.Count) return true;

                    Queue<DungeonCell> queue = new();
                    Dictionary<DungeonCell, DungeonCell> previous = new();
                    HashSet<DungeonCell> visited = new(connected);
                    foreach (DungeonCell origin in connected) queue.Enqueue(origin);
                    DungeonCell target = default; bool found = false;
                    while (queue.Count > 0 && !found)
                    {
                        DungeonCell current = queue.Dequeue();
                        foreach (DungeonCell direction in Directions)
                        {
                            DungeonCell next = new(current.X + direction.X, current.Z + direction.Z);
                            if (!InBounds(next.X, next.Z) || visited.Contains(next)) continue;
                            if (cells[next.X, next.Z] == CellKind.Corridor && !connected.Contains(next))
                            {
                                previous[next] = current; target = next; found = true; break;
                            }
                            if (cells[next.X, next.Z] != CellKind.Empty || IsDoorFlank(next.X, next.Z)) continue;
                            visited.Add(next); previous[next] = current; queue.Enqueue(next);
                        }
                    }
                    if (!found) return false;
                    DungeonCell cursor = target;
                    while (!connected.Contains(cursor))
                    {
                        if (cells[cursor.X, cursor.Z] == CellKind.Empty) cells[cursor.X, cursor.Z] = CellKind.Corridor;
                        cursor = previous[cursor];
                    }
                }
                return false;
            }

            private bool IsDoorFlank(int x, int z)
            {
                foreach (DungeonDoor door in doors)
                {
                    int dx = x - door.Cell.X, dz = z - door.Cell.Z;
                    if (Math.Abs(dx) + Math.Abs(dz) != 1) continue;
                    DungeonCell opposite = new(door.Cell.X - dx, door.Cell.Z - dz);
                    if (InBounds(opposite.X, opposite.Z) && cells[opposite.X, opposite.Z] == CellKind.Room)
                        return false;
                    return true;
                }
                return false;
            }

            private bool EmplaceStairs()
            {
                if (request.StairCount == 0) return true;
                List<DungeonCell> candidates = WalkableCells().Where(c => cells[c.X, c.Z] == CellKind.Corridor && OpenNeighbors(c).Count == 1).ToList();
                if (candidates.Count < request.StairCount) candidates = WalkableCells().Where(c => cells[c.X, c.Z] == CellKind.Corridor && OpenNeighbors(c).Count > 0).ToList();
                if (candidates.Count < request.StairCount) return false;
                DungeonCell first = candidates[random.NextInt(candidates.Count)]; AddStair(first, DungeonStairKind.Up);
                if (request.StairCount == 2)
                {
                    DungeonCell second = Farthest(first, candidates.Where(c => c != first)); AddStair(second, DungeonStairKind.Down);
                }
                return stairs.Count == request.StairCount;
            }

            private void AddStair(DungeonCell cell, DungeonStairKind kind)
            {
                List<DungeonCell> neighbors = OpenNeighbors(cell); if (neighbors.Count == 0) return;
                string id = kind == DungeonStairKind.Up ? "stair-up" : "stair-down";
                stairs.Add(new DungeonStair(id, kind, cell, neighbors[0]));
            }

            private DungeonCell Farthest(DungeonCell start, IEnumerable<DungeonCell> candidates)
            {
                Dictionary<DungeonCell, int> distances = Distances(start);
                return candidates.OrderByDescending(c => distances.TryGetValue(c, out int d) ? d : -1).ThenBy(c => c.Z).ThenBy(c => c.X).First();
            }

            private void CleanDeadEnds()
            {
                HashSet<DungeonCell> protectedCells = new(stairs.SelectMany(s => new[] { s.Cell, s.ArrivalCell }));
                foreach (DungeonDoor door in doors)
                    foreach (DungeonCell neighbor in OpenNeighbors(door.Cell)) protectedCells.Add(neighbor);
                bool changed;
                do
                {
                    changed = false;
                    foreach (DungeonCell cell in WalkableCells().ToArray())
                    {
                        if (cells[cell.X, cell.Z] != CellKind.Corridor || protectedCells.Contains(cell) || OpenNeighbors(cell).Count != 1 || !random.NextPercent(request.DeadEndRemovalPercent)) continue;
                        DungeonCell cursor = cell;
                        while (!protectedCells.Contains(cursor) && cells[cursor.X, cursor.Z] == CellKind.Corridor && OpenNeighbors(cursor).Count == 1)
                        {
                            DungeonCell next = OpenNeighbors(cursor)[0]; cells[cursor.X, cursor.Z] = CellKind.Empty; changed = true; cursor = next;
                        }
                    }
                } while (changed && request.DeadEndRemovalPercent == 100);
            }

            private List<DungeonCell> BuildSafeCells()
            {
                List<DungeonCell> safe = new();
                foreach (DungeonStair stair in stairs) if (!safe.Contains(stair.ArrivalCell)) safe.Add(stair.ArrivalCell);
                foreach (DungeonRoom room in rooms)
                {
                    DungeonCell center = new((room.MinimumX + room.MaximumX) / 2, (room.MinimumZ + room.MaximumZ) / 2);
                    if (IsWalkable(center) && !safe.Contains(center)) safe.Add(center);
                }
                if (safe.Count == 0) safe.AddRange(WalkableCells().Where(c => cells[c.X, c.Z] != CellKind.Door).Take(8));
                return safe;
            }

            private IReadOnlyList<string> BuildRows()
            {
                List<string> rows = new(request.Height);
                for (int z = request.Height - 1; z >= 0; z--)
                {
                    char[] row = new char[request.Width];
                    for (int x = 0; x < request.Width; x++) row[x] = cells[x, z] switch { CellKind.Masked => ' ', CellKind.Room or CellKind.Corridor => '.', CellKind.Door => 'D', _ => '#' };
                    rows.Add(new string(row));
                }
                return rows;
            }

            private bool IsConnected()
            {
                List<DungeonCell> walkable = WalkableCells().ToList();
                return walkable.Count > 0 && Distances(walkable[0]).Count == walkable.Count;
            }

            private Dictionary<DungeonCell, int> Distances(DungeonCell start)
            {
                Dictionary<DungeonCell, int> result = new() { [start] = 0 }; Queue<DungeonCell> queue = new(); queue.Enqueue(start);
                while (queue.Count > 0) { DungeonCell current = queue.Dequeue(); foreach (DungeonCell next in OpenNeighbors(current)) if (!result.ContainsKey(next)) { result[next] = result[current] + 1; queue.Enqueue(next); } }
                return result;
            }

            private IEnumerable<DungeonCell> WalkableCells() { for (int z = 0; z < request.Height; z++) for (int x = 0; x < request.Width; x++) { DungeonCell c = new(x, z); if (IsWalkable(c)) yield return c; } }
            private List<DungeonCell> OpenNeighbors(DungeonCell cell) { List<DungeonCell> result = new(); foreach (DungeonCell d in Directions) { DungeonCell next = new(cell.X + d.X, cell.Z + d.Z); if (InBounds(next.X, next.Z) && IsWalkable(next)) result.Add(next); } return result; }
            private bool IsWalkable(DungeonCell cell) => cells[cell.X, cell.Z] == CellKind.Room || cells[cell.X, cell.Z] == CellKind.Corridor || cells[cell.X, cell.Z] == CellKind.Door;
            private bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < request.Width && z < request.Height;
            private void Shuffle<T>(IList<T> values) { for (int i = values.Count - 1; i > 0; i--) { int j = random.NextInt(i + 1); (values[i], values[j]) = (values[j], values[i]); } }
        }
    }
}
