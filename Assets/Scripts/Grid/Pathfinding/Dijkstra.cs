using NUnit;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace GridPrivate
{
    /// <summary>
    /// Isolated Dijkstra pathfinding for grid-based movement.
    /// </summary>
    public class Dijkstra : IPathfinder
    {
        protected Dictionary<Vector3Int, PathNode> Locations = new();
        protected Dictionary<Vector3Int, Neighbors> NeighborCache = new();
        protected Heap<PathNode> Heap = new(PathNode.Cmp, PathNode.Eq);
        protected List<PathNode> Distances = new();
        protected Tile[,] Tiles;

        protected Vector3Int? Searched = null;

        public Dijkstra(Tile[,] tiles)
        {
            Tiles = tiles;
        }

        public List<PathNode> Pathfind(GameObject pathfinder, Vector3Int start, Vector3Int end)
        {
            Locations.Clear();
            Heap.Clear();
            Distances.Clear();
            Searched = null;
            // Unpathable
            if ((end.x >= Tiles.GetLength(0)) || (end.z >= Tiles.GetLength(1)) || end.x < 0 || end.z < 0)
                return new();

            // Initialize heap
            Heap.Push( new PathNode( 0.0f, start));
            Locations.Add(start, new PathNode(0.0f, start));

            while (Heap.Count() > 0)
            {
                PathNode path = Heap.Pop();
                if (path.Location == end)
                    return TracePath(path);
                ExploreNeighbors(path, pathfinder);
            }

            PathNode found;
            if(Locations.TryGetValue(end, out found))
            {
                return TracePath(found);
            }
            return null;
        }

        public void Search(GameObject pathfinder, Vector3Int start)
        {
            Locations.Clear();
            Heap.Clear();
            Distances.Clear();
            Searched = start;

            // Initialize heap
            Heap.Push(new PathNode(0.0f, start));
            Locations.Add(start, new PathNode(0.0f, start));

            while (Heap.Count() > 0)
            {
                PathNode path = Heap.Pop();
                Distances.Add(path);
                ExploreNeighbors(path, pathfinder);
            }
        }

        public void Search(GameObject pathfinder, Vector3Int start, float range)
        {
            Locations.Clear();
            Heap.Clear();
            Distances.Clear();
            Searched = start;
            float maxCellDist = range / 5.0f;

            // Initialize heap
            Heap.Push(new PathNode(0.0f, start));
            Locations.Add(start, new PathNode(0.0f, start));

            while (Heap.Count() > 0)
            {
                PathNode path = Heap.Pop();
                if (path.Dist > maxCellDist)
                    break;
                Distances.Add(path);
                ExploreNeighbors(path, pathfinder);
            }
        }

        public List<PathNode> Find(Vector3Int end)
        {
            if (Searched == null)
            {
                Debug.LogError("Attempting \"Find\" before a \"Search\" is not valid. \n" +
                    "Call Pathfind for a single request, call Search then Find to request number of times.");
                return null;
            }
            // Unpathable
            if ((end.x >= Tiles.GetLength(0)) || (end.z >= Tiles.GetLength(1)) || end.x < 0 || end.z < 0)
                return new();
            PathNode path;
            if (Locations.TryGetValue(end, out path))
                return TracePath(path);
            return new();

        }

        public List<Vector3Int> InRange(GameObject pathfinder, Vector3Int start, float distance)
        {
            List<Vector3Int> inRange = new();
            Debug.Log("start search: " + Searched + " " + start);
            if (Searched == start)
            {
                int i = 0;
                while (i < Distances.Count && Distances[i].Dist <= distance)
                    inRange.Add(Distances[i++].Location);
                return inRange;
            }
            Locations.Clear();
            Heap.Clear();
            Searched = null;

            // Initialize heap
            Debug.Log("start search");
            Heap.Push(new PathNode(0.0f, start));
            Locations.Add(start, new PathNode(0.0f, start));

            while (Heap.Count() > 0)
            {
                Debug.Log("Range Search");
                PathNode path = Heap.Pop();
                if (path.Dist > distance)
                    return inRange;
                else
                    inRange.Add(path.Location);
                ExploreNeighbors(path, pathfinder);
            }
            return inRange;
        }

        public List<Vector3Int> CalculateEmination(Vector3Int start, float range)
        {
            List<Vector3Int> rangeTiles = new List<Vector3Int>();
            int _range = (int)(range * 0.2f);
            // Range of 1 represents melee attack - only adjacent tiles
            if (_range == 1)
            {
                // Include all adjacent tiles (cardinal directions)
                foreach (var dir in Directions)
                {
                    Vector3Int adjacent = start + dir;
                    if (adjacent.x >= 0 && adjacent.z >= 0 && adjacent.x < Tiles.GetLength(0) && adjacent.z < Tiles.GetLength(1))
                    {
                        rangeTiles.Add(adjacent);
                    }
                }
            }
            else
            {
                float maxRangeSquared = _range * _range;
                // iterate over square area around the start cell, but only include tiles within the circular radius
                for (int x = -_range; x <= _range; x++)
                {
                    for (int z = -_range; z <= _range; z++)
                    {
                        // Skip the starting cell
                        if (x == 0 && z == 0)
                            continue;

                        Vector3Int candidate = new Vector3Int(start.x + x, start.y, start.z + z);

                        // Check bounds first
                        if (candidate.x < 0 || candidate.z < 0 || candidate.x >= Tiles.GetLength(0) || candidate.z >= Tiles.GetLength(1))
                            continue;

                        float distanceSquared = x * x + z * z;
                        if (distanceSquared <= maxRangeSquared)
                        {
                            rangeTiles.Add(candidate);
                        }
                    }
                }
            }
            return rangeTiles;
        }

        /// <summary>
        /// Explores neighbors
        /// </summary>
        protected void ExploreNeighbors(PathNode path, GameObject pathfinder)
        {
            // Get offsets of neighbors
            Neighbors neighbors;
            List<int> neighborOffsets;
            if (!NeighborCache.TryGetValue(path.Location, out neighbors))
            {
                neighborOffsets = new();
                // Cache neighbors
                for (int i = 0; i < OFFSETS.Length; i++)
                {
                    Vector3Int offset = OFFSETS[i];
                    // Only 2d for now
                    int x = offset.x + path.Location.x;
                    int z = offset.z + path.Location.z;
                    if (
                        offset.y != 0 || (
                        x < 0 ||
                        z < 0 ||
                        x >= Tiles.GetLength(0) ||
                        z >= Tiles.GetLength(1) ||
                        Tiles[x, z] == null)
                    )
                        continue;
                    neighbors |= (Neighbors)(1 << i);
                    neighborOffsets.Add(i);
                }
                NeighborCache.Add(path.Location, neighbors);
            }
            else
            {
                neighborOffsets = GetNeighborOffsets(neighbors);
            }

            // Filter to accessible
            for(int i = 0; i < neighborOffsets.Count; i++)
            {
                Vector3Int cell = path.Location + OFFSETS[neighborOffsets[i]];
                Tile tile = Tiles[cell.x, cell.z];
                if (tile == null || (pathfinder != null && !tile.CanStrideOn(pathfinder)))
                    neighborOffsets.RemoveAt(i--);
            }
            RemoveUnusableDiagonals(neighborOffsets);

            // Update heap and locations
            foreach (int offset in neighborOffsets)
            {
                Vector3Int cell = path.Location + OFFSETS[offset];

                float distTo = NEIGHBOR_DISTANCE[offset] + path.Dist;
                PathNode newPath = new PathNode(distTo, cell, path);
                PathNode existing;
                if (Locations.TryGetValue(cell, out existing))
                {
                    // Replace
                    if (existing.Dist >= distTo)
                    {
                        Heap.Replace(existing, newPath);
                        Locations.Remove(cell);
                        Locations.Add(cell, newPath);
                    }
                }
                else
                {
                    Heap.Push(newPath);
                    Locations.Add(cell, newPath);
                }
            }
        }

        protected List<PathNode> TracePath(PathNode end)
        {
            List<PathNode> backwards = new();
            while(end != null)
            {
                backwards.Add(end);
                end = end.Prev;
            }
            backwards.Reverse();
            return backwards;
        }


        /// <summary>
        /// Removes unusable diagonals from the list
        /// </summary>
        /// <param name="neighbors"></param>
        protected void RemoveUnusableDiagonals(List<int> neighbors)
        {
            if (!neighbors.Contains(0)) // X
            {
                neighbors.Remove(10); // X Z
                neighbors.Remove(11); // X-Z
            }
            if (!neighbors.Contains(1)) // Z
            {
                neighbors.Remove(12); //-X Z
                neighbors.Remove(13); //-X-Z
            }
            if (!neighbors.Contains(4)) //-X
            {
                neighbors.Remove(10); // X Z
                neighbors.Remove(12); //-X Z
            }
            if (!neighbors.Contains(5)) //-Z
            {
                neighbors.Remove(11); // X-Z
                neighbors.Remove(13); //-X-Z
            }
        }

        //===========================================
        // Rest of file is neighbor lookup
        //===========================================

        protected List<int> GetNeighborOffsets(Neighbors neighbors)
        {
            int mask = (int)neighbors;

            // Max possible 3D(26), 2D(8)
            List<int> result = new(8);

            for (int i = 0; i < OFFSETS.Length; i++)
            {
                // Extract bit (0 or 1)
                if(((mask >> i) & 1) == 1)
                    result.Add(i);
            }

            return result;
        }

        [System.Flags]
        protected enum Neighbors
        {
            None = 0,

            X = 1 << 0,
            Nx = 1 << 1,
            Y = 1 << 2,
            Ny = 1 << 3,
            Z = 1 << 4,
            Nz = 1 << 5,

            XY = 1 << 6,
            XNy = 1 << 7,
            NxY = 1 << 8,
            NxNy = 1 << 9,

            XZ = 1 << 10,
            XNz = 1 << 11,
            NxZ = 1 << 12,
            NxNz = 1 << 13,

            YZ = 1 << 14,
            YNz = 1 << 15,
            NyZ = 1 << 16,
            NyNz = 1 << 17,

            XYZ = 1 << 18,
            XYNz = 1 << 19,
            XNyZ = 1 << 20,
            XNyNz = 1 << 21,
            NxYZ = 1 << 22,
            NxYNz = 1 << 23,
            NxNyZ = 1 << 24,
            NxNyNz = 1 << 25,
        }

        protected Neighbors ToNeighbor(Vector3Int v)
        {
            switch (v.x, v.y, v.z)
            {
                // None
                case (0, 0, 0): return Neighbors.None;

                // Axes
                case (1, 0, 0): return Neighbors.X;
                case (-1, 0, 0): return Neighbors.Nx;
                case (0, 1, 0): return Neighbors.Y;
                case (0, -1, 0): return Neighbors.Ny;
                case (0, 0, 1): return Neighbors.Z;
                case (0, 0, -1): return Neighbors.Nz;

                // XY plane
                case (1, 1, 0): return Neighbors.XY;
                case (1, -1, 0): return Neighbors.XNy;
                case (-1, 1, 0): return Neighbors.NxY;
                case (-1, -1, 0): return Neighbors.NxNy;

                // XZ plane
                case (1, 0, 1): return Neighbors.XZ;
                case (1, 0, -1): return Neighbors.XNz;
                case (-1, 0, 1): return Neighbors.NxZ;
                case (-1, 0, -1): return Neighbors.NxNz;

                // YZ plane
                case (0, 1, 1): return Neighbors.YZ;
                case (0, 1, -1): return Neighbors.YNz;
                case (0, -1, 1): return Neighbors.NyZ;
                case (0, -1, -1): return Neighbors.NyNz;

                // 3D diagonals
                case (1, 1, 1): return Neighbors.XYZ;
                case (1, 1, -1): return Neighbors.XYNz;
                case (1, -1, 1): return Neighbors.XNyZ;
                case (1, -1, -1): return Neighbors.XNyNz;
                case (-1, 1, 1): return Neighbors.NxYZ;
                case (-1, 1, -1): return Neighbors.NxYNz;
                case (-1, -1, 1): return Neighbors.NxNyZ;
                case (-1, -1, -1): return Neighbors.NxNyNz;

                default:
                    throw new ArgumentException($"Invalid neighbor offset: {v}");
            }
        }

        private static readonly Vector3Int[] OFFSETS = new Vector3Int[]
        {
            new Vector3Int( 1,  0,  0), // X
            new Vector3Int(-1,  0,  0), // Nx
            new Vector3Int( 0,  1,  0), // Y
            new Vector3Int( 0, -1,  0), // Ny
            new Vector3Int( 0,  0,  1), // Z
            new Vector3Int( 0,  0, -1), // Nz

            new Vector3Int( 1,  1,  0),
            new Vector3Int( 1, -1,  0),
            new Vector3Int(-1,  1,  0),
            new Vector3Int(-1, -1,  0),

            new Vector3Int( 1,  0,  1),
            new Vector3Int( 1,  0, -1),
            new Vector3Int(-1,  0,  1),
            new Vector3Int(-1,  0, -1),

            new Vector3Int( 0,  1,  1),
            new Vector3Int( 0,  1, -1),
            new Vector3Int( 0, -1,  1),
            new Vector3Int( 0, -1, -1),

            new Vector3Int( 1,  1,  1),
            new Vector3Int( 1,  1, -1),
            new Vector3Int( 1, -1,  1),
            new Vector3Int( 1, -1, -1),
            new Vector3Int(-1,  1,  1),
            new Vector3Int(-1,  1, -1),
            new Vector3Int(-1, -1,  1),
            new Vector3Int(-1, -1, -1),
        };

        private static readonly Vector3Int[] Directions = new[]
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),

            new Vector3Int(1, 0, 1),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(1, 0, -1),
            new Vector3Int(-1, 0, -1)
        };

        private static readonly float[] NEIGHBOR_DISTANCE = BuildDistances();

        private static float[] BuildDistances()
        {
            var d = new float[OFFSETS.Length];

            for (int i = 0; i < OFFSETS.Length; i++)
            {
                var o = OFFSETS[i];
                int count =
                    (o.x != 0 ? 1 : 0) +
                    (o.y != 0 ? 1 : 0) +
                    (o.z != 0 ? 1 : 0);

                d[i] = Mathf.Sqrt(count);
            }

            return d;
        }
    }
}