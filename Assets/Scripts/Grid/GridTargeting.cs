using System.Collections.Generic;
using Game.KayKit;
using UnityEngine;

namespace GridPrivate
{
    public static class GridTargeting
    {
        private const float CornerOffset = 0.4f;
        private const int SamplesPerCell = 8;

        private static readonly Vector2[] CornerOffsets = new[]
        {
            new Vector2(-CornerOffset, -CornerOffset),
            new Vector2(CornerOffset, -CornerOffset),
            new Vector2(-CornerOffset, CornerOffset),
            new Vector2(CornerOffset, CornerOffset),
        };

        public static int MeasureGridDistanceFeet(Vector3Int start, Vector3Int target)
        {
            return MeasureGridDistanceFeet(
                Mathf.Abs(target.x - start.x),
                Mathf.Abs(target.z - start.z)
            );
        }

        public static int MeasureGridDistanceFeet(int dx, int dz)
        {
            int diagonals = Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dz));
            int straight = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) - diagonals;
            int diagonalFeet = (diagonals / 2) * 15 + (diagonals % 2) * 5;
            return diagonalFeet + straight * 5;
        }

        public static bool IsInBounds(Tile[,] tiles, Vector3Int cell)
        {
            return tiles != null
                && cell.x >= 0
                && cell.z >= 0
                && cell.x < tiles.GetLength(0)
                && cell.z < tiles.GetLength(1);
        }

        public static bool IsBlocking(Tile[,] tiles, Vector3Int cell)
        {
            return GridLineOfSightData.IsBlocking(tiles, cell);
        }

        public static bool BlocksDiagonalCorner(Tile[,] tiles, Vector3Int start, Vector3Int target)
        {
            int dx = target.x - start.x;
            int dz = target.z - start.z;
            if (Mathf.Abs(dx) != 1 || Mathf.Abs(dz) != 1)
                return false;

            int stepX = dx > 0 ? 1 : -1;
            int stepZ = dz > 0 ? 1 : -1;
            Vector3Int sideX = new(start.x + stepX, start.y, start.z);
            Vector3Int sideZ = new(start.x, start.y, start.z + stepZ);
            return IsBlocking(tiles, sideX) && IsBlocking(tiles, sideZ);
        }

        public static int CountClearRays(Tile[,] tiles, Vector3Int start, Vector3Int target)
        {
            int clear = 0;
            foreach (Vector2 startOffset in CornerOffsets)
            {
                foreach (Vector2 targetOffset in CornerOffsets)
                {
                    if (
                        !IsRayBlocked(
                            tiles,
                            CellPoint(start, startOffset),
                            CellPoint(target, targetOffset),
                            start,
                            target
                        )
                    )
                        clear++;
                }
            }
            return clear;
        }

        public static int CountClearRaysFromPoint(
            Tile[,] tiles,
            Vector2 startPoint,
            Vector3Int target
        )
        {
            int clear = 0;
            foreach (Vector2 targetOffset in CornerOffsets)
            {
                if (!IsRayBlocked(tiles, startPoint, CellPoint(target, targetOffset), null, target))
                    clear++;
            }
            return clear;
        }

        public static List<GameObject> OccupantsAt(Tile[,] tiles, Vector3Int cell)
        {
            if (!IsInBounds(tiles, cell) || tiles[cell.x, cell.z] == null)
                return new List<GameObject>();

            return new List<GameObject>(tiles[cell.x, cell.z].Occupants);
        }

        private static Vector2 CellPoint(Vector3Int cell, Vector2 offset)
        {
            return new Vector2(cell.x + 0.5f + offset.x, cell.z + 0.5f + offset.y);
        }

        private static bool IsRayBlocked(
            Tile[,] tiles,
            Vector2 rayStart,
            Vector2 rayEnd,
            Vector3Int? startCell,
            Vector3Int targetCell
        )
        {
            Vector2 delta = rayEnd - rayStart;
            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon)
                return false;

            int samples = Mathf.Max(1, Mathf.CeilToInt(distance * SamplesPerCell));
            for (int i = 1; i < samples; i++)
            {
                Vector2 sample = rayStart + delta * (i / (float)samples);
                Vector3Int cell = new(
                    Mathf.FloorToInt(sample.x),
                    targetCell.y,
                    Mathf.FloorToInt(sample.y)
                );
                if ((startCell.HasValue && cell == startCell.Value) || cell == targetCell)
                    continue;

                if (IsBlocking(tiles, cell))
                    return true;
            }

            Vector3 rayStart3D = new(rayStart.x, 0.75f, rayStart.y);
            Vector3 rayEnd3D = new(rayEnd.x, 0.75f, rayEnd.y);
            return MapLineOfSightBlocker.BlocksSegment(rayStart3D, rayEnd3D);
        }
    }
}
