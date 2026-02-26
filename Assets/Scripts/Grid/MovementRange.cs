using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Manages visual highlights for reachable tiles within movement range.
/// </summary>
public class MovementRange
{
    // reference to grid memory for walkability checks and dimensions
    private readonly IGridMemory grid;
    // configuration options for movement and highlights
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

    /// <summary>
    /// Represents the degree of cover
    /// </summary>
    public enum CoverDegree
    {
        None,        // No cover
        Lesser,      // +1 circumstance bonus to AC
        Standard,    // +2 circumstance bonus to AC
        Greater      // +4 circumstance bonus to AC
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
    private LOSStatus GetLOSStatus(Vector3Int start, Vector3Int target, out int visibleCorners)
    {
        // Count all 16 corner-to-corner rays
        int clearRays = CountCornerRays(start, target, false);
        visibleCorners = clearRays;
       
        // Even 1 clear ray means you have line of sight
        if (clearRays == 0)
            return LOSStatus.FullyBlocked; // No LOS
        else if (clearRays >= 15) // Almost all or all rays clear
            return LOSStatus.Clear; // Clear LOS, minimal to no cover
        else
            return LOSStatus.PartialBlock; // Has LOS but with cover (Lesser/Standard/Greater depending on rays blocked)
    }

    // Calculates cover degree
    private CoverDegree CalculateCoverDegree(int clearRays)
    {
        float percentBlocked = (16 - clearRays) / 16f;
        if (percentBlocked >= 0.75f)
            return CoverDegree.Greater; // +4 AC
        else if (percentBlocked >= 0.50f)
            return CoverDegree.Standard; // +2 AC
        else if (percentBlocked >= 0.25f)
            return CoverDegree.Lesser; // +1 circumstance bonus to AC
        else
            return CoverDegree.None;
    }

    // counts how many of the 4 target tile corners are visible from the center of the start tile
    private int CountVisibleTargetCorners(Vector3Int start, Vector3Int target)
    {
        int visibleCount = 0;
        foreach (var cornerOffset in TileCornerOffsets)
        {
            // Check ray from center of start tile to each corner of target tile
            bool blocked = IsRayBlocked(start, target, Vector2.zero, cornerOffset);
            if (!blocked)
            {
                visibleCount++;
            }
        }
        return visibleCount;
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

    /// <summary>
    /// uses raycasting to determine if there's a clear line of sight between start and end point
    /// </summary>
    /// <param name="start"></attacking cell (player position)>
    /// <param name="end"></target cell>
    /// <param name="startOffset"></offset from center of start tile>
    /// <param name="endOffset"></offset from center of end tile>
    /// <returns></returns whether or not there's a clear line of sight between start an end points>
    private bool IsRayBlocked(Vector3Int start, Vector3Int end, Vector2 startOffset, Vector2 endOffset)
    {
        // calculate ray start and end positions
        float rayStartX = start.x + 0.5f + startOffset.x;
        float rayStartZ = start.z + 0.5f + startOffset.y;
        float rayEndX = end.x + 0.5f + endOffset.x;
        float rayEndZ = end.z + 0.5f + endOffset.y;

        // vector from start to end point
        float directionX = rayEndX - rayStartX;
        float directionZ = rayEndZ - rayStartZ;
        float rayDistance = Mathf.Sqrt(directionX * directionX + directionZ * directionZ);
        // if start and end are the same or very close, return not blocked
        if (rayDistance < 0.01f)
            return false;

        // normalize direction to unit vector
        // allows consistent step increments regardless of distance
        (directionX, directionZ) = (directionX / rayDistance, directionZ / rayDistance);

        // Sample 4 points per tile for corners
        int sampleCount = Mathf.CeilToInt(rayDistance * 4);
        // keep track of visited cells
        HashSet<Vector3Int> visitedCells = new HashSet<Vector3Int>(sampleCount);

        // Step along ray, skipping start (i=0) and end (i=sampleCount)
        for (int i = 1; i < sampleCount; i++)
        {
            // 
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

            // determine LOS status for this tile and get corresponding color and text
            LOSStatus status = GetLOSStatus(startCell, cell, out int visibleCorners);
            (Color color, string statusText) = GetLOSColorAndText(status, ref clearCount, ref partialCount, ref blockedCount);
            
            if (status == LOSStatus.PartialBlock)
            {
                float distance = CalculateDistance(startCell, cell);
                CoverDegree cover = CalculateCoverDegree(visibleCorners);
                string coverText = GetCoverText(cover);
                Debug.Log($"Tile {cell}: {statusText} - Visible Rays: {visibleCorners}/16 - Distance: {distance:F2} - Cover: {coverText}");
            }
            else if (status == LOSStatus.Clear)
            {
                Debug.Log($"Tile {cell}: {statusText} - No Cover");
            }
            else
            {
                Debug.Log($"Tile {cell}: {statusText} - No LOS");
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

    /// <summary>
    /// Checks if there's line of sight between two cells and returns cover information
    /// </summary>
    public bool HasLineOfSight(Vector3Int from, Vector3Int to, out CoverDegree coverDegree)
    {
        LOSStatus status = GetLOSStatus(from, to, out int visibleCorners);
        coverDegree = CalculateCoverDegree(visibleCorners);
        
        // even partial block means you have LOS (just with cover)
        return status != LOSStatus.FullyBlocked;
    }
}