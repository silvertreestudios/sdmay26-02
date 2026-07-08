using System;
using System.Collections.Generic;
using UnityEngine;

namespace GridPublic
{
    public enum StrikeCover
    {
        None,
        Lesser,
        Standard,
        Greater
    }

    public enum StrikeLineOfEffect
    {
        Clear,
        Blocked
    }

    public class StrikeTargetRequest
    {
        public int ReachFeet { get; set; } = 5;
        public int RangeIncrementFeet { get; set; } = 0;
        public bool IsRanged { get; set; } = false;
        public bool RequiresLineOfEffect { get; set; } = true;

        public int MaximumRangeFeet
        {
            get
            {
                if (IsRanged && RangeIncrementFeet > 0)
                    return RangeIncrementFeet * 6;
                return ReachFeet;
            }
        }
    }

    public class StrikeTargetResult
    {
        public GameObject Target { get; set; }
        public int DistanceFeet { get; set; }
        public StrikeLineOfEffect LineOfEffect { get; set; }
        public StrikeCover Cover { get; set; }
        public int RangePenalty { get; set; }

        public int CoverAcBonus
        {
            get
            {
                return Cover switch
                {
                    StrikeCover.Lesser => 1,
                    StrikeCover.Standard => 2,
                    StrikeCover.Greater => 4,
                    _ => 0
                };
            }
        }

        public bool IsLegal => Target != null && LineOfEffect == StrikeLineOfEffect.Clear;
    }
}

namespace GridPrivate
{
    public static class StrikeTargeting
    {
        private const float CornerOffset = 0.4f;
        private const int SamplesPerCell = 8;

        private static readonly Vector2[] CornerOffsets = new[]
        {
            new Vector2(-CornerOffset, -CornerOffset),
            new Vector2(CornerOffset, -CornerOffset),
            new Vector2(-CornerOffset, CornerOffset),
            new Vector2(CornerOffset, CornerOffset)
        };

        public static int MeasureGridDistanceFeet(Vector3Int start, Vector3Int target)
        {
            int dx = Mathf.Abs(target.x - start.x);
            int dz = Mathf.Abs(target.z - start.z);
            int diagonals = Mathf.Min(dx, dz);
            int straight = Mathf.Max(dx, dz) - diagonals;
            int diagonalFeet = (diagonals / 2) * 15 + (diagonals % 2) * 5;
            return diagonalFeet + straight * 5;
        }

        public static bool IsWithinStrikeRange(Vector3Int start, Vector3Int target, GridPublic.StrikeTargetRequest request)
        {
            if (request == null)
                return false;

            int distance = MeasureGridDistanceFeet(start, target);
            if (request.IsRanged)
                return request.RangeIncrementFeet > 0 && distance <= request.RangeIncrementFeet * 6;
            return distance <= request.ReachFeet;
        }

        public static int CalculateRangePenalty(int distanceFeet, int rangeIncrementFeet)
        {
            if (rangeIncrementFeet <= 0 || distanceFeet <= rangeIncrementFeet)
                return 0;

            int increment = Mathf.CeilToInt(distanceFeet / (float)rangeIncrementFeet);
            if (increment > 6)
                throw new ArgumentOutOfRangeException(nameof(distanceFeet), "Ranged Strikes cannot target beyond six range increments.");

            return -2 * (increment - 1);
        }

        public static List<Vector3Int> CellsInRange(Tile[,] tiles, Vector3Int start, GridPublic.StrikeTargetRequest request)
        {
            List<Vector3Int> result = new();
            if (tiles == null || request == null)
                return result;

            int maxCells = Mathf.CeilToInt(request.MaximumRangeFeet / 5.0f);
            for (int x = start.x - maxCells; x <= start.x + maxCells; x++)
            {
                for (int z = start.z - maxCells; z <= start.z + maxCells; z++)
                {
                    Vector3Int cell = new(x, start.y, z);
                    if (!GridTargeting.IsInBounds(tiles, cell) || cell == start)
                        continue;

                    if (IsWithinStrikeRange(start, cell, request))
                        result.Add(cell);
                }
            }
            return result;
        }

