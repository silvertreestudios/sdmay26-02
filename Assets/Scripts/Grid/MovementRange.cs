using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages visual highlights for reachable tiles within movement range.
/// Calculates reachable tiles using depth-first search and creates highlight GameObjects.
/// </summary>
public class MovementRange
{
    // Grid reference for pathfinding and coordinate conversion
    private readonly GridRenderer3D grid;

    // Movement configuration
    private readonly bool allowDiagonalMovement;
    private readonly float diagonalCost;

    // Highlight visual configuration
    private readonly GameObject highlightPrefab;
    private readonly Color highlightColor;
    private readonly float highlightHeightOffset;

    // Runtime state
    private readonly List<GameObject> activeHighlights = new List<GameObject>();
    private HashSet<Vector3Int> currentReachableTiles = new HashSet<Vector3Int>();

    // Delegate for converting grid cells to world positions
    private readonly System.Func<int, int, float, Vector3> gridToWorld;

    // Cached direction arrays to avoid repeated allocations
    private static readonly Vector3Int[] CardinalDirections = new[]
    {
        new Vector3Int(1, 0, 0),   // East
        new Vector3Int(-1, 0, 0),  // West
        new Vector3Int(0, 0, 1),   // North
        new Vector3Int(0, 0, -1)   // South
    };

    private static readonly Vector3Int[] DiagonalDirections = new[]
    {
        new Vector3Int(1, 0, 1),   // Northeast
        new Vector3Int(-1, 0, 1),  // Northwest
        new Vector3Int(1, 0, -1),  // Southeast
        new Vector3Int(-1, 0, -1)  // Southwest
    };

    /// <summary>
    /// Gets the current set of reachable tiles
    /// </summary>
    public HashSet<Vector3Int> ReachableTiles => new HashSet<Vector3Int>(currentReachableTiles);

    /// <summary>
    /// Checks if a specific cell is within the current reachable range
    /// </summary>
    public bool IsCellReachable(Vector3Int cell) => currentReachableTiles.Contains(cell);

    /// <summary>
    /// Creates a new MovementRangeHighlighter
    /// </summary>
    /// <param name="gridReference">Grid for pathfinding</param>
    /// <param name="prefab">Prefab to instantiate for highlights</param>
    /// <param name="color">Color for highlight visuals</param>
    /// <param name="heightOffset">Height offset above grid</param>
    /// <param name="allowDiagonal">Whether diagonal movement is allowed</param>
    /// <param name="diagCost">Cost for diagonal movement</param>
    /// <param name="gridCellToWorld">Function to convert grid coordinates to world position</param>
    public MovementRange(
        GridRenderer3D gridReference,
        GameObject prefab,
        Color color,
        float heightOffset,
        bool allowDiagonal,
        float diagCost,
        System.Func<int, int, float, Vector3> gridCellToWorld)
    {
        if (gridReference == null)
        {
            Debug.LogError("[MovementRangeHighlighter] Grid reference cannot be null!");
        }

        this.grid = gridReference;
        this.highlightPrefab = prefab;
        this.highlightColor = color;
        this.highlightHeightOffset = heightOffset;
        this.allowDiagonalMovement = allowDiagonal;
        this.diagonalCost = Mathf.Max(1f, diagCost); // Ensure diagonal cost is at least 1
        this.gridToWorld = gridCellToWorld;
    }

    /// <summary>
    /// Updates highlights for a character at a given position
    /// </summary>
    /// <param name="startCell">Starting grid position</param>
    /// <param name="maxRange">Maximum movement range (0 = unlimited)</param>
    public void UpdateHighlights(Vector3Int startCell, int maxRange)
    {
        // Clear existing highlights
        ClearHighlights();

        // Calculate reachable tiles
        currentReachableTiles = CalculateReachableTiles(startCell, maxRange);

        // Create visual highlights if prefab is assigned
        if (highlightPrefab != null)
        {
            CreateHighlightVisuals(startCell);
        }

        Debug.Log($"[MovementRangeHighlighter] Updated highlights: {currentReachableTiles.Count} tiles reachable.");
    }

    /// <summary>
    /// Clears all highlight visuals and cached reachable tiles
    /// </summary>
    public void ClearHighlights()
    {
        foreach (var highlight in activeHighlights)
        {
            if (highlight != null)
            {
                Object.Destroy(highlight);
            }
        }
        activeHighlights.Clear();
        currentReachableTiles.Clear();
    }

