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
    // convert grid coordinates to world position
    private readonly System.Func<int, int, float, Vector3> gridToWorld;

    private readonly Color clearLOSColor = new Color(0f, 1f, 0f, 0.5f);
    private readonly Color partialLOSColor = new Color(1f, 1f, 0f, 0.5f);
    private readonly Color blockedLOSColor = new Color(1f, 0f, 0f, 0.5f);

    /// <summary>
    /// Represents the line-of-sight status for a tile
    /// </summary>
    public enum LOSStatus
    {
        Clear,
        PartialBlock,
        FullyBlocked
    }

    // directions for cardinal and diagonal movement, used for neighbor checks
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
    // offsets for raycasting to tile corners, used in LOS checks
    private static readonly Vector2[] TileCornerOffsets = new[]
    {
        new Vector2(-0.4f, -0.4f),
        new Vector2(0.4f, -0.4f),
        new Vector2(-0.4f, 0.4f),
        new Vector2(0.4f, 0.4f)
    };

    /// <summary>
    /// Creates a movement range system for a character
    /// </summary>
    public MovementRange(GridCharacterController3D controller)
    {
        grid = controller.gridMemory;
        highlightPrefab = controller.rangeHighlightPrefab;
        highlightColor = controller.rangeHighlightColor;
        highlightHeightOffset = controller.rangeHighlightHeightOffset;
        allowDiagonalMovement = controller.allowDiagonalMovement;
        diagonalCost = controller.diagonalCost;
        gridToWorld = controller.coordinateConverter.GridCellCenterWorld;
    }

    // currently reachable tiles are stored in hash set for quick lookup
    public HashSet<Vector3Int> ReachableTiles => new HashSet<Vector3Int>(currentReachableTiles);
    public bool IsCellReachable(Vector3Int cell) => currentReachableTiles.Contains(cell);
    public void UpdateHighlights(Vector3Int startCell, int maxRange)
    {
        ClearHighlights();
        currentReachableTiles = CalculateReachableTiles(startCell, maxRange);

        if (highlightPrefab != null)
        {
            CreateHighlightVisuals(startCell, currentReachableTiles);
        }
    }

    public void UpdateAttackHighlights(Vector3Int startCell, HashSet<Vector3Int> attackedTiles)
    {
        ClearHighlights();

        if (highlightPrefab != null)
        {
            CreateAttackHighlightsWithLOSStatus(startCell, attackedTiles);
        }
    }

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

    // calculate reachable tiles using a depth-first search
    private HashSet<Vector3Int> CalculateReachableTiles(Vector3Int start, int maxRange)
    {
        // If maxRange is zero or negative, treat it as unlimited range
        if (maxRange <= 0)
        {
            return GetAllWalkableTiles();
        }

        HashSet<Vector3Int> reachable = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, float> bestCost = new Dictionary<Vector3Int, float>();
        Stack<(Vector3Int cell, float cost)> stack = new Stack<(Vector3Int, float)>();

        stack.Push((start, 0f));
        bestCost[start] = 0f;

        // DFS loop
        while (stack.Count > 0)
        {
            var (current, currentCost) = stack.Pop();

            if (bestCost.TryGetValue(current, out float existingCost) && currentCost > existingCost)
                continue;

            if (currentCost > maxRange)
                continue;

            if (grid.IsCellWalkable(current) && current != start)
            {
                reachable.Add(current);
            }
            // for each neighbor, calculate new cost and add to stack if it's within range
            foreach (var neighbor in GetNeighbors(current))
            {
                if (!IsWithinBounds(neighbor) || (!grid.IsCellWalkable(neighbor) && neighbor != start))
                    continue;

                float newCost = currentCost + CalculateMovementCost(current, neighbor);

                if (newCost <= maxRange && (!bestCost.TryGetValue(neighbor, out float neighborBestCost) || newCost < neighborBestCost))
                {
                    bestCost[neighbor] = newCost;
                    stack.Push((neighbor, newCost));
                }
            }
        }

        return reachable;
    }

    // calculates attacked tiles using a simple circular area of effect
    public HashSet<Vector3Int> CalculateEmination(Vector3Int start, int maxRange)
    {
        attackedTiles.Clear();
        attackedTiles = CalculateCircle(start, maxRange);
        return attackedTiles;
    }

    // determines line of sight status for a target tile by checking center and corner rays
    private LOSStatus GetLOSStatus(Vector3Int start, Vector3Int target)
    {
        bool centerClear = !IsRayBlocked(start, target, Vector2.zero, Vector2.zero);

        // If center ray is blocked, check corners to see if it's fully blocked or just partially
        if (!centerClear)
        {
            int clearCorners = CountCornerRays(start, target, false);
            return clearCorners == 0 ? LOSStatus.FullyBlocked : LOSStatus.PartialBlock;
        }
        int blockedRays = CountCornerRays(start, target, true);
        // If more than 25% of corner rays are blocked, consider it partially blocked
        return (blockedRays / 16f > 0.25f) ? LOSStatus.PartialBlock : LOSStatus.Clear;
    }

    // counts how many corner rays are blocked or clear based on the countBlocked parameter
    private int CountCornerRays(Vector3Int start, Vector3Int target, bool countBlocked)
    {
        int count = 0;
        foreach (var startOffset in TileCornerOffsets)
        {
            foreach (var endOffset in TileCornerOffsets)
            {
                bool blocked = IsRayBlocked(start, target, startOffset, endOffset);
                if (blocked == countBlocked)
                {
                    count++;
                }
            }
        }
        return count;
    }

    // checks to see if there's a clear path between two points on grid 
    // starting point is where player is, ending point is the tile being checked for line of sight
    // offsets allow for checks of tile corners 
    private bool IsRayBlocked(Vector3Int start, Vector3Int end, Vector2 startOffset, Vector2 endOffset)
    {
        // calculate ray start and end positions in world space
        float rayStartX = start.x + 0.5f + startOffset.x;
        float rayStartZ = start.z + 0.5f + startOffset.y;
        float rayEndX = end.x + 0.5f + endOffset.x;
        float rayEndZ = end.z + 0.5f + endOffset.y;

        // calculate ray direction and distance
        // e.x start is (2,3) and end is (5,7) then direction is (3,4) and distance is 5
        float directionX = rayEndX - rayStartX;
        float directionZ = rayEndZ - rayStartZ;
        float rayDistance = Mathf.Sqrt(directionX * directionX + directionZ * directionZ);

        if (rayDistance < 0.01f)
            return false;

        // normalize direction vector
        // creates unit vector, allowing consistent stepping along ray regardless of distance
        directionX /= rayDistance;
        directionZ /= rayDistance;

        // Sample 4 points per cell for accuracy
        int sampleCount = Mathf.CeilToInt(rayDistance * 4);
        // keep track of visited cells
        HashSet<Vector3Int> visitedCells = new HashSet<Vector3Int>(sampleCount);

        // Step along ray, skipping start (i=0) and end (i=sampleCount)
        for (int i = 1; i < sampleCount; i++)
        {
            float normalizedDistance = (float)i / sampleCount;
            float sampleX = rayStartX + directionX * rayDistance * normalizedDistance;
            float sampleZ = rayStartZ + directionZ * rayDistance * normalizedDistance;
            Vector3Int currentCell = new Vector3Int(
                Mathf.FloorToInt(sampleX),
                start.y,
                Mathf.FloorToInt(sampleZ)
            );

            // Skip if already checked, or if it's start/end cell
            if (!visitedCells.Add(currentCell) || currentCell == start || currentCell == end)
                continue;
            // if cell out of bounds or not walkable, ray blocked
            if (!IsWithinBounds(currentCell) || !grid.IsCellWalkable(currentCell))
                return true;
        }

        return false;
    }

    // creates highlights for attacked tiles
    private void CreateAttackHighlightsWithLOSStatus(Vector3Int startCell, HashSet<Vector3Int> tileSet)
    {
        int clearCount = 0;
        int partialCount = 0;
        int blockedCount = 0;

        foreach (var cell in tileSet)
        {
            if (cell.Equals(startCell))
                continue;

            LOSStatus status = GetLOSStatus(startCell, cell);
            (Color color, string statusText) = GetLOSColorAndText(status, ref clearCount, ref partialCount, ref blockedCount);
            Debug.Log($"Tile {cell}: {statusText}");
            CreateHighlight(cell, color, $"AttackHighlight_{cell.x}_{cell.z}_{statusText}");
        }

        Debug.Log($"Clear: {clearCount}, Partial: {partialCount}, Blocked: {blockedCount}");
    }

    private (Color color, string text) GetLOSColorAndText(LOSStatus status, ref int clearCount, ref int partialCount, ref int blockedCount)
    {
        switch (status)
        {
            case LOSStatus.Clear:
                clearCount++;
                return (clearLOSColor, "CLEAR");
            case LOSStatus.PartialBlock:
                partialCount++;
                return (partialLOSColor, "PARTIAL");
            case LOSStatus.FullyBlocked:
                blockedCount++;
                return (blockedLOSColor, "BLOCKED");
            default:
                return (highlightColor, "UNKNOWN");
        }
    }

    private HashSet<Vector3Int> CalculateCircle(Vector3Int start, int maxRange)
    {
        HashSet<Vector3Int> circleTiles = new HashSet<Vector3Int>();
        float effectiveRange = maxRange + 0.5f;

        for (int x = -maxRange; x <= maxRange; x++)
        {
            for (int z = -maxRange; z <= maxRange; z++)
            {
                Vector3Int candidate = new Vector3Int(start.x + x, start.y, start.z + z);
                float distance = Mathf.Sqrt(x * x + z * z);

                // Only include tiles within the effective range, that are walkable, and not the starting cell
                if (distance <= effectiveRange &&
                    IsWithinBounds(candidate) &&
                    (x != 0 || z != 0) &&
                    grid.IsCellWalkable(candidate))
                {
                    circleTiles.Add(candidate);
                }
            }
        }

        return circleTiles;
    }

    // if max range is zero or negative, we treat it as unlimited and return all walkable tiles
    private HashSet<Vector3Int> GetAllWalkableTiles()
    {
        HashSet<Vector3Int> allTiles = new HashSet<Vector3Int>();
        for (int x = 0; x < grid.Width; x++)
        {
            for (int z = 0; z < grid.Height; z++)
            {
                Vector3Int cell = new Vector3Int(x, 0, z);
                if (grid.IsCellWalkable(cell))
                {
                    allTiles.Add(cell);
                }
            }
        }
        return allTiles;
    }

    // gets neighboring cells in cardinal directions, and optionally diagonal directions if enabled
    private IEnumerable<Vector3Int> GetNeighbors(Vector3Int cell)
    {
        // Yield all cardinal neighbors
        foreach (var dir in CardinalDirections)
            yield return cell + dir;

        // Yield valid diagonal neighbors if enabled
        if (allowDiagonalMovement)
        {
            foreach (var dir in DiagonalDirections)
            {
                if (IsDiagonalMovementValid(cell, dir))
                    yield return cell + dir;
            }
        }
    }

    // for diagonal movement, both adjacent cardinal tiles must be walkable to prevent corner cutting
    private bool IsDiagonalMovementValid(Vector3Int cell, Vector3Int direction)
    {
        Vector3Int adjacent1 = cell + new Vector3Int(direction.x, 0, 0);
        Vector3Int adjacent2 = cell + new Vector3Int(0, 0, direction.z);

        return IsWithinBounds(adjacent1) && grid.IsCellWalkable(adjacent1) &&
               IsWithinBounds(adjacent2) && grid.IsCellWalkable(adjacent2);
    }

    // checks if a cell is within the grid bounds to prevent out-of-range errors
    private bool IsWithinBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < grid.Width &&
               cell.z >= 0 && cell.z < grid.Height;
    }

    // calculates movement cost between two adjacent cells, with diagonal movement costing more if enabled
    private float CalculateMovementCost(Vector3Int from, Vector3Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dz = Mathf.Abs(to.z - from.z);

        return (dx == 1 && dz == 1) ? diagonalCost : 1f;
    }

    // creates highlight objects for each reachable tile, skipping the starting cell
    private void CreateHighlightVisuals(Vector3Int startCell, HashSet<Vector3Int> tileSet)
    {
        foreach (var cell in tileSet)
        {
            if (!cell.Equals(startCell))
            {
                CreateHighlight(cell, highlightColor, $"RangeHighlight_{cell.x}_{cell.z}");
            }
        }
    }

    // creates a highlight object at the specified cell with the given color and name
    private void CreateHighlight(Vector3Int cell, Color color, string name)
    {
        GameObject highlight = Object.Instantiate(highlightPrefab);
        highlight.name = name;
        highlight.transform.position = gridToWorld(cell.x, cell.z, highlightHeightOffset);
        highlight.transform.localScale = new Vector3(grid.CellSize * 0.1f, 1f, grid.CellSize * 0.1f);

        var renderer = highlight.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material mat = new Material(renderer.sharedMaterial);
            mat.color = color;
            renderer.material = mat;
        }

        activeHighlights.Add(highlight);
    }

    public void Dispose()
    {
        ClearHighlights();
    }
}