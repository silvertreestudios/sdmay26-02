using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages visual highlights for reachable tiles within movement range.
/// </summary>
public class MovementRange
{
    // reference to grid memory for walkability checks and dimensions
    private readonly IGridMemory grid;
    private readonly bool allowDiagonalMovement;
    private readonly float diagonalCost;
    private readonly GameObject highlightPrefab;
    private readonly Color highlightColor;
    private readonly float highlightHeightOffset;
    private readonly List<GameObject> activeHighlights = new List<GameObject>();
    private HashSet<Vector3Int> currentReachableTiles = new HashSet<Vector3Int>();
    // convert grid coordinates to world position
    private readonly System.Func<int, int, float, Vector3> gridToWorld;

    private readonly Color clearLOSColor = new Color(0f, 1f, 0f, 0.5f);
    private readonly Color partialLOSColor = new Color(1f, 1f, 0f, 0.5f);
    private readonly Color blockedLOSColor = new Color(1f, 0f, 0f, 0.5f);

    // represents the line-of-sight status for a tile
    public enum LOSStatus
    {
        Clear,
        PartialBlock,
        FullyBlocked
    }

    // represents the degree of cover 
    public enum CoverDegree
    {
        None,        // No cover
        Lesser,      // +1 circumstance bonus to armor class
        Standard,    // +2 circumstance bonus to armor class
        Greater      // +4 circumstance bonus to armor class
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
    private const float CORNER_OFFSET = 0.4f;
    private const int SAMPLES_PER_TILE = 4;
    private const float MIN_RAY_DISTANCE = 0.01f;
    private const float HIGHLIGHT_SCALE_FACTOR = 0.1f;

    private static readonly Vector2[] TileCornerOffsets = new[]
    {
        new Vector2(-CORNER_OFFSET, -CORNER_OFFSET),
        new Vector2(CORNER_OFFSET, -CORNER_OFFSET),
        new Vector2(-CORNER_OFFSET, CORNER_OFFSET),
        new Vector2(CORNER_OFFSET, CORNER_OFFSET)
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
        return CalculateCircle(start, maxRange);
    }

    /// <summary>
    /// Determines line of sight status 
    /// </summary>
    private LOSStatus GetLOSStatus(Vector3Int start, Vector3Int target, out int clearRays)
    {
        // Count how many of the 16 corner-to-corner rays are clear
        clearRays = CountCornerToCornerRays(start, target);
        // if ANY corner-to-corner line is clear, you have line of sight
        if (clearRays == 0)
            return LOSStatus.FullyBlocked;
        else if (clearRays == 16)
            return LOSStatus.Clear;
        else
            return LOSStatus.PartialBlock;
    }

    /// <summary>
    /// Calculates cover degree based on how many target corners are visible
    /// </summary>
    private CoverDegree CalculateCoverDegree(int visibleCorners)
    {
        // Calculate percentage of corners that are blocked
        float percentBlocked = (4 - visibleCorners) / 4f;
        if (percentBlocked >= 0.75f)
            return CoverDegree.Greater;
        else if (percentBlocked >= 0.50f)
            return CoverDegree.Standard;
        else if (percentBlocked >= 0.25f)
            return CoverDegree.Lesser;
        else
            return CoverDegree.None;
    }

    /// <summary>
    /// Counts how many of the 16 corner-to-corner rays are clear.
    /// </summary>
    private int CountCornerToCornerRays(Vector3Int start, Vector3Int target)
    {
        int clearCount = 0;
        // check all 16 combinations: 4 source corners × 4 target corners
        foreach (var sourceCorner in TileCornerOffsets)
        {
            foreach (var targetCorner in TileCornerOffsets)
            {
                if (!IsRayBlocked(start, target, sourceCorner, targetCorner))
                    clearCount++;
            }
        }
        return clearCount;
    }

    /// <summary>
    /// Counts how many of the 4 target corners are visible from at least one source corner.
    /// </summary>
    private int CountVisibleCorners(Vector3Int start, Vector3Int target)
    {
        int visibleCorners = 0;
        
        // For each target corner, check if it's visible from any source corner
        foreach (var targetCorner in TileCornerOffsets)
        {
            bool cornerVisible = false;
            foreach (var sourceCorner in TileCornerOffsets)
            {
                if (!IsRayBlocked(start, target, sourceCorner, targetCorner))
                {
                    cornerVisible = true;
                    break; // At least one ray to this corner is clear
                }
            }
            if (cornerVisible)
            {
                visibleCorners++;
            }
        }
        
        return visibleCorners;
    }

