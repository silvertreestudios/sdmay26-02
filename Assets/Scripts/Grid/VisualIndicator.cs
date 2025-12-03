using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages visual path preview rendering using Unity's LineRenderer.
/// Handles path display, color changes, and cleanup.
/// </summary>
public class VisualIndicator
{
    // Configuration
    private readonly Material indicatorMaterial;
    private readonly float lineWidth;
    private readonly float heightOffset;
    private readonly Color defaultColor;
    private readonly Color confirmedColor;

    // Runtime state
    private GameObject indicatorObject;
    private LineRenderer lineRenderer;
    private List<Vector3Int> currentPath;
    private bool isActive;

    // Delegate for converting grid cells to world positions
    private readonly System.Func<int, int, float, Vector3> gridToWorld;

    /// <summary>
    /// Gets whether a path is currently being previewed
    /// </summary>
    public bool IsActive => isActive;

    /// <summary>
    /// Gets the currently previewed path (returns null if no path is active)
    /// </summary>
    public List<Vector3Int> CurrentPath => isActive && currentPath != null
        ? new List<Vector3Int>(currentPath)
        : null;

    /// <summary>
    /// Creates a new VisualIndicator
    /// </summary>
    /// <param name="parent">Parent transform for the LineRenderer GameObject</param>
    /// <param name="material">Material for the line (null creates default)</param>
    /// <param name="width">Width of the preview line</param>
    /// <param name="height">Height offset above the ground</param>
    /// <param name="defaultPreviewColor">Color for normal preview</param>
    /// <param name="confirmedPreviewColor">Color for confirmed preview</param>
    /// <param name="gridCellToWorld">Function to convert grid coordinates to world position</param>
    public VisualIndicator(
        Transform parent,
        Material material,
        float width,
        float height,
        Color defaultPreviewColor,
        Color confirmedPreviewColor,
        System.Func<int, int, float, Vector3> gridCellToWorld)
    {
        this.indicatorMaterial = material;
        this.lineWidth = width;
        this.heightOffset = height;
        this.defaultColor = defaultPreviewColor;
        this.confirmedColor = confirmedPreviewColor;
        this.gridToWorld = gridCellToWorld;

        InitializeLineRenderer(parent);
    }

    /// <summary>
    /// Initialize the LineRenderer GameObject and component
    /// </summary>
    private void InitializeLineRenderer(Transform parent)
    {
        // Create GameObject
        indicatorObject = new GameObject("VisualIndicator");
        indicatorObject.transform.SetParent(parent);

        // Add and configure LineRenderer
        lineRenderer = indicatorObject.AddComponent<LineRenderer>();

        // Set up material
        if (indicatorMaterial != null)
        {
            lineRenderer.material = indicatorMaterial;
        }
        else
        {
            // Create a simple unlit material if none provided
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }

        // Configure line properties
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = defaultColor;
        lineRenderer.endColor = defaultColor;
        lineRenderer.positionCount = 0;
        lineRenderer.useWorldSpace = true;

        // Start hidden
        indicatorObject.SetActive(false);
        isActive = false;
    }

    /// <summary>
    /// Shows a path preview
    /// </summary>
    /// <param name="path">Path to preview (must have at least 2 points)</param>
    /// <param name="isConfirmed">Whether this is a confirmed path (changes color)</param>
    public void ShowPath(List<Vector3Int> path, bool isConfirmed = false)
    {
        if (path == null || path.Count < 2)
        {
            Clear();
            return;
        }

        // Store the path
        currentPath = new List<Vector3Int>(path);
        isActive = true;

        // Set up the LineRenderer
        lineRenderer.positionCount = path.Count;

        // Convert grid cells to world positions
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 worldPos = gridToWorld(path[i].x, path[i].z, heightOffset);
            lineRenderer.SetPosition(i, worldPos);
        }

        // Set color based on confirmation state
        Color lineColor = isConfirmed ? confirmedColor : defaultColor;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;

        // Show the preview
        indicatorObject.SetActive(true);

        Debug.Log($"[VisualIndicator] Path shown with {path.Count} waypoints.");
    }

    /// <summary>
    /// Updates the color of the current preview
    /// </summary>
    /// <param name="isConfirmed">Whether to use confirmed color</param>
    public void UpdateColor(bool isConfirmed)
    {
        if (!isActive) return;

        Color lineColor = isConfirmed ? confirmedColor : defaultColor;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
    }

    /// <summary>
    /// Clears the current path preview
    /// </summary>
    public void Clear()
    {
        if (indicatorObject != null)
        {
            indicatorObject.SetActive(false);
        }

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }

        currentPath = null;
        isActive = false;
    }

    /// <summary>
    /// Cleans up resources when no longer needed
    /// </summary>
    public void Dispose()
    {
        if (indicatorObject != null)
        {
            Object.Destroy(indicatorObject);
        }
    }
}