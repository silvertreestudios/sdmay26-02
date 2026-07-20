using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Adds deterministic, nonblocking KayKit wall decorations to generated room data.
    /// </summary>
    internal static class DungeonDecorationPlanner
    {
        internal const string BannerAssetId = "dungeon/assets/fbx(unity)/banner_red";
        internal const string TorchAssetId = "dungeon/assets/fbx(unity)/torch_mounted";
        internal const int TorchSpacingCells = 8;

        // A three-cell minimum makes every accepted torch strictly more than two grid units
        // from a corner or intersection along the wall on which it is mounted.
        internal const int MinimumTorchCornerDistanceCells = 3;
        private const int BannerAttemptsPerRoom = 1;
        private const int BannerPlacementChancePercent = 35;

        internal static IReadOnlyList<DungeonObjectPlacement> CreatePlacements(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IReadOnlyCollection<DungeonCell> reservedCells,
            IDungeonRandom random
        )
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (rooms == null)
                throw new ArgumentNullException(nameof(rooms));
            if (reservedCells == null)
                throw new ArgumentNullException(nameof(reservedCells));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            List<DungeonObjectPlacement> placements = new();
            HashSet<WallFace> occupiedFaces = new();
            HashSet<DungeonCell> reserved = new(reservedCells);
            int torchIndex = 0;
            foreach (IReadOnlyList<WallFace> run in FindWallRuns(rows, reserved))
            {
                List<int> eligibleFaceIndices = Enumerable
                    .Range(0, run.Count)
                    .Where(index => HasTorchCornerClearance(rows, run[index]))
                    .ToList();
                if (eligibleFaceIndices.Count == 0)
                    continue;

                int torchCount = Math.Min(
                    eligibleFaceIndices.Count,
                    Math.Max(1, (run.Count + TorchSpacingCells / 2) / TorchSpacingCells)
                );
                for (int index = 0; index < torchCount; index++)
                {
                    int targetFaceIndex = (int)Math.Floor((index + 0.5) * run.Count / torchCount);
                    // Preserve the established spacing whenever its target is valid. For a
                    // corner-adjacent target, choose the nearest safe face with lower indices
                    // winning ties so serialized placement remains deterministic.
                    int selectedIndex = 0;
                    for (int candidate = 1; candidate < eligibleFaceIndices.Count; candidate++)
                    {
                        int selectedDistance = Math.Abs(
                            eligibleFaceIndices[selectedIndex] - targetFaceIndex
                        );
                        int candidateDistance = Math.Abs(
                            eligibleFaceIndices[candidate] - targetFaceIndex
                        );
                        if (candidateDistance < selectedDistance)
                            selectedIndex = candidate;
                    }

                    int faceIndex = eligibleFaceIndices[selectedIndex];
                    eligibleFaceIndices.RemoveAt(selectedIndex);
                    WallFace face = run[faceIndex];
                    occupiedFaces.Add(face);
                    torchIndex++;
                    placements.Add(
                        new DungeonObjectPlacement(
                            "sconce-" + torchIndex.ToString("D4", CultureInfo.InvariantCulture),
                            TorchAssetId,
                            face.Cell,
                            face.Rotation,
                            yOffset: 0.2f
                        )
                    );
                }
            }

            foreach (DungeonRoom room in rooms)
            {
                List<WallFace> candidates = FindWallFaces(
                        rows,
                        room.MinimumX,
                        room.MinimumZ,
                        room.MaximumX,
                        room.MaximumZ,
                        reserved
                    )
                    .Where(face => !occupiedFaces.Contains(face))
                    .ToList();
                for (int attempt = 0; attempt < BannerAttemptsPerRoom; attempt++)
                {
                    if (!random.NextPercent(BannerPlacementChancePercent) || candidates.Count == 0)
                        continue;

                    int candidateIndex = random.NextInt(candidates.Count);
                    WallFace face = candidates[candidateIndex];
                    candidates.RemoveAt(candidateIndex);
                    occupiedFaces.Add(face);
                    placements.Add(
                        new DungeonObjectPlacement(
                            "decoration-"
                                + room.Id.ToString("D4", CultureInfo.InvariantCulture)
                                + "-"
                                + (attempt + 1).ToString("D2", CultureInfo.InvariantCulture),
                            BannerAssetId,
                            face.Cell,
                            face.Rotation,
                            yOffset: 0.25f
                        )
                    );
                }
            }

            return placements.ToArray();
        }

        private static List<WallFace> FindWallFaces(
            IReadOnlyList<string> rows,
            int minimumX,
            int minimumZ,
            int maximumX,
            int maximumZ,
            HashSet<DungeonCell> reservedCells
        )
        {
            List<WallFace> candidates = new();
            for (int z = minimumZ; z <= maximumZ; z++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    if (!IsDecorationAnchor(rows, x, z, reservedCells))
                        continue;

                    AddWallFace(rows, candidates, x, z, 0, -1, 0);
                    AddWallFace(rows, candidates, x, z, -1, 0, 90);
                    AddWallFace(rows, candidates, x, z, 0, 1, 180);
                    AddWallFace(rows, candidates, x, z, 1, 0, 270);
                }
            }

            return candidates;
        }

        private static bool HasTorchCornerClearance(IReadOnlyList<string> rows, WallFace face)
        {
            int alongX = face.Rotation == 0 || face.Rotation == 180 ? 1 : 0;
            int alongZ = alongX == 1 ? 0 : 1;
            for (int direction = -1; direction <= 1; direction += 2)
            {
                for (int distance = 1; distance < MinimumTorchCornerDistanceCells; distance++)
                {
                    if (
                        CellAt(
                            rows,
                            face.Cell.X + direction * alongX * distance,
                            face.Cell.Z + direction * alongZ * distance
                        ) == '#'
                    )
                        return false;
                }
            }

            return true;
        }

        private static IReadOnlyList<IReadOnlyList<WallFace>> FindWallRuns(
            IReadOnlyList<string> rows,
            HashSet<DungeonCell> reservedCells
        )
        {
            int width = rows.Count == 0 ? 0 : rows[0].Length;
            List<WallFace> faces = FindWallFaces(
                rows,
                0,
                0,
                width - 1,
                rows.Count - 1,
                reservedCells
            );
            List<IReadOnlyList<WallFace>> runs = new();
            int[] rotations = { 0, 90, 180, 270 };
            foreach (int rotation in rotations)
            {
                IEnumerable<IGrouping<int, WallFace>> lines = faces
                    .Where(face => face.Rotation == rotation)
                    .GroupBy(face => LineCoordinate(face))
                    .OrderBy(group => group.Key);
                foreach (IGrouping<int, WallFace> line in lines)
                {
                    List<WallFace> run = new();
                    int previous = int.MinValue;
                    foreach (WallFace face in line.OrderBy(AlongCoordinate))
                    {
                        int along = AlongCoordinate(face);
                        if (run.Count > 0 && along != previous + 1)
                        {
                            runs.Add(run.ToArray());
                            run.Clear();
                        }

                        run.Add(face);
                        previous = along;
                    }

                    if (run.Count > 0)
                        runs.Add(run.ToArray());
                }
            }

            return runs;
        }

        private static int LineCoordinate(WallFace face)
        {
            return face.Rotation == 0 || face.Rotation == 180 ? face.Cell.Z : face.Cell.X;
        }

        private static int AlongCoordinate(WallFace face)
        {
            return face.Rotation == 0 || face.Rotation == 180 ? face.Cell.X : face.Cell.Z;
        }

        private static void AddWallFace(
            IReadOnlyList<string> rows,
            ICollection<WallFace> candidates,
            int x,
            int z,
            int offsetX,
            int offsetZ,
            int rotation
        )
        {
            if (CellAt(rows, x + offsetX, z + offsetZ) == '#')
                candidates.Add(new WallFace(new DungeonCell(x, z), rotation));
        }

        private static bool IsDecorationAnchor(
            IReadOnlyList<string> rows,
            int x,
            int z,
            HashSet<DungeonCell> reservedCells
        )
        {
            return CellAt(rows, x, z) == '.' && !reservedCells.Contains(new DungeonCell(x, z));
        }

        private static char CellAt(IReadOnlyList<string> rows, int x, int z)
        {
            if (z < 0 || z >= rows.Count || x < 0)
                return ' ';
            string row = rows[rows.Count - 1 - z];
            return x < row.Length ? row[x] : ' ';
        }

        private readonly struct WallFace
        {
            internal WallFace(DungeonCell cell, int rotation)
            {
                Cell = cell;
                Rotation = rotation;
            }

            internal DungeonCell Cell { get; }
            internal int Rotation { get; }

            public override bool Equals(object obj)
            {
                return obj is WallFace other && Cell == other.Cell && Rotation == other.Rotation;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Cell.GetHashCode() * 397) ^ Rotation;
                }
            }
        }
    }
}
