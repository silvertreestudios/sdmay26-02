using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Isolated Dijkstra pathfinding for grid-based movement.
/// Supports both cardinal and diagonal movement
/// </summary>
public class GridPathfinder
{
    // reference to the grid for walkability checks
    private readonly GridRenderer3D grid;

    // configuration for diagonal movement
    private bool allowDiagonalMovement;
    private float diagonalCost;

    // reusable arrays for distances and previous nodes
    private float[] distanceArray;
    private Vector3Int?[] previousArray;
    private readonly MinHeap heap = new MinHeap();

    // cardinal directions (up, down, left, right)
    private static readonly Vector3Int[] CardinalDirections = new[]
    {
        new Vector3Int(1, 0, 0),   // East
        new Vector3Int(-1, 0, 0),  // West
        new Vector3Int(0, 0, 1),   // North
        new Vector3Int(0, 0, -1)   // South
    };

    // diagonal directions (northeast, northwest, southeast, southwest)
    private static readonly Vector3Int[] DiagonalDirections = new[]
    {
        new Vector3Int(1, 0, 1),   // Northeast
        new Vector3Int(-1, 0, 1),  // Northwest
        new Vector3Int(1, 0, -1),  // Southeast
        new Vector3Int(-1, 0, -1)  // Southwest
    };

    /// <summary>
    /// Creates a new pathfinder for the given grid
    /// </summary>
    /// <param name="gridReference">Grid to pathfind on</param>
    /// <param name="allowDiagonal">Whether to allow diagonal movement</param>
    /// <param name="diagCost">Cost multiplier for diagonal moves (typically √2 ≈ 1.414)</param>
    public GridPathfinder(GridRenderer3D gridReference, bool allowDiagonal = true, float diagCost = 1.414f)
    {
        grid = gridReference ?? throw new ArgumentNullException(nameof(gridReference));
        allowDiagonalMovement = allowDiagonal;
        diagonalCost = diagCost;
    }

    /// <summary>
    /// Updates pathfinding configuration at runtime
    /// </summary>
    public void SetDiagonalMovement(bool allow, float cost = 1.414f)
    {
        allowDiagonalMovement = allow;
        diagonalCost = cost;
    }

    /// <summary>
    /// Finds the shortest path between two grid cells using Dijkstra's algorithm
    /// </summary>
    /// <param name="start">Starting cell position</param>
    /// <param name="target">Target cell position</param>
    /// <returns>Tuple containing (found, distance, path)</returns>
    public (bool found, float distance, List<Vector3Int> path) FindPath(Vector3Int start, Vector3Int target)
    {
        // helper to convert 2D cell to 1D index
        int w = grid.width, h = grid.height, total = w * h;

        // local function to compute index from Vector3Int
        int Idx(Vector3Int p) => p.x + p.z * w;

        // reuse arrays instead of allocating new ones each time
        EnsureArrayCapacity(total);

        var dist = distanceArray;
        var prev = previousArray;

        // initialize distances to infinity
        for (int i = 0; i < total; i++)
            dist[i] = float.PositiveInfinity;

        // set start distance to 0
        dist[Idx(start)] = 0f;
        heap.Push(new PathNode { pos = start, dist = 0f });

        // main search loop
        while (heap.Count > 0)
        {
            var node = heap.Pop();
            var u = node.pos;
            int uIdx = Idx(u);

            // skip if we've already found a better path to this node
            if (node.dist != dist[uIdx]) continue;

            // stop if target reached - reconstruct path
            if (u == target)
            {
                return (true, dist[uIdx], ReconstructPath(start, target, prev, Idx));
            }

            // explore cardinal direction neighbors (up, down, left, right)
            ExploreNeighbors(u, CardinalDirections, 1f, w, h, dist, prev, Idx);

            // explore diagonal neighbors if enabled
            if (allowDiagonalMovement)
            {
                ExploreNeighbors(u, DiagonalDirections, diagonalCost, w, h, dist, prev, Idx);
            }
        }

        // no path found
        return (false, -1f, null);
    }

    /// <summary>
    /// Ensures arrays are sized correctly and cleared for reuse
    /// </summary>
    private void EnsureArrayCapacity(int requiredSize)
    {
        if (distanceArray == null || distanceArray.Length != requiredSize)
        {
            // allocate new arrays
            distanceArray = new float[requiredSize];
            previousArray = new Vector3Int?[requiredSize];
        }
        else
        {
            // clear existing arrays for reuse
            Array.Clear(distanceArray, 0, requiredSize);
            Array.Clear(previousArray, 0, requiredSize);
        }
    }

