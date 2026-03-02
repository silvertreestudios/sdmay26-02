using System.Collections.Generic;
using UnityEngine;

public class LineOfSight
{
    private readonly IGridMemory grid;

    private const int SAMPLES_PER_TILE = 4;
    private const float MIN_RAY_DISTANCE = 0.01f;
    private const float CORNER_OFFSET = 0.4f;

    private static readonly Vector2[] CornerOffsets = new[]
    {
        new Vector2(-CORNER_OFFSET, -CORNER_OFFSET),
        new Vector2(CORNER_OFFSET, -CORNER_OFFSET),
        new Vector2(-CORNER_OFFSET, CORNER_OFFSET),
        new Vector2(CORNER_OFFSET, CORNER_OFFSET)
    };

    public enum LOSStatus
    {
        Clear,
        PartialBlock,
        FullyBlocked
    }

    public LineOfSight(IGridMemory gridReference)
    {
        if (gridReference == null)
        {
            Debug.LogError("[LineOfSightCalculator] Grid reference cannot be null!");
        }
        this.grid = gridReference;
    }

    public bool IsBlocked(Vector3Int start, Vector3Int end)
    {
        return IsRayBlocked(start, end, Vector2.zero, Vector2.zero);
    }

    public LOSStatus GetStatus(Vector3Int start, Vector3Int target, out int clearRays)
    {
        clearRays = CountClearCornerRays(start, target);
        if (clearRays == 0)
            return LOSStatus.FullyBlocked;
        else if (clearRays == 16)
            return LOSStatus.Clear;
        else
            return LOSStatus.PartialBlock;
    }

    public int CountClearCornerRays(Vector3Int start, Vector3Int target)
    {
        int clearCount = 0;

        foreach (var sourceCorner in CornerOffsets)
        {
            foreach (var targetCorner in CornerOffsets)
            {
                if (!IsRayBlocked(start, target, sourceCorner, targetCorner))
                {
                    clearCount++;
                }
            }
        }

        return clearCount;
    }

    public int CountVisibleCorners(Vector3Int start, Vector3Int target)
    {
        int visibleCorners = 0;

        foreach (var targetCorner in CornerOffsets)
        {
            bool cornerVisible = false;

            foreach (var sourceCorner in CornerOffsets)
            {
                if (!IsRayBlocked(start, target, sourceCorner, targetCorner))
                {
                    cornerVisible = true;
                    break;
                }
            }

            if (cornerVisible)
            {
                visibleCorners++;
            }
        }

        return visibleCorners;
    }

    private bool IsRayBlocked(Vector3Int start, Vector3Int end, Vector2 startOffset, Vector2 endOffset)
    {
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
            Vector3Int currentCell = new Vector3Int(
                Mathf.FloorToInt(samplePos.x),
                start.y,
                Mathf.FloorToInt(samplePos.y));

            if (!visitedCells.Add(currentCell) || currentCell == start || currentCell == end)
                continue;

            if (!IsWithinBounds(currentCell) || !grid.IsCellWalkable(currentCell))
                return true;
        }

        return false;
    }

    private bool IsWithinBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < grid.Width &&
               cell.z >= 0 && cell.z < grid.Height;
    }
}