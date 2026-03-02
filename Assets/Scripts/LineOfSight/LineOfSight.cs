using System;
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
        return IsRayBlocked(start, end, Vector2.zero, Vector2.zero, -1);
    }

    public bool IsBlockedWithRange(Vector3Int start, Vector3Int end, int range)
    {
        // Check if target is within range first
        if (range > 0 && !IsWithinRange(start, end, range))
        {
            return true; // Out of range = blocked
        }
        return IsRayBlocked(start, end, Vector2.zero, Vector2.zero, range);
    }

    public LOSStatus GetStatus(Vector3Int start, Vector3Int target, out int clearRays)
    {
        clearRays = CountClearCornerRays(start, target, -1);
        if (clearRays == 0)
            return LOSStatus.FullyBlocked;
        else if (clearRays == 16)
            return LOSStatus.Clear;
        else
            return LOSStatus.PartialBlock;
    }

    public LOSStatus GetStatusWithRange(Vector3Int start, Vector3Int target, int range, out int clearRays)
    {
        // Check if target is within range first
        if (range > 0 && !IsWithinRange(start, target, range))
        {
            clearRays = 0;
            return LOSStatus.FullyBlocked;
        }

        clearRays = CountClearCornerRays(start, target, range);
        if (clearRays == 0)
            return LOSStatus.FullyBlocked;
        else if (clearRays == 16)
            return LOSStatus.Clear;
        else
            return LOSStatus.PartialBlock;
    }

    private int CountClearCornerRays(Vector3Int start, Vector3Int target, int range)
    {
        // Check range constraint first
        if (range > 0 && !IsWithinRange(start, target, range))
        {
            return 0; // Out of range = no clear rays
        }

        int clearCount = 0;
        foreach (var sourceCorner in CornerOffsets)
        {
            foreach (var targetCorner in CornerOffsets)
            {
                if (!IsRayBlocked(start, target, sourceCorner, targetCorner, range))
                {
                    clearCount++;
                }
            }
        }
        return clearCount;
    }

    public int CountVisibleCorners(Vector3Int start, Vector3Int target, int range)
    {
        // Check range constraint first
        if (range > 0 && !IsWithinRange(start, target, range))
        {
            return 0; // Out of range = no visible corners
        }

        int visibleCorners = 0;
        foreach (var targetCorner in CornerOffsets)
        {
            bool cornerVisible = false;
            foreach (var sourceCorner in CornerOffsets)
            {
                if (!IsRayBlocked(start, target, sourceCorner, targetCorner, range))
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

    private bool IsRayBlocked(Vector3Int start, Vector3Int end, Vector2 startOffset, Vector2 endOffset, int range)
    {
        Vector2 rayStart = new Vector2(start.x + 0.5f + startOffset.x, start.z + 0.5f + startOffset.y);
        Vector2 rayEnd = new Vector2(end.x + 0.5f + endOffset.x, end.z + 0.5f + endOffset.y);
        Vector2 direction = rayEnd - rayStart;
        float rayDistance = direction.magnitude;

        if (rayDistance < MIN_RAY_DISTANCE)
            return false;

        // If range is specified and positive, check if the distance exceeds range
        if (range > 0 && rayDistance > range + 0.5f) // Add 0.5f tolerance for diagonal movement
        {
            return true; // Out of range = blocked
        }

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

    private bool IsWithinRange(Vector3Int start, Vector3Int end, int range)
    {
        if (range <= 0)
            return true; // No range restriction

        // Calculate Euclidean distance for more accurate range checking
        float distance = Vector3.Distance(
            new Vector3(start.x, start.y, start.z),
            new Vector3(end.x, end.y, end.z));

        return distance <= range + 0.5f; // Add tolerance for edge cases
    }

    private bool IsWithinBounds(Vector3Int cell)
    {
        return cell.x >= 0 && cell.x < grid.Width &&
               cell.z >= 0 && cell.z < grid.Height;
    }

}