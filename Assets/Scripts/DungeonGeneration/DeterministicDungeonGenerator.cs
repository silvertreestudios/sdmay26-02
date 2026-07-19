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
        internal const string AlgorithmId = "donjon-logical-system-random";
        internal const int MinimumDimension = 15;
        internal const int MaximumDimension = 101;

        /// <summary>Maximum deterministic topology attempts, including the initial attempt.</summary>
        public const int MaximumAttempts = 32;

        // Donjon sorts direction names before shuffling them: east, north, south, west.
        private static readonly DungeonCell[] Directions =
        {
            new(1, 0),
            new(0, 1),
            new(0, -1),
            new(-1, 0),
        };

        /// <inheritdoc/>
        public DungeonGenerationResult Generate(DungeonGenerationRequest request)
        {
            IReadOnlyList<DungeonGenerationDiagnostic> invalid = ValidateRequest(request);
            if (invalid.Count > 0)
                return new DungeonGenerationResult(null, invalid);

            DungeonGenerationDiagnostic last = null;
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                int topologySeed = DungeonSeedSequence.ForTopologyAttempt(
                    request.RunSeed,
                    request.Depth,
                    attempt
                );
                Attempt state = new(request, attempt, topologySeed);
                if (state.TryGenerate(out DungeonLevelDocument document, out string rejection))
                    return new DungeonGenerationResult(
                        document,
                        Array.Empty<DungeonGenerationDiagnostic>()
                    );
                last = new DungeonGenerationDiagnostic(
                    DungeonGenerationDiagnosticCode.TopologyRejected,
                    "topology",
                    rejection,
                    attempt
                );
            }

            return new DungeonGenerationResult(
                null,
                new[]
                {
                    last,
                    new DungeonGenerationDiagnostic(
                        DungeonGenerationDiagnosticCode.RetryLimitExhausted,
                        "topology",
                        "All 32 deterministic topology attempts were rejected. Reduce minimum room/stair requirements, enlarge the map, or change the layout mask."
                    ),
                }
            );
        }

        /// <summary>
        /// Returns whether metadata uses the exact algorithm contract owned by this generator.
        /// Parser invariants that depend on implementation details use this predicate so authored
        /// documents can retain their own topology semantics without schema-version dispatch.
        /// </summary>
        /// <param name="metadata">The parsed generation metadata, or absence after a schema failure.</param>
        /// <returns><see langword="true"/> only for the current Donjon generator.</returns>
        internal static bool OwnsContract(DungeonGenerationMetadata metadata) =>
            metadata != null
            && string.Equals(metadata.Algorithm, AlgorithmId, StringComparison.Ordinal);

        /// <summary>Returns whether one map dimension is supported by the owned generator contract.</summary>
        internal static bool IsSupportedDimension(int dimension) =>
            dimension >= MinimumDimension && dimension <= MaximumDimension && (dimension & 1) == 1;

        private static IReadOnlyList<DungeonGenerationDiagnostic> ValidateRequest(
            DungeonGenerationRequest request
        )
        {
            List<DungeonGenerationDiagnostic> errors = new();
            void Check(bool condition, string field, string message)
            {
                if (!condition)
                {
                    errors.Add(
                        new DungeonGenerationDiagnostic(
                            DungeonGenerationDiagnosticCode.InvalidRequest,
                            field,
                            message
                        )
                    );
                }
            }
            if (request == null)
            {
                errors.Add(
                    new DungeonGenerationDiagnostic(
                        DungeonGenerationDiagnosticCode.InvalidRequest,
                        "request",
                        "A generation request is required."
                    )
                );
                return errors;
            }

            Check(request.Depth >= 0, nameof(request.Depth), "Depth must be zero or greater.");
            Check(
                IsSupportedDimension(request.Width),
                nameof(request.Width),
                "Width must be an odd integer from 15 through 101."
            );
            Check(
                IsSupportedDimension(request.Height),
                nameof(request.Height),
                "Height must be an odd integer from 15 through 101."
            );
            Check(
                Enum.IsDefined(typeof(DungeonLayout), request.Layout),
                nameof(request.Layout),
                "Layout is not supported."
            );
            Check(
                Enum.IsDefined(typeof(DungeonRoomLayout), request.RoomLayout),
                nameof(request.RoomLayout),
                "Room layout is not supported."
            );
            Check(
                Enum.IsDefined(typeof(DungeonCorridorLayout), request.CorridorLayout),
                nameof(request.CorridorLayout),
                "Corridor layout is not supported."
            );
            Check(
                request.MinimumRoomSize >= 3 && (request.MinimumRoomSize & 1) == 1,
                nameof(request.MinimumRoomSize),
                "Minimum room size must be an odd integer of at least 3."
            );
            Check(
                request.MaximumRoomSize >= request.MinimumRoomSize
                    && (request.MaximumRoomSize & 1) == 1,
                nameof(request.MaximumRoomSize),
                "Maximum room size must be odd and no smaller than the minimum."
            );
            Check(
                request.MaximumRoomSize <= Math.Min(request.Width, request.Height) - 4,
                nameof(request.MaximumRoomSize),
                "Maximum room size must leave a two-cell margin inside the map boundary."
            );
            Check(
                request.MinimumRoomCount >= 0 && request.MinimumRoomCount <= 999,
                nameof(request.MinimumRoomCount),
                "Minimum room count must be from 0 through 999."
            );
            Check(
                request.StairCount >= 0 && request.StairCount <= 2,
                nameof(request.StairCount),
                "Stair count must be zero, one, or two."
            );
            Check(
                request.DeadEndRemovalPercent >= 0 && request.DeadEndRemovalPercent <= 100,
                nameof(request.DeadEndRemovalPercent),
                "Dead-end removal must be a percentage from 0 through 100."
            );
            return errors;
        }

        /// <summary>
        /// Finds the first shortest connector from a multi-cell region using coordinate-ordered
        /// origins and the generator's fixed direction order. Hash-based collections are used only
        /// for membership so their enumeration order cannot affect the selected topology.
        /// </summary>
        /// <param name="width">The exclusive horizontal map bound.</param>
        /// <param name="height">The exclusive vertical map bound.</param>
        /// <param name="connectedCells">The existing connected region; enumeration order is ignored.</param>
        /// <param name="isTarget">Returns whether a candidate reaches a different acceptable region.</param>
        /// <param name="canTraverse">Returns whether a candidate can be carved and searched.</param>
        /// <param name="path">The origin-exclusive, target-inclusive path in carving order, or an empty list when none is reachable.</param>
        /// <returns><see langword="true"/> when a connector reaches an acceptable target.</returns>
        internal static bool TryFindConnectorPath(
            int width,
            int height,
            IEnumerable<DungeonCell> connectedCells,
            Func<DungeonCell, bool> isTarget,
            Func<DungeonCell, bool> canTraverse,
            out IReadOnlyList<DungeonCell> path
        )
        {
            if (connectedCells == null)
                throw new ArgumentNullException(nameof(connectedCells));
            if (isTarget == null)
                throw new ArgumentNullException(nameof(isTarget));
            if (canTraverse == null)
                throw new ArgumentNullException(nameof(canTraverse));

            HashSet<DungeonCell> connected = new(connectedCells);
            Queue<DungeonCell> queue = new();
            Dictionary<DungeonCell, DungeonCell> previous = new();
            HashSet<DungeonCell> visited = new(connected);
            foreach (DungeonCell origin in connected.OrderBy(cell => cell.Z).ThenBy(cell => cell.X))
            {
                queue.Enqueue(origin);
            }

            while (queue.Count > 0)
            {
                DungeonCell current = queue.Dequeue();
                foreach (DungeonCell direction in Directions)
                {
                    DungeonCell next = new(current.X + direction.X, current.Z + direction.Z);
                    if (
                        next.X < 0
                        || next.Z < 0
                        || next.X >= width
                        || next.Z >= height
                        || visited.Contains(next)
                    )
                    {
                        continue;
                    }

                    previous[next] = current;
                    if (isTarget(next))
                    {
                        List<DungeonCell> connector = new();
                        DungeonCell cursor = next;
                        while (!connected.Contains(cursor))
                        {
                            connector.Add(cursor);
                            cursor = previous[cursor];
                        }

                        connector.Reverse();
                        path = Array.AsReadOnly(connector.ToArray());
                        return true;
                    }

                    if (!canTraverse(next))
                    {
                        previous.Remove(next);
                        continue;
                    }

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            path = Array.Empty<DungeonCell>();
            return false;
        }

        /// <summary>
        /// Selects the generated arrival default without coupling it to Donjon's down-before-up
        /// stair record order. Two-stair levels use the Up arrival. A one-stair level has only a
        /// Down stair, so it uses the first ordered safe cell outside that stair's endpoint and
        /// arrival when possible. Zero-stair levels use the first safe cell. If every safe cell is
        /// associated with a Down stair, the first safe cell is the deterministic last resort.
        /// </summary>
        /// <param name="stairs">The generated stairs in stable traversal order.</param>
        /// <param name="safeCells">The non-empty ordered safe-cell collection.</param>
        /// <returns>The deterministic default player start.</returns>
        internal static DungeonCell SelectStartCell(
            IReadOnlyList<DungeonStair> stairs,
            IReadOnlyList<DungeonCell> safeCells
        )
        {
            foreach (DungeonStair stair in stairs)
            {
                if (stair.Kind == DungeonStairKind.Up)
                    return stair.ArrivalCell;
            }

            HashSet<DungeonCell> downCells = new(
                stairs
                    .Where(stair => stair.Kind == DungeonStairKind.Down)
                    .SelectMany(stair => new[] { stair.Cell, stair.ArrivalCell })
            );
            foreach (DungeonCell safeCell in safeCells)
            {
                if (!downCells.Contains(safeCell))
                    return safeCell;
            }

            return safeCells[0];
        }

        private enum CellKind : byte
        {
            Empty,
            Masked,
            Room,
            Corridor,
            Door,
        }

        private sealed class Attempt
        {
            private readonly DungeonGenerationRequest request;
            private readonly int attempt;
            private readonly IDungeonRandom random;
            private readonly CellKind[,] cells;

            // Donjon stores perimeter and corridor as independent bits. Keep that overlap explicitly so a
            // door tunneled into the maze cannot be revisited recursively merely because it remains a door.
            private readonly bool[,] perimeter;
            private readonly bool[,] tunneled;
            private readonly List<DungeonRoom> rooms = new();
            private readonly List<DungeonDoor> doors = new();
            private readonly List<DungeonStair> stairs = new();

            internal Attempt(DungeonGenerationRequest request, int attempt, int topologySeed)
            {
                this.request = request;
                this.attempt = attempt;
                random = new SystemDungeonRandom(topologySeed);
                cells = new CellKind[request.Width, request.Height];
                perimeter = new bool[request.Width, request.Height];
                tunneled = new bool[request.Width, request.Height];
            }

            internal bool TryGenerate(out DungeonLevelDocument document, out string rejection)
            {
                document = null;
                rejection = null;
                InitializeMask();
                EmplaceRooms();
                if (rooms.Count < request.MinimumRoomCount)
                {
                    rejection =
                        $"Placed {rooms.Count.ToString(CultureInfo.InvariantCulture)} rooms, fewer than required {request.MinimumRoomCount.ToString(CultureInfo.InvariantCulture)}.";
                    return false;
                }

                if (!OpenRooms())
                {
                    rejection =
                        "At least one room had no structurally valid sill for an unlocked door.";
                    return false;
                }

                TunnelCorridors();
                if (!ConnectRegions())
                {
                    rejection =
                        "Rooms and corridor regions could not be joined without crossing the layout mask or a room wall.";
                    return false;
                }

                if (!IsConnected())
                {
                    rejection =
                        "Rooms and corridor regions did not form one connected walkable component.";
                    return false;
                }

                if (!EmplaceStairs())
                {
                    rejection =
                        $"Only {stairs.Count.ToString(CultureInfo.InvariantCulture)} structurally valid stair ends were available for {request.StairCount.ToString(CultureInfo.InvariantCulture)} requested stairs.";
                    return false;
                }

                CleanDeadEnds();
                if (!IsConnected())
                {
                    rejection = "Dead-end cleanup disconnected walkable topology.";
                    return false;
                }

                IReadOnlyList<string> rows = BuildRows();
                if (
                    stairs.Any(stair =>
                        !DungeonTopologyValidator.MatchesStairEnd(
                            rows,
                            rooms,
                            stair.Cell,
                            stair.ArrivalCell
                        )
                    )
                )
                {
                    rejection = "Dead-end cleanup invalidated a generated stair runway.";
                    return false;
                }

                if (!DungeonTopologyValidator.HasProducibleLayoutMask(rows))
                {
                    rejection = "Generated rows did not retain one supported initialization mask.";
                    return false;
                }

                if (!DungeonTopologyValidator.HasValidRoomBoundaryCrossings(rows, rooms, doors))
                {
                    rejection =
                        "A room-boundary crossing was not represented by exactly one stable door.";
                    return false;
                }

                if (!DungeonTopologyValidator.HasValidDoors(rows, rooms, doors))
                {
                    rejection =
                        "A generated door did not retain two opposite walkable neighbors or a unique stable record.";
                    return false;
                }

                if (!DungeonTopologyValidator.HasProducibleDoorRecords(doors))
                {
                    rejection =
                        "Generated door records did not retain stable IDs, ordering, or sill parity.";
                    return false;
                }

                IReadOnlyList<DungeonCell> safe = DungeonTopologyValidator.BuildSafeCells(
                    rows,
                    rooms,
                    stairs
                );
                if (safe.Count == 0)
                {
                    rejection = "No valid safe arrival cell remained after cleanup.";
                    return false;
                }

                DungeonCell start = SelectStartCell(stairs, safe);
                DungeonGenerationMetadata metadata = new(
                    AlgorithmId,
                    request.RunSeed,
                    request.Depth,
                    attempt
                );
                document = new DungeonLevelDocument(
                    metadata,
                    rows,
                    rooms,
                    doors,
                    stairs,
                    start,
                    safe,
                    Array.Empty<DungeonObjectPlacement>(),
                    Array.Empty<DungeonEncounterPlan>()
                );
                return true;
            }

            private void InitializeMask()
            {
                for (int z = 0; z < request.Height; z++)
                for (int x = 0; x < request.Width; x++)
                {
                    cells[x, z] = DungeonTopologyValidator.IsMaskedByLayout(
                        request.Layout,
                        request.Width,
                        request.Height,
                        x,
                        z
                    )
                        ? CellKind.Masked
                        : CellKind.Empty;
                }
            }

            private void EmplaceRooms()
            {
                int coarseWidth = (request.Width - 1) / 2;
                int coarseHeight = (request.Height - 1) / 2;
                if (request.RoomLayout == DungeonRoomLayout.Packed)
                {
                    for (int coarseZ = 0; coarseZ < coarseHeight; coarseZ++)
                    for (int coarseX = 0; coarseX < coarseWidth; coarseX++)
                    {
                        int anchorX = coarseX * 2 + 1;
                        int anchorZ = coarseZ * 2 + 1;
                        if (cells[anchorX, anchorZ] == CellKind.Room)
                            continue;
                        if ((coarseX == 0 || coarseZ == 0) && random.NextInt(2) != 0)
                            continue;
                        TryRoom(coarseX, coarseZ, true);
                    }
                }
                else
                {
                    int attempts =
                        (request.Width - 1)
                        * (request.Height - 1)
                        / (request.MaximumRoomSize * request.MaximumRoomSize);
                    for (int index = 0; index < attempts; index++)
                        TryRoom(0, 0, false);
                }
            }

            private void TryRoom(int coarseX, int coarseZ, bool fixedPosition)
            {
                int coarseWidth = (request.Width - 1) / 2;
                int coarseHeight = (request.Height - 1) / 2;
                int roomBase = (request.MinimumRoomSize + 1) / 2;
                int roomRadix = (request.MaximumRoomSize - request.MinimumRoomSize) / 2 + 1;
                int heightSteps;
                int widthSteps;
                if (fixedPosition)
                {
                    int availableHeight = Math.Max(0, coarseHeight - roomBase - coarseZ);
                    int availableWidth = Math.Max(0, coarseWidth - roomBase - coarseX);
                    // Counts include zero extra size and the largest size that still fits.
                    int heightRange = Math.Min(availableHeight + 1, roomRadix);
                    int widthRange = Math.Min(availableWidth + 1, roomRadix);
                    heightSteps = roomBase + random.NextInt(heightRange);
                    widthSteps = roomBase + random.NextInt(widthRange);
                }
                else
                {
                    heightSteps = roomBase + random.NextInt(roomRadix);
                    widthSteps = roomBase + random.NextInt(roomRadix);
                    // Anchor counts include the last position and the exact-fit position at zero.
                    int verticalPositionCount = coarseHeight - heightSteps + 1;
                    int horizontalPositionCount = coarseWidth - widthSteps + 1;
                    if (verticalPositionCount <= 0 || horizontalPositionCount <= 0)
                        return;
                    coarseZ = random.NextInt(verticalPositionCount);
                    coarseX = random.NextInt(horizontalPositionCount);
                }

                int minimumX = coarseX * 2 + 1;
                int minimumZ = coarseZ * 2 + 1;
                int width = widthSteps * 2 - 1;
                int height = heightSteps * 2 - 1;
                int maximumX = minimumX + width - 1,
                    maximumZ = minimumZ + height - 1;
                if (
                    minimumX < 1
                    || minimumZ < 1
                    || maximumX > request.Width - 2
                    || maximumZ > request.Height - 2
                )
                    return;
                for (int z = minimumZ; z <= maximumZ; z++)
                for (int x = minimumX; x <= maximumX; x++)
                    if (cells[x, z] == CellKind.Masked || cells[x, z] == CellKind.Room)
                        return;

                DungeonRoom room = new(rooms.Count + 1, minimumX, minimumZ, maximumX, maximumZ);
                rooms.Add(room);
                for (int z = minimumZ; z <= maximumZ; z++)
                for (int x = minimumX; x <= maximumX; x++)
                {
                    cells[x, z] = CellKind.Room;
                    perimeter[x, z] = false;
                }

                for (int z = minimumZ - 1; z <= maximumZ + 1; z++)
                {
                    MarkPerimeter(minimumX - 1, z);
                    MarkPerimeter(maximumX + 1, z);
                }
                for (int x = minimumX - 1; x <= maximumX + 1; x++)
                {
                    MarkPerimeter(x, minimumZ - 1);
                    MarkPerimeter(x, maximumZ + 1);
                }

                void MarkPerimeter(int x, int z)
                {
                    if (
                        InBounds(x, z)
                        && cells[x, z] != CellKind.Room
                        && cells[x, z] != CellKind.Door
                    )
                        perimeter[x, z] = true;
                }
            }

            private bool OpenRooms()
            {
                HashSet<DungeonCell> used = new();
                HashSet<string> connectedRoomPairs = new(StringComparer.Ordinal);
                foreach (DungeonRoom room in rooms)
                {
                    List<(DungeonCell door, int outsideRoomId)> candidates = new();
                    for (int x = room.MinimumX; x <= room.MaximumX; x += 2)
                    {
                        AddSill(x, room.MinimumZ, 0, -1);
                        AddSill(x, room.MaximumZ, 0, 1);
                    }
                    for (int z = room.MinimumZ; z <= room.MaximumZ; z += 2)
                    {
                        AddSill(room.MinimumX, z, -1, 0);
                        AddSill(room.MaximumX, z, 1, 0);
                    }

                    Shuffle(candidates);
                    int roomHeight = (room.MaximumZ - room.MinimumZ) / 2 + 1;
                    int roomWidth = (room.MaximumX - room.MinimumX) / 2 + 1;
                    int openingBase = (int)Math.Sqrt(roomHeight * roomWidth);
                    int allocatedOpenings = openingBase + random.NextInt(openingBase);
                    int opened = 0;
                    while (opened < allocatedOpenings && candidates.Count > 0)
                    {
                        int candidateIndex = random.NextInt(candidates.Count);
                        var candidate = candidates[candidateIndex];
                        candidates.RemoveAt(candidateIndex);
                        if (
                            used.Contains(candidate.door)
                            || cells[candidate.door.X, candidate.door.Z] == CellKind.Door
                        )
                            continue;

                        if (candidate.outsideRoomId > 0)
                        {
                            int first = Math.Min(room.Id, candidate.outsideRoomId);
                            int second = Math.Max(room.Id, candidate.outsideRoomId);
                            string pair =
                                first.ToString(CultureInfo.InvariantCulture)
                                + ":"
                                + second.ToString(CultureInfo.InvariantCulture);
                            if (!connectedRoomPairs.Add(pair))
                                continue;
                        }

                        used.Add(candidate.door);
                        cells[candidate.door.X, candidate.door.Z] = CellKind.Door;
                        perimeter[candidate.door.X, candidate.door.Z] = false;
                        doors.Add(
                            new DungeonDoor(
                                "door-"
                                    + (doors.Count + 1).ToString(
                                        "D4",
                                        CultureInfo.InvariantCulture
                                    ),
                                candidate.door
                            )
                        );
                        opened++;
                    }

                    if (!doors.Any(door => AdjacentToRoom(door.Cell, room)))
                        return false;

                    void AddSill(int sillX, int sillZ, int deltaX, int deltaZ)
                    {
                        int doorX = sillX + deltaX;
                        int doorZ = sillZ + deltaZ;
                        int outsideX = doorX + deltaX;
                        int outsideZ = doorZ + deltaZ;
                        if (!InBounds(outsideX, outsideZ) || !perimeter[doorX, doorZ])
                            return;
                        if (
                            cells[doorX, doorZ] == CellKind.Masked
                            || cells[doorX, doorZ] == CellKind.Door
                        )
                            return;
                        if (cells[outsideX, outsideZ] == CellKind.Masked)
                            return;
                        int outsideRoomId = RoomIdAt(outsideX, outsideZ);
                        if (outsideRoomId == room.Id)
                            return;
                        candidates.Add((new DungeonCell(doorX, doorZ), outsideRoomId));
                    }
                }
                return true;
            }

            private int RoomIdAt(int x, int z)
            {
                foreach (DungeonRoom room in rooms)
                    if (
                        x >= room.MinimumX
                        && x <= room.MaximumX
                        && z >= room.MinimumZ
                        && z <= room.MaximumZ
                    )
                        return room.Id;
                return 0;
            }

            private bool AdjacentToRoom(DungeonCell cell, DungeonRoom room) =>
                (
                    cell.X >= room.MinimumX
                    && cell.X <= room.MaximumX
                    && (cell.Z == room.MinimumZ - 1 || cell.Z == room.MaximumZ + 1)
                )
                || (
                    cell.Z >= room.MinimumZ
                    && cell.Z <= room.MaximumZ
                    && (cell.X == room.MinimumX - 1 || cell.X == room.MaximumX + 1)
                );

            private void TunnelCorridors()
            {
                for (int z = 3; z < request.Height - 1; z += 2)
                for (int x = 3; x < request.Width - 1; x += 2)
                    if (cells[x, z] != CellKind.Corridor)
                        Tunnel(new DungeonCell(x, z), -1);
            }

            private void Tunnel(DungeonCell cell, int priorDirection)
            {
                List<int> directions = new() { 0, 1, 2, 3 };
                Shuffle(directions);
                int straightChance =
                    request.CorridorLayout == DungeonCorridorLayout.Labyrinth ? 0
                    : request.CorridorLayout == DungeonCorridorLayout.Bent ? 50
                    : 100;
                if (priorDirection >= 0 && random.NextPercent(straightChance))
                    directions.Insert(0, priorDirection);
                foreach (int direction in directions)
                {
                    if (OpenTunnel(cell, direction, out DungeonCell next))
                        Tunnel(next, direction);
                }
            }

            private bool OpenTunnel(DungeonCell cell, int direction, out DungeonCell next)
            {
                DungeonCell delta = Directions[direction];
                DungeonCell middle = new(cell.X + delta.X, cell.Z + delta.Z);
                next = new DungeonCell(cell.X + 2 * delta.X, cell.Z + 2 * delta.Z);
                if (
                    !InBounds(next.X, next.Z)
                    || !TunnelCellAvailable(middle)
                    || !TunnelCellAvailable(next)
                )
                    return false;

                for (int step = 0; step <= 2; step++)
                {
                    int x = cell.X + delta.X * step;
                    int z = cell.Z + delta.Z * step;
                    tunneled[x, z] = true;
                    if (cells[x, z] == CellKind.Empty)
                        cells[x, z] = CellKind.Corridor;
                }
                return true;
            }

            private bool TunnelCellAvailable(DungeonCell cell) =>
                cells[cell.X, cell.Z] != CellKind.Masked
                && cells[cell.X, cell.Z] != CellKind.Corridor
                && !tunneled[cell.X, cell.Z]
                && !perimeter[cell.X, cell.Z];

            private bool ConnectRegions()
            {
                for (int bridge = 0; bridge < request.Width * request.Height; bridge++)
                {
                    List<DungeonCell> walkable = WalkableCells().ToList();
                    if (walkable.Count == 0)
                        return false;
                    HashSet<DungeonCell> connected = new(Distances(walkable[0]).Keys);
                    if (connected.Count == walkable.Count)
                        return true;

                    bool found = TryFindConnectorPath(
                        request.Width,
                        request.Height,
                        connected,
                        cell =>
                            IsWalkable(cell)
                            && !connected.Contains(cell)
                            && cells[cell.X, cell.Z] != CellKind.Room,
                        CanCarveConnector,
                        out IReadOnlyList<DungeonCell> connector
                    );
                    if (!found)
                        return false;
                    foreach (DungeonCell cell in connector)
                    {
                        if (cells[cell.X, cell.Z] == CellKind.Empty)
                            cells[cell.X, cell.Z] = CellKind.Corridor;
                    }
                }
                return false;
            }

            private bool CanCarveConnector(DungeonCell cell)
            {
                if (cells[cell.X, cell.Z] != CellKind.Empty || perimeter[cell.X, cell.Z])
                    return false;
                foreach (DungeonCell direction in Directions)
                {
                    int x = cell.X + direction.X;
                    int z = cell.Z + direction.Z;
                    if (InBounds(x, z) && cells[x, z] == CellKind.Room)
                        return false;
                }
                return true;
            }

            private bool EmplaceStairs()
            {
                if (request.StairCount == 0)
                    return true;
                List<(DungeonCell cell, DungeonCell arrival)> candidates = new();
                for (int z = 1; z < request.Height - 1; z += 2)
                for (int x = 1; x < request.Width - 1; x += 2)
                {
                    DungeonCell cell = new(x, z);
                    if (cells[x, z] != CellKind.Corridor)
                        continue;
                    foreach (DungeonCell arrivalDirection in Directions)
                    {
                        if (!MatchesStairEnd(cell, arrivalDirection))
                            continue;
                        candidates.Add(
                            (cell, new DungeonCell(x + arrivalDirection.X, z + arrivalDirection.Z))
                        );
                        break;
                    }
                }
                if (candidates.Count < request.StairCount)
                    return false;
                for (int index = 0; index < request.StairCount; index++)
                {
                    int candidateIndex = random.NextInt(candidates.Count);
                    var candidate = candidates[candidateIndex];
                    candidates.RemoveAt(candidateIndex);
                    DungeonStairKind kind =
                        index == 0 ? DungeonStairKind.Down : DungeonStairKind.Up;
                    string id = kind == DungeonStairKind.Down ? "stair-down" : "stair-up";
                    stairs.Add(new DungeonStair(id, kind, candidate.cell, candidate.arrival));
                }
                return stairs.Count == request.StairCount;
            }

            private bool MatchesStairEnd(DungeonCell cell, DungeonCell arrivalDirection)
            {
                DungeonCell arrival = new(cell.X + arrivalDirection.X, cell.Z + arrivalDirection.Z);
                return DungeonTopologyValidator.MatchesStairEnd(
                    request.Width,
                    request.Height,
                    cell,
                    arrival,
                    candidate => cells[candidate.X, candidate.Z] == CellKind.Corridor,
                    IsWalkable
                );
            }

            private void CleanDeadEnds()
            {
                if (request.DeadEndRemovalPercent == 0)
                    return;
                HashSet<DungeonCell> protectedCells = new();
                foreach (DungeonStair stair in stairs)
                {
                    // MatchesStairEnd requires this complete three-cell runway after cleanup.
                    DungeonCell direction = new(
                        stair.ArrivalCell.X - stair.Cell.X,
                        stair.ArrivalCell.Z - stair.Cell.Z
                    );
                    protectedCells.Add(stair.Cell);
                    protectedCells.Add(stair.ArrivalCell);
                    protectedCells.Add(
                        new DungeonCell(
                            stair.ArrivalCell.X + direction.X,
                            stair.ArrivalCell.Z + direction.Z
                        )
                    );
                }
                foreach (DungeonDoor door in doors)
                {
                    protectedCells.Add(door.Cell);
                    foreach (DungeonCell neighbor in OpenNeighbors(door.Cell))
                        protectedCells.Add(neighbor);
                }
                for (int z = 1; z < request.Height - 1; z += 2)
                for (int x = 1; x < request.Width - 1; x += 2)
                {
                    DungeonCell cell = new(x, z);
                    if (
                        cells[x, z] == CellKind.Corridor
                        && !protectedCells.Contains(cell)
                        && random.NextPercent(request.DeadEndRemovalPercent)
                    )
                        CollapseDeadEnd(cell, protectedCells);
                }
            }

            private void CollapseDeadEnd(DungeonCell cell, HashSet<DungeonCell> protectedCells)
            {
                if (
                    !InBounds(cell.X, cell.Z)
                    || cells[cell.X, cell.Z] != CellKind.Corridor
                    || protectedCells.Contains(cell)
                )
                    return;
                foreach (DungeonCell recurseDirection in Directions)
                {
                    DungeonCell perpendicular = new(-recurseDirection.Z, recurseDirection.X);
                    DungeonCell opposite = new(-recurseDirection.X, -recurseDirection.Z);
                    DungeonCell[] walled =
                    {
                        perpendicular,
                        new DungeonCell(-perpendicular.X, -perpendicular.Z),
                        opposite,
                        new DungeonCell(opposite.X + perpendicular.X, opposite.Z + perpendicular.Z),
                        new DungeonCell(opposite.X - perpendicular.X, opposite.Z - perpendicular.Z),
                    };
                    if (walled.Any(offset => IsOpenOffset(cell, offset)))
                        continue;
                    cells[cell.X, cell.Z] = CellKind.Empty;
                    CollapseDeadEnd(
                        new DungeonCell(cell.X + recurseDirection.X, cell.Z + recurseDirection.Z),
                        protectedCells
                    );
                    return;
                }
            }

            private bool IsOpenOffset(DungeonCell cell, DungeonCell offset)
            {
                int x = cell.X + offset.X;
                int z = cell.Z + offset.Z;
                return InBounds(x, z) && IsWalkable(new DungeonCell(x, z));
            }

            private IReadOnlyList<string> BuildRows()
            {
                List<string> rows = new(request.Height);
                for (int z = request.Height - 1; z >= 0; z--)
                {
                    char[] row = new char[request.Width];
                    for (int x = 0; x < request.Width; x++)
                    {
                        row[x] = cells[x, z] switch
                        {
                            CellKind.Masked => ' ',
                            CellKind.Room or CellKind.Corridor => '.',
                            CellKind.Door => 'D',
                            _ => '#',
                        };
                    }

                    rows.Add(new string(row));
                }
                return rows;
            }

            private bool IsConnected() =>
                DungeonTopologyValidator.HasSingleWalkableRegion(BuildRows());

            private Dictionary<DungeonCell, int> Distances(DungeonCell start)
            {
                Dictionary<DungeonCell, int> result = new() { [start] = 0 };
                Queue<DungeonCell> queue = new();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    DungeonCell current = queue.Dequeue();
                    foreach (DungeonCell next in OpenNeighbors(current))
                    {
                        if (!result.ContainsKey(next))
                        {
                            result[next] = result[current] + 1;
                            queue.Enqueue(next);
                        }
                    }
                }

                return result;
            }

            private IEnumerable<DungeonCell> WalkableCells()
            {
                for (int z = 0; z < request.Height; z++)
                {
                    for (int x = 0; x < request.Width; x++)
                    {
                        DungeonCell cell = new(x, z);
                        if (IsWalkable(cell))
                        {
                            yield return cell;
                        }
                    }
                }
            }

            private List<DungeonCell> OpenNeighbors(DungeonCell cell)
            {
                List<DungeonCell> result = new();
                foreach (DungeonCell direction in Directions)
                {
                    DungeonCell next = new(cell.X + direction.X, cell.Z + direction.Z);
                    if (InBounds(next.X, next.Z) && IsWalkable(next))
                    {
                        result.Add(next);
                    }
                }

                return result;
            }

            private bool IsWalkable(DungeonCell cell) =>
                cells[cell.X, cell.Z] == CellKind.Room
                || cells[cell.X, cell.Z] == CellKind.Corridor
                || cells[cell.X, cell.Z] == CellKind.Door;

            private bool InBounds(int x, int z) =>
                x >= 0 && z >= 0 && x < request.Width && z < request.Height;

            private void Shuffle<T>(IList<T> values)
            {
                for (int index = values.Count - 1; index > 0; index--)
                {
                    int swapIndex = random.NextInt(index + 1);
                    (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
                }
            }
        }
    }
}
