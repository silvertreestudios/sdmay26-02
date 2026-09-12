using UnityEngine;

namespace GridPrivate
{
    public static class GridTargeting
    {
        private const int SamplesPerCell = 8;

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

        /// <summary>
        /// Counts rays clear of both captured grid obstruction and captured map colliders.
        /// Burst captures use four point-origin rays; other captures use sixteen corner pairs.
        /// This evaluates historical inputs only, even when line of effect is not required:
        /// callers still need raw ray counts for cover. Recapture before action confirmation.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">The snapshot is missing.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">The target was not captured.</exception>
        public static int CountClearRays(GridPublic.TargetingSnapshot snapshot, Vector3Int target)
        {
            if (snapshot == null)
                throw new System.ArgumentNullException(nameof(snapshot));
            ushort physicsClear = snapshot.PhysicsClearRayMask(target);
            int clear = 0;
            for (int ray = 0; ray < snapshot.PhysicsRayCount; ray++)
            {
                // Intersection is per ray, not the minimum of two independently counted sets.
                if ((physicsClear & (1 << ray)) == 0)
                    continue;
                if (
                    !IsCapturedGridRayBlocked(
                        snapshot,
                        snapshot.RayStart(ray),
                        snapshot.RayEnd(target, ray),
                        target
                    )
                )
                    clear++;
            }
            return clear;
        }

        private static bool IsCapturedGridRayBlocked(
            GridPublic.TargetingSnapshot snapshot,
            Vector2 rayStart,
            Vector2 rayEnd,
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
                // Keep the original full-cell equality (including elevation). Burst origins
                // are corners rather than cells and must not acquire a source-cell exemption.
                if (
                    cell == targetCell
                    || (snapshot.Shape != GridPublic.AreaShape.Burst && cell == snapshot.SourceCell)
                )
                    continue;
                if (snapshot.IsBlocking(cell))
                    return true;
            }
            return false;
        }
    }
}
