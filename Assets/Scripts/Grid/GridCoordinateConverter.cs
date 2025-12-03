using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Utility class for converting between different coordinate systems in a 3D grid.
/// Handles conversions between screen space, world space, and grid cell coordinates.
/// </summary>
public class GridCoordinateConverter
{
    private readonly GridRenderer3D grid;
    private readonly Dictionary<GameObject, Vector3Int> cachedCharacterCells;
    private readonly Dictionary<GameObject, Vector3> cachedCharacterPositions;

    /// <summary>
    /// Creates a new GridCoordinateConverter for the specified grid
    /// </summary>
    /// <param name="gridReference">The grid to perform conversions for</param>
    public GridCoordinateConverter(GridRenderer3D gridReference)
    {
        grid = gridReference;
        cachedCharacterCells = new Dictionary<GameObject, Vector3Int>();
        cachedCharacterPositions = new Dictionary<GameObject, Vector3>();
    }

    /// <summary>
    /// Converts a grid cell coordinate to world position at the center of the cell
    /// </summary>
    /// <param name="x">Grid X coordinate</param>
    /// <param name="z">Grid Z coordinate</param>
    /// <param name="yOffset">Optional Y offset from grid height</param>
    /// <returns>World position at the center of the specified cell</returns>
    public Vector3 GridCellCenterWorld(int x, int z, float yOffset = 0f)
    {
        float wx = grid.origin.x + (x + 0.5f) * grid.cellSize;
        float wz = grid.origin.z + (z + 0.5f) * grid.cellSize;
        return new Vector3(wx, grid.gridY + yOffset, wz);
    }

    /// <summary>
    /// Gets the grid cell position of a character GameObject with caching
    /// </summary>
    /// <param name="character">The character GameObject</param>
    /// <returns>Grid cell coordinate</returns>
    public Vector3Int GetCharacterCell(GameObject character)
    {
        if (cachedCharacterPositions.TryGetValue(character, out Vector3 lastPos)
            && lastPos == character.transform.position)
        {
            return cachedCharacterCells[character];
        }

        TryGridWorldToCell(character.transform.position, out Vector3Int cell, clamp: true);
        cachedCharacterCells[character] = cell;
        cachedCharacterPositions[character] = character.transform.position;
        return cell;
    }

    /// <summary>
    /// Converts a screen position to a world position on the XZ plane
    /// </summary>
    /// <param name="cam">Camera to use for conversion</param>
    /// <param name="screenPos">Screen position</param>
    /// <param name="planeY">Y coordinate of the XZ plane</param>
    /// <param name="hit">Output world position where ray hits the plane</param>
    /// <returns>True if the ray hits the plane, false otherwise</returns>
    public bool ScreenToXZPlane(Camera cam, Vector2 screenPos, float planeY, out Vector3 hit)
    {
        var ray = cam.ScreenPointToRay(screenPos);
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float t))
        {
            hit = ray.GetPoint(t);
            return true;
        }
        hit = default;
        return false;
    }

    /// <summary>
    /// Converts a world position to a grid cell coordinate
    /// </summary>
    /// <param name="world">World position</param>
    /// <param name="cell">Output grid cell coordinate</param>
    /// <param name="clamp">Whether to clamp the result to grid bounds</param>
    /// <returns>True if the cell is within bounds (or clamp is true), false otherwise</returns>
    public bool TryGridWorldToCell(Vector3 world, out Vector3Int cell, bool clamp = false)
    {
        int cx = Mathf.FloorToInt((world.x - grid.origin.x) / grid.cellSize);
        int cz = Mathf.FloorToInt((world.z - grid.origin.z) / grid.cellSize);

        if (clamp)
        {
            cx = Mathf.Clamp(cx, 0, grid.width - 1);
            cz = Mathf.Clamp(cz, 0, grid.height - 1);
            cell = new Vector3Int(cx, 0, cz);
            return true;
        }

        cell = new Vector3Int(cx, 0, cz);
        return (uint)cx < (uint)grid.width && (uint)cz < (uint)grid.height;
    }

    /// <summary>
    /// Clears the character position cache for a specific character
    /// </summary>
    /// <param name="character">Character to clear cache for</param>
    public void ClearCharacterCache(GameObject character)
    {
        cachedCharacterCells.Remove(character);
        cachedCharacterPositions.Remove(character);
    }

    /// <summary>
    /// Clears all cached character positions
    /// </summary>
    public void ClearAllCaches()
    {
        cachedCharacterCells.Clear();
        cachedCharacterPositions.Clear();
    }
}