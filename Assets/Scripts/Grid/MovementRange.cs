using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Manages visual highlights for reachable tiles within movement range.
/// </summary>
public class MovementRange
{
    private readonly IGridMemory grid;
    private readonly bool allowDiagonalMovement;
    private readonly float diagonalCost;
    private readonly GameObject highlightPrefab;
    private readonly Color highlightColor;
    private readonly float highlightHeightOffset;
    private readonly List<GameObject> activeHighlights = new List<GameObject>();
    private HashSet<Vector3Int> currentReachableTiles = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> attackedTiles = new HashSet<Vector3Int>();
    private readonly System.Func<int, int, float, Vector3> gridToWorld;

    // Cached direction arrays to avoid repeated allocations
    private static readonly Vector3Int[] CardinalDirections = new[]
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

    private static readonly Vector3Int[] DiagonalDirections = new[]
    {
        new Vector3Int(1, 0, 1),
        new Vector3Int(-1, 0, 1),
        new Vector3Int(1, 0, -1),
        new Vector3Int(-1, 0, -1)
    };

    /// <summary>
    /// Creates a new MovementRange
    /// </summary>
    /// <param name="controller">Reference to the grid controller</param>
    public MovementRange(GridCharacterController3D controller)
    {
        this.grid = controller.gridMemory;
        this.highlightPrefab = controller.rangeHighlightPrefab;
        this.highlightColor = controller.rangeHighlightColor;
        this.highlightHeightOffset = controller.rangeHighlightHeightOffset;
        this.allowDiagonalMovement = controller.allowDiagonalMovement;
        this.diagonalCost = controller.diagonalCost;
        this.gridToWorld = controller.coordinateConverter.GridCellCenterWorld;
    }

    /// <summary>
    /// Gets the current set of reachable tiles
    /// </summary>
    public HashSet<Vector3Int> ReachableTiles => new HashSet<Vector3Int>(currentReachableTiles);

    /// <summary>
    /// Checks if a specific cell is within the current reachable range
    /// </summary>
    public bool IsCellReachable(Vector3Int cell) => currentReachableTiles.Contains(cell);

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
            CreateHighlightVisuals(startCell, currentReachableTiles);
        }

        Debug.Log($"[MovementRangeHighlighter] Updated highlights: {currentReachableTiles.Count} tiles reachable.");
    }
    //similar to UpdateHighlights but for attacks
    public void UpdateAttackHighlights(Vector3Int startCell, HashSet<Vector3Int> attackedTiles)
    {
        // Clear existing highlights
        ClearHighlights();

        // Create visual highlights if prefab is assigned
        if (highlightPrefab != null)
        {
            CreateHighlightVisuals(startCell, attackedTiles);
        }

        Debug.Log($"[MovementRangeHighlighter] Updated attack highlights: {attackedTiles.Count} tiles reachable.");
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

            // Only proceed if within range
            if (currentCost > maxRange)
                continue;

            // Add to reachable if within range, walkable and not the start cell
            if (grid.IsCellWalkable(current) && current != start)
            {
                reachable.Add(current);
            }

            // Explore neighbors (even if current is not walkable, as long as we are within range)
            foreach (var neighbor in GetNeighbors(current))
            {
                // Check bounds
                if (!IsWithinBounds(neighbor))
                    continue;

                // Check walkability for neighbor; allow the start cell even if it's not walkable
                if (!grid.IsCellWalkable(neighbor) && neighbor != start)
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

        return reachable;
    }

    //this method calculates and returns all reachable tiles within a range that have line of sight to the start position
    public HashSet<Vector3Int> CalculateEmination(Vector3Int start, int maxRange)
    {
        attackedTiles.Clear();
        HashSet<Vector3Int> reachable = CalculateCircle(start, maxRange);


        foreach (var cell in reachable)
        {
            if (IsLineOfSightBlocked(start, cell) == false)
            {
                attackedTiles.Add(cell);
            }
        }

        return attackedTiles;
    }

    //similar to CalculateReachableTiles but ignores walkability and movement cost, just calculates a circle of tiles within range
    private HashSet<Vector3Int> CalculateCircle(Vector3Int start, int maxRange)
    {
        HashSet<Vector3Int> circleTiles = new HashSet<Vector3Int>();
        float effectiveRange = maxRange + 0.5f;

        for (int x = -maxRange; x <= maxRange; x++)
        {
            for (int z = -maxRange; z <= maxRange; z++)
            {
                Vector3Int candidate = new Vector3Int(start.x + x, start.y, start.z + z);
                // round down to the nearest whole number to avoid missing edge tiles
                float distance = Mathf.Sqrt(x * x + z * z);

                if (distance <= effectiveRange && IsWithinBounds(candidate) && (x != 0 || z != 0))
                {
                    circleTiles.Add(candidate);
                }
            }
        }

        return circleTiles;
    }

    //detects if an unwalkable tile is blocking line of sight between two tiles
    private bool IsLineOfSightBlocked(Vector3Int start, Vector3Int end)
    {
        // Use Bresenham's line algorithm or similar to iterate through cells between start and end
        // Check if any of those cells are unwalkable
        // This is a placeholder implementation; actual implementation may vary
        return false;
    }

    /// <summary>
    /// Gets all walkable tiles on the grid (for unlimited range)
    /// </summary>
    private HashSet<Vector3Int> GetAllWalkableTiles()
    {
        HashSet<Vector3Int> allTiles = new HashSet<Vector3Int>();
        for (int x = 0; x < grid.Width; x++)
        {
            for (int z = 0; z < grid.Height; z++)
            {
                if (grid.IsCellWalkable(new Vector3Int(x, 0, z)))
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

                bool adjacent1Valid = IsWithinBounds(adjacent1) && grid.IsCellWalkable(adjacent1);
                bool adjacent2Valid = IsWithinBounds(adjacent2) && grid.IsCellWalkable(adjacent2);

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
        return cell.x >= 0 && cell.x < grid.Width &&
               cell.z >= 0 && cell.z < grid.Height;
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
    private void CreateHighlightVisuals(Vector3Int startCell, HashSet<Vector3Int> tileSet)
    {
        foreach (var cell in tileSet)
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
                grid.CellSize * 0.1f,
                1f,
                grid.CellSize * 0.1f);

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