    /// <summary>
    /// Explores neighbors in given directions with specified move cost
    /// </summary>
    private void ExploreNeighbors(Vector3Int current, Vector3Int[] directions, float moveCost,
        int width, int height, float[] dist, Vector3Int?[] prev, Func<Vector3Int, int> indexer)
    {
        int uIdx = indexer(current);

        for (int i = 0; i < directions.Length; i++)
        {
            var v = current + directions[i];

            // bounds check using unsigned comparison trick for performance
            if ((uint)v.x >= (uint)width || (uint)v.z >= (uint)height)
                continue;

            // check if neighbor cell is walkable
            if (!grid.IsCellWalkable(v.x, v.z))
                continue;

            // for diagonal movement, also check if both adjacent cardinal cells are walkable
            // this prevents "cutting corners" through walls
            if (moveCost > 1f && !CanMoveDiagonally(current, v))
                continue;

            int vIdx = indexer(v);
            float alt = dist[uIdx] + moveCost;

            // if this path to neighbor is shorter, update it
            if (alt < dist[vIdx])
            {
                dist[vIdx] = alt;
                prev[vIdx] = current;
                heap.Push(new PathNode { pos = v, dist = alt });
            }
        }
    }

    /// <summary>
    /// Checks if diagonal movement is valid (both adjacent cardinal cells are walkable)
    /// </summary>
    private bool CanMoveDiagonally(Vector3Int from, Vector3Int to)
    {
        int dx = to.x - from.x;
        int dz = to.z - from.z;

        // check horizontal adjacent cell
        var horizontal = new Vector3Int(from.x + dx, 0, from.z);
        if (!grid.IsCellWalkable(horizontal.x, horizontal.z))
            return false;

        // check vertical adjacent cell
        var vertical = new Vector3Int(from.x, 0, from.z + dz);
        if (!grid.IsCellWalkable(vertical.x, vertical.z))
            return false;

        return true;
    }

    /// <summary>
    /// Reconstructs the path by following previous pointers from target back to start
    /// </summary>
    private List<Vector3Int> ReconstructPath(Vector3Int start, Vector3Int target,
        Vector3Int?[] prev, Func<Vector3Int, int> indexer)
    {
        var path = new List<Vector3Int>();
        var current = target;

        // follow the chain of previous nodes back to start
        while (true)
        {
            path.Add(current);
            if (current.Equals(start)) break;
            current = prev[indexer(current)].Value;
        }

        // reverse to get start -> target order
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Struct representing a grid node for pathfinding
    /// </summary>
    private struct PathNode
    {
        public Vector3Int pos;
        public float dist;
    }

    /// <summary>
    /// Min-heap priority queue optimized for pathfinding
    /// </summary>
    private class MinHeap
    {
        private readonly List<PathNode> data = new List<PathNode>();

        public int Count => data.Count;

        /// <summary>
        /// Adds a new node to the heap
        /// </summary>
        public void Push(PathNode node)
        {
            data.Add(node);
            SiftUp(data.Count - 1);
        }

        /// <summary>
        /// Removes and returns the node with smallest distance
        /// </summary>
        public PathNode Pop()
        {
            var result = data[0];
            int lastIdx = data.Count - 1;
            data[0] = data[lastIdx];
            data.RemoveAt(lastIdx);
            if (data.Count > 0) SiftDown(0);
            return result;
        }

        /// <summary>
        /// Bubbles node up until heap property holds
        /// </summary>
        private void SiftUp(int idx)
        {
            while (idx > 0)
            {
                int parentIdx = (idx - 1) >> 1;
                if (data[idx].dist >= data[parentIdx].dist) break;
                (data[idx], data[parentIdx]) = (data[parentIdx], data[idx]);
                idx = parentIdx;
            }
        }

        /// <summary>
        /// Pushes node down until heap property restored
        /// </summary>
        private void SiftDown(int idx)
        {
            while (true)
            {
                int left = (idx << 1) + 1;
                int right = left + 1;
                int smallest = idx;

                if (left < data.Count && data[left].dist < data[smallest].dist)
                    smallest = left;
                if (right < data.Count && data[right].dist < data[smallest].dist)
                    smallest = right;
                if (smallest == idx) break;

                (data[idx], data[smallest]) = (data[smallest], data[idx]);
                idx = smallest;
            }
        }
    }
}