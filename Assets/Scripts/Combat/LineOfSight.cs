using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles line-of-sight calculations for grid-based combat.
/// </summary>
public class LineOfSight
{
    private readonly IGridMemory grid;

    // offsets for raycasting to tile corners, used in LOS checks
    private const float CORNER_OFFSET = 0.4f;
    private const int SAMPLES_PER_TILE = 4;
    private const float MIN_RAY_DISTANCE = 0.01f;

    // LOS status colors for visual feedback
    public static readonly Color ClearColor = new Color(0f, 1f, 0f, 0.5f);   
    public static readonly Color PartialColor = new Color(1f, 1f, 0f, 0.5f); 
    public static readonly Color BlockedColor = new Color(1f, 0f, 0f, 0.5f);    

    // predefined offsets for the 4 corners of a tile, used to check LOS from multiple points within the tile
    private static readonly Vector2[] TileCornerOffsets = new[]
    {
        new Vector2(-CORNER_OFFSET, -CORNER_OFFSET),
        new Vector2(CORNER_OFFSET, -CORNER_OFFSET),
        new Vector2(-CORNER_OFFSET, CORNER_OFFSET),
        new Vector2(CORNER_OFFSET, CORNER_OFFSET)
    };

    // line of sight status categories based on how many rays are clear
    public enum Status
    {
        Clear,
        PartialBlock,
        FullyBlocked
    }

    /// Creates a line-of-sight calculator
    public LineOfSight(IGridMemory gridMemory)
    {
        grid = gridMemory;
    }

    // determines line of sight status between two cells
    public Status GetStatus(Vector3Int start, Vector3Int target, out int clearRays)
    {
        clearRays = CountCornerToCornerRays(start, target);
        if (clearRays == 0)
            return Status.FullyBlocked;
        else if (clearRays == 16)
            return Status.Clear;
        else
            return Status.PartialBlock;
    }

    /// <summary>
    /// Gets color associated with a line of sight status.
    /// </summary>
    public static Color GetStatusColor(Status status)
    {
        switch (status)
        {
            case Status.Clear:
                return ClearColor;
            case Status.PartialBlock:
                return PartialColor;
            case Status.FullyBlocked:
                return BlockedColor;
            default:
                return Color.white;
        }
    }

    // counts how many of the 16 corner-to-corner rays are clear.
    public int CountCornerToCornerRays(Vector3Int start, Vector3Int target)
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

    // Counts how many of the 4 target corners are visible from at least one source corner.
    public int CountVisibleCorners(Vector3Int start, Vector3Int target)
    {
        int visibleCorners = 0;
        // for each target corner, check if at least one ray from any source corner is clear
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

    // check if a single ray from one corner to another is blocked 
    // uses both physics raycast for obstacles and grid walkability checks along path
    private bool IsRayBlocked(Vector3Int start, Vector3Int end, Vector2 startOffset, Vector2 endOffset)
    {
        // x and y positions of start and end corner
        Vector2 rayStart2D = new Vector2(start.x + 0.5f + startOffset.x, start.z + 0.5f + startOffset.y);
        Vector2 rayEnd2D = new Vector2(end.x + 0.5f + endOffset.x, end.z + 0.5f + endOffset.y);
        
        Vector2 direction2D = rayEnd2D - rayStart2D;
        // how far apart start and end corners are in 2D space
        float rayDistance2D = direction2D.magnitude;
        
        // if distance is small, assume unblocked
        if (rayDistance2D < MIN_RAY_DISTANCE)
            return false;

        // get height to cast ray at
        float rayHeight = grid.GridY + 0.5f;
        // convert start and end points to 3D for raycasting
        Vector3 rayStart3D = new Vector3(rayStart2D.x, rayHeight, rayStart2D.y);
        Vector3 rayEnd3D = new Vector3(rayEnd2D.x, rayHeight, rayEnd2D.y);

        // get 3D direction and distance
        Vector3 direction3D = (rayEnd3D - rayStart3D).normalized;
        float distance3D = Vector3.Distance(rayStart3D, rayEnd3D);

        // move ray start slightly forward
        const float startOffset3D = 0.1f;
        // fire a ray to see if it hits any object
        if (Physics.Raycast(rayStart3D + direction3D * startOffset3D, direction3D, distance3D - startOffset3D))
            return true; 

        // check grid walkability along the ray path
        direction2D.Normalize();
        // how many spots along the ray to check
        int sampleCount = Mathf.CeilToInt(rayDistance2D * SAMPLES_PER_TILE);
        // track tiles already checked
        HashSet<Vector3Int> visitedCells = new HashSet<Vector3Int>(sampleCount);

        for (int i = 1; i < sampleCount; i++)
        {
            // sample points along ray at regular intervals, convert to grid coord and check walkability
            Vector2 samplePos = rayStart2D + direction2D * rayDistance2D * ((float)i / sampleCount);
            // which tile on the grid this position is at 
            Vector3Int currentCell = new Vector3Int(Mathf.FloorToInt(samplePos.x), start.y, Mathf.FloorToInt(samplePos.y));

            if (visitedCells.Add(currentCell) && currentCell != start && currentCell != end)
            {
                if (!IsWithinBounds(currentCell) || !grid.IsCellWalkable(currentCell))
                    return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Checks if a cell is within the grid bounds
    /// </summary>
    private bool IsWithinBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < grid.Width &&
               cell.z >= 0 && cell.z < grid.Height;
    }
}