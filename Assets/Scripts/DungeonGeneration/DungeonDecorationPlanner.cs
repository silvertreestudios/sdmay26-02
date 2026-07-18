using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Adds deterministic, nonblocking KayKit wall decorations to generated room data.
    /// </summary>
    internal static class DungeonDecorationPlanner
    {
        internal const string BannerAssetId = "dungeon/assets/fbx(unity)/banner_red";
        internal const string TorchAssetId = "dungeon/assets/fbx(unity)/torch_mounted";
        private const int AttemptsPerRoom = 2;
        private const int PlacementChancePercent = 35;

        internal static IReadOnlyList<DungeonObjectPlacement> CreatePlacements(
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IDungeonRandom random)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (rooms == null)
                throw new ArgumentNullException(nameof(rooms));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            List<DungeonObjectPlacement> placements = new();
            foreach (DungeonRoom room in rooms)
            {
                List<WallFace> candidates = FindWallFaces(rows, room);
                for (int attempt = 0; attempt < AttemptsPerRoom; attempt++)
                {
                    if (!random.NextPercent(PlacementChancePercent) || candidates.Count == 0)
                        continue;

                    string assetId = random.NextInt(2) == 0 ? BannerAssetId : TorchAssetId;
                    int candidateIndex = random.NextInt(candidates.Count);
                    WallFace face = candidates[candidateIndex];
                    candidates.RemoveAt(candidateIndex);
                    placements.Add(new DungeonObjectPlacement(
                        "decoration-" + room.Id.ToString("D4", CultureInfo.InvariantCulture) +
                        "-" + (attempt + 1).ToString("D2", CultureInfo.InvariantCulture),
                        assetId,
                        face.Cell,
                        face.Rotation,
                        yOffset: assetId == BannerAssetId ? 0.25f : 0.2f));
                }
            }

            return placements.ToArray();
        }

        private static List<WallFace> FindWallFaces(
            IReadOnlyList<string> rows,
            DungeonRoom room)
        {
            List<WallFace> candidates = new();
            for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
            {
                for (int x = room.MinimumX; x <= room.MaximumX; x++)
                {
                    if (!IsWalkable(rows, x, z))
                        continue;

                    AddWallFace(rows, candidates, x, z, 0, -1, 0);
                    AddWallFace(rows, candidates, x, z, -1, 0, 90);
                    AddWallFace(rows, candidates, x, z, 0, 1, 180);
                    AddWallFace(rows, candidates, x, z, 1, 0, 270);
                }
            }

            return candidates;
        }

        private static void AddWallFace(
            IReadOnlyList<string> rows,
            ICollection<WallFace> candidates,
            int x,
            int z,
            int offsetX,
            int offsetZ,
            int rotation)
        {
            if (CellAt(rows, x + offsetX, z + offsetZ) == '#')
                candidates.Add(new WallFace(new DungeonCell(x, z), rotation));
        }

        private static bool IsWalkable(IReadOnlyList<string> rows, int x, int z)
        {
            char cell = CellAt(rows, x, z);
            return cell == '.' || cell == 'D';
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
        }
    }
}
