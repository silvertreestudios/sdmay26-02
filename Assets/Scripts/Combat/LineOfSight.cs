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
    public static readonly Color ClearColor = new Color(0f, 1f, 0f, 0.5f);      // Green with 50% alpha
    public static readonly Color PartialColor = new Color(1f, 1f, 0f, 0.5f);    // Yellow with 50% alpha
    public static readonly Color BlockedColor = new Color(1f, 0f, 0f, 0.5f);    // Red with 50% alpha

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
    /// Gets the color associated with a line of sight status.
    /// </summary>
    public static Color GetStatusColor(Status status)
    {
        return status switch
        {
            Status.Clear => ClearColor,
            Status.PartialBlock => PartialColor,
            Status.FullyBlocked => BlockedColor,
            _ => Color.white
        };
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

    // Uses raycasting to determine if there's a clear line of sight between start and end point.
    private bool IsRayBlocked(Vector3Int start, Vector3Int end, Vector2 startOffset, Vector2 endOffset)
    {
        // calculate ray start and end positions in world space, applying offsets to check corners
        Vector2 rayStart2D = new Vector2(start.x + 0.5f + startOffset.x, start.z + 0.5f + startOffset.y);
        Vector2 rayEnd2D = new Vector2(end.x + 0.5f + endOffset.x, end.z + 0.5f + endOffset.y);
        Vector2 direction2D = rayEnd2D - rayStart2D;
        float rayDistance2D = direction2D.magnitude;
        
        if (rayDistance2D < MIN_RAY_DISTANCE)
            return false;

        // convert to 3D for Unity Physics raycast
        float rayHeight = grid.GridY + 0.5f;
        Vector3 rayStart3D = new Vector3(rayStart2D.x, rayHeight, rayStart2D.y);
        Vector3 rayEnd3D = new Vector3(rayEnd2D.x, rayHeight, rayEnd2D.y);
        Vector3 direction3D = (rayEnd3D - rayStart3D).normalized;
        float distance3D = Vector3.Distance(rayStart3D, rayEnd3D);
        
        // use small offset to avoid hitting the source character's collider
        const float startOffset3D = 0.1f;
        // raycast in 3D to check for any solid obstacles along the path
        if (Physics.Raycast(rayStart3D + direction3D * startOffset3D, direction3D, distance3D - startOffset3D))
            return true; 

        // check grid walkability along the ray path
        direction2D.Normalize();
        int sampleCount = Mathf.CeilToInt(rayDistance2D * SAMPLES_PER_TILE);
        HashSet<Vector3Int> visitedCells = new HashSet<Vector3Int>(sampleCount);

        for (int i = 1; i < sampleCount; i++)
        {
            // sample points along ray at regular intervals, convert to grid coord and check walkability
            Vector2 samplePos = rayStart2D + direction2D * rayDistance2D * ((float)i / sampleCount);
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