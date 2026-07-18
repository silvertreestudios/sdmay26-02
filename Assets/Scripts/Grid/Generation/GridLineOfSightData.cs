using System.Collections.Generic;
using UnityEngine;

namespace GridPrivate
{
    public static class GridLineOfSightData
    {
        private static readonly Dictionary<Tile[,], VisibilityGrid> VisibilityByGrid = new();

        public static void Register(Tile[,] tiles, bool[,] blockers, TileType[,] gridData = null)
        {
            if (tiles == null || blockers == null ||
                tiles.GetLength(0) != blockers.GetLength(0) ||
                tiles.GetLength(1) != blockers.GetLength(1))
            {
                Debug.LogWarning("Grid line-of-sight data was not registered because tile and blocker dimensions are invalid.");
                return;
            }

            bool[,] transparentObstacles = new bool[tiles.GetLength(0), tiles.GetLength(1)];
            if (gridData != null &&
                gridData.GetLength(0) == tiles.GetLength(0) &&
                gridData.GetLength(1) == tiles.GetLength(1))
            {
                for (int x = 0; x < tiles.GetLength(0); x++)
                {
                    for (int z = 0; z < tiles.GetLength(1); z++)
                    {
                        transparentObstacles[x, z] =
                            gridData[x, z] == TileType.Obstacle && !blockers[x, z];
                    }
                }
            }

            VisibilityByGrid[tiles] = new VisibilityGrid(blockers, transparentObstacles);
        }

        public static void Unregister(Tile[,] tiles)
        {
            if (tiles != null)
                VisibilityByGrid.Remove(tiles);
        }

        public static bool IsBlocking(Tile[,] tiles, Vector3Int cell)
        {
            if (!IsInBounds(tiles, cell))
                return true;

            if (VisibilityByGrid.TryGetValue(tiles, out VisibilityGrid visibility))
            {
                return visibility.Blockers[cell.x, cell.z] ||
                       (tiles[cell.x, cell.z] == null &&
                        !visibility.TransparentObstacles[cell.x, cell.z]);
            }

            return tiles[cell.x, cell.z] == null;
        }

        private static bool IsInBounds(Tile[,] tiles, Vector3Int cell)
        {
            return tiles != null &&
                   cell.x >= 0 && cell.z >= 0 &&
                   cell.x < tiles.GetLength(0) && cell.z < tiles.GetLength(1);
        }

        private sealed class VisibilityGrid
        {
            public bool[,] Blockers { get; }
            public bool[,] TransparentObstacles { get; }

            public VisibilityGrid(bool[,] blockers, bool[,] transparentObstacles)
            {
                Blockers = blockers;
                TransparentObstacles = transparentObstacles;
            }
        }
    }
}