    /// <summary>
    /// uses raycasting to determine if there's a clear line of sight between start and end point
    /// </summary>
    /// <param name="start">attacking cell (player position)</param>
    /// <param name="end">target cell</param>
    /// <param name="startOffset">offset from center of start tile</param>
    /// <param name="endOffset">offset from center of end tile</param>
    /// <returns>whether or not there's a clear line of sight between start an end points</returns>
    private bool IsRayBlocked(Vector3Int start, Vector3Int end, Vector2 startOffset, Vector2 endOffset)
    {
        // create a 2D vector 
        Vector2 rayStart = new Vector2(start.x + 0.5f + startOffset.x, start.z + 0.5f + startOffset.y);
        Vector2 rayEnd = new Vector2(end.x + 0.5f + endOffset.x, end.z + 0.5f + endOffset.y);
        Vector2 direction = rayEnd - rayStart;
        float rayDistance = direction.magnitude;

        if (rayDistance < MIN_RAY_DISTANCE)
            return false;
        direction.Normalize();
        int sampleCount = Mathf.CeilToInt(rayDistance * SAMPLES_PER_TILE);
        HashSet<Vector3Int> visitedCells = new HashSet<Vector3Int>(sampleCount);

        for (int i = 1; i < sampleCount; i++)
        {
            Vector2 samplePos = rayStart + direction * rayDistance * ((float)i / sampleCount);
            Vector3Int currentCell = new Vector3Int(Mathf.FloorToInt(samplePos.x), start.y, Mathf.FloorToInt(samplePos.y));
            if (!visitedCells.Add(currentCell) || currentCell == start || currentCell == end)
                continue;
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

            // determine LOS status for this tile and get corresponding color and text
            LOSStatus status = GetLOSStatus(startCell, cell, out int clearRays);
            (Color color, string statusText) = GetLOSColorAndText(status, ref clearCount, ref partialCount, ref blockedCount);

            if (status == LOSStatus.PartialBlock)
            {
                float distance = CalculateDistance(startCell, cell);
                int visibleCorners = CountVisibleCorners(startCell, cell);
                CoverDegree cover = CalculateCoverDegree(visibleCorners);
                string coverText = GetCoverText(cover);
                Debug.Log($"Tile {cell}: {statusText} - Corners Visible: {visibleCorners} - Distance: {distance:F2} - Cover: {coverText}");
            }
            else if (status == LOSStatus.Clear)
            {
                Debug.Log($"Tile {cell}: {statusText} - Corners Visible: 4 - No Cover");
            }
            else
            {
                Debug.Log($"Tile {cell}: {statusText} - Corners Visible: 0 - No Line of Sight");
            }

            CreateHighlight(cell, color, $"AttackHighlight_{cell.x}_{cell.z}_{statusText}");
        }

        Debug.Log($"Clear: {clearCount}, Partial: {partialCount}, Blocked: {blockedCount}");
    }

    // Helper to get cover text for display
    private string GetCoverText(CoverDegree cover)
    {
        switch (cover)
        {
            case CoverDegree.Lesser:
                return "Lesser (+1 AC)";
            case CoverDegree.Standard:
                return "Standard (+2 AC)";
            case CoverDegree.Greater:
                return "Greater (+4 AC)";
            default:
                return "None";
        }
    }

    // calculates the euclidean distance between two cells
    private float CalculateDistance(Vector3Int from, Vector3Int to)
    {
        int dx = to.x - from.x;
        int dz = to.z - from.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
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

    // calculates tiles in a circular area around the start cell, used for attack range
    private HashSet<Vector3Int> CalculateCircle(Vector3Int start, int maxRange)
    {
        HashSet<Vector3Int> circleTiles = new HashSet<Vector3Int>();
        float effectiveRange = maxRange + 0.5f;

        // iterate over square area around the start cell, but only include tiles within the circular radius
        for (int x = -maxRange; x <= maxRange; x++)
        {
            for (int z = -maxRange; z <= maxRange; z++)
            {
                Vector3Int candidate = new Vector3Int(start.x + x, start.y, start.z + z);
                float distance = Mathf.Sqrt(x * x + z * z);

                // only include tiles within range, that are walkable, and not starting cell
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
            // yield used to return neighbors one at a time without needing to create a full list in memory
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
        highlight.transform.localScale = new Vector3(grid.CellSize * HIGHLIGHT_SCALE_FACTOR, 1f, grid.CellSize * HIGHLIGHT_SCALE_FACTOR);

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

    /// <summary>
    /// Checks if there's line of sight between two cells and returns cover information.
    /// </summary>
    public bool HasLineOfSight(Vector3Int from, Vector3Int to, out CoverDegree coverDegree)
    {
        LOSStatus status = GetLOSStatus(from, to, out int clearRays);
        int visibleCorners = CountVisibleCorners(from, to);
        coverDegree = CalculateCoverDegree(visibleCorners);

        // PF2E Rule: even partial block means you have LOS (just with cover)
        return status != LOSStatus.FullyBlocked;
    }
}