    /// <summary>
    /// Calculates all reachable tiles within movement range using depth-first search
    /// </summary>
    private HashSet<Vector3Int> CalculateReachableTiles(Vector3Int start, int maxRange)
    {
        // Return all walkable tiles if unlimited range
        if (maxRange <= 0)
        {
            return GetAllWalkableTiles();
        }

        HashSet<Vector3Int> reachable = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, float> bestCost = new Dictionary<Vector3Int, float>();
        Stack<(Vector3Int cell, float cost)> stack = new Stack<(Vector3Int, float)>();

        // Start DFS
        stack.Push((start, 0f));
        bestCost[start] = 0f;

        while (stack.Count > 0)
        {
            var (current, currentCost) = stack.Pop();

            // Skip if we've already found a better path to this cell
            if (bestCost.TryGetValue(current, out float existingCost) && currentCost > existingCost)
                continue;

            // Add to reachable if within range and walkable
            if (currentCost <= maxRange && grid.IsCellWalkable(current.x, current.z))
            {
                reachable.Add(current);

                // Explore neighbors
                foreach (var neighbor in GetNeighbors(current))
                {
                    // Check bounds
                    if (!IsWithinBounds(neighbor))
                        continue;

                    // Check walkability
                    if (!grid.IsCellWalkable(neighbor.x, neighbor.z))
                        continue;

                    // Calculate movement cost
                    float moveCost = CalculateMovementCost(current, neighbor);
                    float newCost = currentCost + moveCost;

                    // Only add if within range and better than any previous path
                    if (newCost <= maxRange)
                    {
                        if (!bestCost.TryGetValue(neighbor, out float neighborBestCost) || newCost < neighborBestCost)
                        {
                            bestCost[neighbor] = newCost;
                            stack.Push((neighbor, newCost));
                        }
                    }
                }
            }
        }

        return reachable;
    }

    /// <summary>
    /// Gets all walkable tiles on the grid (for unlimited range)
    /// </summary>
    private HashSet<Vector3Int> GetAllWalkableTiles()
    {
        HashSet<Vector3Int> allTiles = new HashSet<Vector3Int>();
        for (int x = 0; x < grid.width; x++)
        {
            for (int z = 0; z < grid.height; z++)
            {
                if (grid.IsCellWalkable(x, z))
                {
                    allTiles.Add(new Vector3Int(x, 0, z));
                }
            }
        }
        return allTiles;
    }

    /// <summary>
    /// Gets neighboring cells based on movement settings
    /// </summary>
    private IEnumerable<Vector3Int> GetNeighbors(Vector3Int cell)
    {
        // Cardinal directions (always included)
        foreach (var dir in CardinalDirections)
        {
            yield return cell + dir;
        }

        // Diagonal directions (if allowed)
        if (allowDiagonalMovement)
        {
            foreach (var dir in DiagonalDirections)
            {
                // Validate diagonal movement (adjacent cells must be walkable)
                Vector3Int adjacent1 = cell + new Vector3Int(dir.x, 0, 0);
                Vector3Int adjacent2 = cell + new Vector3Int(0, 0, dir.z);

                bool adjacent1Valid = IsWithinBounds(adjacent1) && grid.IsCellWalkable(adjacent1.x, adjacent1.z);
                bool adjacent2Valid = IsWithinBounds(adjacent2) && grid.IsCellWalkable(adjacent2.x, adjacent2.z);

                if (adjacent1Valid && adjacent2Valid)
                {
                    yield return cell + dir;
                }
            }
        }
    }

    /// <summary>
    /// Checks if a cell is within grid bounds
    /// </summary>
    private bool IsWithinBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < grid.width &&
               cell.z >= 0 && cell.z < grid.height;
    }

    /// <summary>
    /// Calculates movement cost between two adjacent cells
    /// </summary>
    private float CalculateMovementCost(Vector3Int from, Vector3Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dz = Mathf.Abs(to.z - from.z);

        // Diagonal movement
        if (dx == 1 && dz == 1)
            return diagonalCost;

        // Cardinal movement
        return 1f;
    }

    /// <summary>
    /// Creates visual highlight GameObjects for reachable tiles
    /// </summary>
    private void CreateHighlightVisuals(Vector3Int startCell)
    {
        foreach (var cell in currentReachableTiles)
        {
            // Skip the starting cell
            if (cell.Equals(startCell))
                continue;

            // Instantiate highlight
            GameObject highlight = Object.Instantiate(highlightPrefab);
            highlight.name = $"RangeHighlight_{cell.x}_{cell.z}";

            // Position highlight
            Vector3 worldPos = gridToWorld(cell.x, cell.z, highlightHeightOffset);
            highlight.transform.position = worldPos;

            // Scale highlight to match grid cell size
            highlight.transform.localScale = new Vector3(
                grid.cellSize * 0.1f,
                1f,
                grid.cellSize * 0.1f);

            // Apply color using shared material to avoid leaks
            var renderer = highlight.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                // Create a material instance only once per highlight
                Material mat = new Material(renderer.sharedMaterial);
                mat.color = highlightColor;
                renderer.material = mat;
            }

            activeHighlights.Add(highlight);
        }
    }

    /// <summary>
    /// Cleans up all resources when no longer needed
    /// </summary>
    public void Dispose()
    {
        ClearHighlights();
    }
}