        public static GridPublic.StrikeTargetResult Evaluate(GameObject attacker, GameObject target, Tile[,] tiles, GridPublic.StrikeTargetRequest request)
        {
            if (attacker == null || target == null || tiles == null || request == null)
                return null;

            Vector3Int start = Vector3Int.RoundToInt(attacker.transform.position);
            Vector3Int targetCell = Vector3Int.RoundToInt(target.transform.position);
            int distance = MeasureGridDistanceFeet(start, targetCell);
            if (!IsWithinStrikeRange(start, targetCell, request))
                return null;

            int clearRays = GridTargeting.BlocksDiagonalCorner(tiles, start, targetCell) ? 0 : CountClearRays(tiles, start, targetCell);
            GridPublic.StrikeLineOfEffect lineOfEffect = clearRays > 0
                ? GridPublic.StrikeLineOfEffect.Clear
                : GridPublic.StrikeLineOfEffect.Blocked;

            if (request.RequiresLineOfEffect && lineOfEffect == GridPublic.StrikeLineOfEffect.Blocked)
                return null;

            GridPublic.StrikeCover cover = GridPublic.StrikeCover.None;
            if (request.IsRanged && clearRays > 0 && clearRays < 16)
                cover = GridPublic.StrikeCover.Standard;

            int rangePenalty = request.IsRanged
                ? CalculateRangePenalty(distance, request.RangeIncrementFeet)
                : 0;

            return new GridPublic.StrikeTargetResult
            {
                Target = target,
                DistanceFeet = distance,
                LineOfEffect = lineOfEffect,
                Cover = cover,
                RangePenalty = rangePenalty
            };
        }

        public static int CountClearRays(Tile[,] tiles, Vector3Int start, Vector3Int target)
        {
            int clear = 0;
            foreach (Vector2 startOffset in CornerOffsets)
            {
                foreach (Vector2 targetOffset in CornerOffsets)
                {
                    if (!IsRayBlocked(tiles, start, target, startOffset, targetOffset))
                        clear++;
                }
            }
            return clear;
        }

        private static bool BlocksDiagonalCorner(Tile[,] tiles, Vector3Int start, Vector3Int target)
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

        private static bool IsBlocking(Tile[,] tiles, Vector3Int cell)
        {
            return !IsInBounds(tiles, cell) || tiles[cell.x, cell.z] == null;
        }

        private static bool IsRayBlocked(Tile[,] tiles, Vector3Int start, Vector3Int target, Vector2 startOffset, Vector2 targetOffset)
        {
            Vector2 rayStart = new(start.x + 0.5f + startOffset.x, start.z + 0.5f + startOffset.y);
            Vector2 rayEnd = new(target.x + 0.5f + targetOffset.x, target.z + 0.5f + targetOffset.y);
            Vector2 delta = rayEnd - rayStart;
            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon)
                return false;

            int samples = Mathf.Max(1, Mathf.CeilToInt(distance * SamplesPerCell));
            for (int i = 1; i < samples; i++)
            {
                Vector2 sample = rayStart + delta * (i / (float)samples);
                Vector3Int cell = new(Mathf.FloorToInt(sample.x), start.y, Mathf.FloorToInt(sample.y));
                if (cell == start || cell == target)
                    continue;

                if (!IsInBounds(tiles, cell) || tiles[cell.x, cell.z] == null)
                    return true;
            }
            return false;
        }

        private static bool IsInBounds(Tile[,] tiles, Vector3Int cell)
        {
            return cell.x >= 0 && cell.z >= 0 && cell.x < tiles.GetLength(0) && cell.z < tiles.GetLength(1);
        }
    }
}
