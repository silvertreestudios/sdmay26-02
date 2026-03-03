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
    
    // Line-of-sight class
    private readonly LineOfSight lineOfSight;

    // directions for cardinal and diagonal movement, used for neighbor checks
    private static readonly Vector3Int[] CardinalDirections = new[]
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

    // diagonal directions for movement, only used if diagonal movement is allowed
    private static readonly Vector3Int[] DiagonalDirections = new[]
    {
        new Vector3Int(1, 0, 1),
        new Vector3Int(-1, 0, 1),
        new Vector3Int(1, 0, -1),
        new Vector3Int(-1, 0, -1)
    };

    // scaling factor for highlight size relative to grid cell size, can be adjusted for better visuals
    private const float HIGHLIGHT_SCALE_FACTOR = 0.1f;

    /// <summary>
    /// Creates a movement range system for a player.
    /// TODO: eventually needs to be expanded for use by mindless AI
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
        lineOfSight = new LineOfSight(grid);
    }

    // checks if a specific cell is currently highlighted as reachable, used for validating player input
    public bool IsCellReachable(Vector3Int cell) => currentReachableTiles.Contains(cell);

    // main method to update highlights based on current position and range,
    // can show either movement range or attack range with line of sight info
    public void UpdateHighlights(Vector3Int startCell, int maxRange, bool showAttackRange = false)
    {
        ClearHighlights();
        
        if (highlightPrefab == null)
            return;

        // if showing attack range, calculate attackable tiles and create highlights with LOS status colors
        if (showAttackRange)
        {
            HashSet<Vector3Int> attackedTiles = CalculateEmination(startCell, maxRange);
            CreateAttackHighlightsWithLOSStatus(startCell, attackedTiles);
        }
        // otherwise calculate movement range and create standard highlights
        else
        {
            currentReachableTiles = CalculateReachableTiles(startCell, maxRange);
            CreateHighlightVisuals(startCell, currentReachableTiles);
        }
    }

    // removes all existing highlight objects and clears the list of active highlights
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
    public HashSet<Vector3Int> CalculateReachableTiles(Vector3Int start, int maxRange)
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

    // Calculates attackable tiles within weapon range
    public HashSet<Vector3Int> CalculateEmination(Vector3Int start, int weaponRange)
    {
        HashSet<Vector3Int> rangeTiles = new HashSet<Vector3Int>();

        // Range of 1 represents melee attack - only adjacent tiles
        if (weaponRange == 1)
        {
            // Include all adjacent tiles (cardinal directions)
            foreach (var dir in CardinalDirections)
            {
                Vector3Int adjacent = start + dir;
                if (IsWithinBounds(adjacent) && grid.IsCellWalkable(adjacent))
                {
                    rangeTiles.Add(adjacent);
                }
            }

            // Add diagonal neighbors if diagonal movement is allowed
            if (allowDiagonalMovement)
            {
                foreach (var dir in DiagonalDirections)
                {
                    Vector3Int adjacent = start + dir;
                    if (IsWithinBounds(adjacent) && grid.IsCellWalkable(adjacent))
                    {
                        rangeTiles.Add(adjacent);
                    }
                }
            }
        }
        else
        { 
            float maxRangeSquared = weaponRange * weaponRange;
            // iterate over square area around the start cell, but only include tiles within the circular radius
            for (int x = -weaponRange; x <= weaponRange; x++)
            {
                for (int z = -weaponRange; z <= weaponRange; z++)
                {
                    // Skip the starting cell
                    if (x == 0 && z == 0)
                        continue;

                    Vector3Int candidate = new Vector3Int(start.x + x, 0, start.z + z);

                    // Check bounds first
                    if (!IsWithinBounds(candidate))
                        continue;

                    float distanceSquared = x * x + z * z;
                    if (distanceSquared <= maxRangeSquared && grid.IsCellWalkable(candidate))
                    {
                        rangeTiles.Add(candidate);
                    }
                }
            }
        }
        return rangeTiles;
    }

    // creates highlights for attacked tiles
    private void CreateAttackHighlightsWithLOSStatus(Vector3Int startCell, HashSet<Vector3Int> tileSet)
    {
        // start counters at zero for each LOS category
        int clearCount = 0;
        int partialCount = 0;
        int blockedCount = 0;
        
        foreach (var cell in tileSet)
        {
            if (cell.Equals(startCell))
                continue;

            // determine line of sight status for this tile and get corresponding color and text
            LineOfSight.Status status = lineOfSight.GetStatus(startCell, cell, out int clearRays);
            int visibleCorners = 0;

            // only calculate visible corners if tile not fully blocked
            if (status != LineOfSight.Status.FullyBlocked)
            {
                visibleCorners = lineOfSight.CountVisibleCorners(startCell, cell);
            }

            // get color from LineOfSight based on status
            Color color = LineOfSight.GetStatusColor(status);
            string statusText;

            // if line of sight is fully blocked, increment blocked count
            if (status == LineOfSight.Status.FullyBlocked)
            {
                blockedCount++;
                continue;
            }
            // if line of sight is clear, increment clear count
            else if (status == LineOfSight.Status.Clear)
            {
                clearCount++;
                statusText = "CLEAR";
            }
            // if line of sight is partially blocked, increment partial count
            else if (status == LineOfSight.Status.PartialBlock)
            {
                partialCount++;
                statusText = "PARTIAL";
            }
            else
            {
                color = highlightColor;
                statusText = "UNKNOWN";
            }

            // calculate distance from start cell to this cell
            float distance = CalculateDistance(startCell, cell);
            // display cell coordinates, LOS status, visible corners, and distance
            Debug.Log($"Tile {cell}: {statusText} - Corners Visible: {visibleCorners} - Distance: {distance} tiles");
            CreateHighlight(cell, color, $"AttackHighlight_{cell.x}_{cell.z}_{statusText}");
        }
        Debug.Log($"Clear: {clearCount}, Partial: {partialCount}, Blocked: {blockedCount}");
    }

    // calculate distance between tiles within line of sight
    // outputs horizontal distance
    private int CalculateDistance(Vector3Int from, Vector3Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dz = Mathf.Abs(to.z - from.z);
        return Mathf.Max(dx, dz);
    }

    // if max range is zero or negative, treat as unlimited and return all walkable tiles
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

    // for diagonal movement, both adjacent tiles must be walkable to prevent corner cutting
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
}