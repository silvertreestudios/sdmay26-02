using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class GridRenderer3D : MonoBehaviour
{
    // ---------- Public, serialized settings ----------

    [Header("Grid (XYZ plane)")]
    [SerializeField] private ImageToGrid imageToGrid;

    public int width;
    public int height;
    // World size of one square cell.
    [Min(0.01f)] public float cellSize = 1f;
    // Bottom-left corner of the grid in world XZ.
    public Vector3 origin = Vector3.zero;
    // Fixed Y height where the grid is drawn.
    public int gridY = 0;
    // explicit camera (falls back to Camera.main).
    public Camera targetCamera;
    // refrence to tile prefab
    [SerializeField] private GameObject groundTile;
    [SerializeField] private GameObject wallTile;
    [SerializeField] private GameObject doorTile;

    [Header("Appearance")]
    // Color of each cell quad.
    public Color cellFillColor = new(0.07f, 0.1f, 0.16f, 0.8f);
    // Desired gap/axis thickness measured in pixels on screen.
    public float lineThicknessPixels = 1f;

    [Header("Hover/Click")]
    // Whether to draw hover visual.
    public bool drawHoverCell = true;
    // Allow hovering over door tiles
    public bool allowDoorHover = true;

    // True when mouse ray hits grid and cell is inside bounds.
    public bool HasHover { get; private set; }
    // Current hovered cell (grid indices), defaults to invalid.
    public Vector3Int HoverCell { get; private set; } = new(-1, 0, -1);
    [SerializeField] private GameObject selectTile;

    // ---------- Grid Data ----------
    // Grid memory instance
    public GridMemory gridMemory;

    // Parent rotated so children (2D sprites) lie on XZ plane.
    Transform _plane;
    // Buckets for grid, overlay/hover.
    Transform _gridRoot, _overlayRoot;

    //trying to use plane meshes instead of sprites so we dont have to deal with the camera. this will also makes textures easier. pysics will also be easier
    readonly List<MeshRenderer> _cells = new();
    readonly List<MeshRenderer> _walls = new();

    // Instance of the select tile prefab for selection visual.
    private GameObject _selectTileInstance;


    // ---------- Small helpers to remove repetition ----------

    // Get a camera (cache into targetCamera if null).
    Camera Cam() => targetCamera ? targetCamera : (targetCamera = Camera.main);

    // ---------- Unity lifecycle ----------

    // First-time setup; build everything once.
    void Awake() { }

    // Ensure ready when enabled; hide hover initially.
    void OnEnable()
    {
        Init();
        FullRebuild();
        HasHover = false;
    }

    // Rebuild when inspector values change in edit mode.
    void OnValidate()
    {
#if UNITY_EDITOR
        // Defer the rebuild to avoid "DestroyImmediate is not permitted during OnValidate"
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (!Application.isPlaying)
            {
                Init();
                FullRebuild();
            }
        };
#endif
    }

    /// <summary>
    /// Clean overlay children when disabled/destroyed to avoid leaks.
    /// </summary>
    void OnDisable()
    {
        if (_overlayRoot)
            for (int i = _overlayRoot.childCount - 1; i >= 0; i--)
                SafeDestroy(_overlayRoot.GetChild(i).gameObject);
        if (_selectTileInstance)
        {
            SafeDestroy(_selectTileInstance);
            _selectTileInstance = null;
        }
    }

    /// <summary>
    /// input → hover; keep pixel-accurate sizes.
    /// </summary>
    void Update()
    {
        // need a camera
        var cam = Cam(); if (!cam) return;

        // Keep plane at Y=gridY and rotated onto XZ each frame.
        if (_plane) { _plane.position = new Vector3(0, gridY, 0); _plane.localEulerAngles = new Vector3(90, 0, 0); }

        // Build a ray from the mouse to find the hit on the grid plane.
        var ray = cam.ScreenPointToRay(InputCompat.MousePositionScreen());
        var plane = new Plane(Vector3.up, new Vector3(0f, gridY, 0f));
        if (plane.Raycast(ray, out float t))
        {
            // World hit.
            var hit = ray.GetPoint(t);
            // Convert world XZ to integer cell indices.
            var cell = new Vector3Int(
                Mathf.FloorToInt((hit.x - origin.x) / cellSize),
                0,
                Mathf.FloorToInt((hit.z - origin.y) / cellSize));
            // Inside bounds?
            bool inside = (uint)cell.x < (uint)width && (uint)cell.z < (uint)height;
            bool canHover = inside && gridMemory != null && IsCellHoverable(cell);

            if (canHover && !cell.Equals(HoverCell)) { HoverCell = cell; HasHover = true; UpdateHover(); }
            else if (!canHover) HasHover = false;
        }
        else HasHover = false;

        // Handle click on door tiles
        if (HasHover && Input.GetMouseButtonDown(0))
        {
            HandleCellClick(HoverCell);
        }

        // Keep all visuals pixel-consistent as the camera moves/zooms.
        UpdateHover();
    }

    // ---------- One-time init ----------

    /// <summary>
    /// Create shared sprite and scene graph parents.
    /// </summary>
    void Init()
    {

        // Find or create the plane root and its children.
        if (!_plane)
        {
            _plane = transform.Find("PlaneXZ");
            if (!_plane) { _plane = new GameObject("PlaneXZ").transform; _plane.SetParent(transform, false); }
            // make sure plane is at correct position and rotation
            _gridRoot = GetOrMake(_plane, "Grid");

            _overlayRoot = GetOrMake(_plane, "Overlay");
        }

        // Ensure the plane is posed (XZ at Y=gridY).
        _plane.position = new Vector3(0, gridY, 0);
        _plane.localEulerAngles = new Vector3(90, 0, 0);
    }

    /// <summary>
    /// Find a child or create it.
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    Transform GetOrMake(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); }
        return t;
    }

    // ---------- Build & update visuals ----------

    /// <summary>
    /// rebuild everything and do an initial pixel-size pass.
    /// </summary>
    void FullRebuild()
    {
        RebuildGrid();
        var cam = Cam(); if (cam) { UpdateHover(); }
    }

    /// <summary>
    /// Remove old grid SRs, create per-cell SRs.
    /// </summary>
    void RebuildGrid()
    {
        int width = 0;
        int height = 0;
        int[,] gridData = null;
        // Clear all previous grid children and empty the cell list.
        ClearChildrenPlane(_gridRoot, _cells);
        _walls.Clear();
        // Destroy all select tile instances under _overlayRoot
        if (_overlayRoot) { ClearOverlayChildren(); }
        _selectTileInstance = null;
        // Compute world-space grid width (gw) and height (gh).
        // origin cache
        float x0 = origin.x, z0 = origin.y;
        if (imageToGrid != null)
        {
            gridData = imageToGrid.GenerateGrid();
            width = imageToGrid.GetWidth();
            height = imageToGrid.GetHeight();
            // Use gridData, gridWidth, gridHeight as needed...
            imageToGrid.PrintGrid();
        }

        if (!gridMemory) gridMemory = GetComponent<GridMemory>();
        if (!gridMemory) gridMemory = gameObject.AddComponent<GridMemory>();

        gridMemory.Initialize(width, height, gridY, cellSize, origin, gridData);
        this.width = width;
        this.height = height;

        if (gridMemory.GridInfo != null)
        {
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    var tileType = gridMemory.GridInfo[x, gridY, z].type;

                    // Skip void tiles
                    if (tileType == GridMemory.TileType.Void) continue;

                    // create ground tiles for walkable cells
                    // if (tileType == GridMemory.TileType.Ground && groundTile != null)
                    // {
                    //     var tile = Instantiate(groundTile, _gridRoot.transform);
                    //     tile.name = $"C{x}_{z}";
                    //     var tileTransform = tile.transform;
                    //     tileTransform.position = new Vector3(x0 + (x + 0.5f) * cellSize, gridY, z0 + (z + 0.5f) * cellSize);
                    //     tileTransform.localScale = new Vector3(cellSize * 0.1f, 1f, cellSize * 0.1f);
                    //     var meshRenderer = tile.GetComponent<MeshRenderer>();
                    //     if (meshRenderer != null)
                    //     {
                    //         // Use sharedMaterial to avoid creating instances
                    //         meshRenderer.sharedMaterial.color = cellFillColor;
                    //         _cells.Add(meshRenderer);
                    //     }
                    // }
                    // create wall tiles for wall cells
                    // else if (tileType == GridMemory.TileType.Wall && wallTile != null)
                    // {
                    //     // use different prefab for walls
                    //     var wall = Instantiate(wallTile, _gridRoot.transform);
                    //     wall.name = $"W{x}_{z}";
                    //     var wallTransform = wall.transform;
                    //     float wallHeight = cellSize;
                    //     // lift center up by half the wall height so base stays at gridY
                    //     wallTransform.position = new Vector3(x0 + (x + 0.5f) * cellSize, gridY + (wallHeight * 0.5f), z0 + (z + 0.5f) * cellSize);
                    //     wallTransform.localScale = new Vector3(cellSize, wallHeight, cellSize*3f);
                    //     var meshRenderer = wall.GetComponent<MeshRenderer>();
                    //     if (meshRenderer != null)
                    //     {
                    //         _walls.Add(meshRenderer);
                    //     }
                    // }
                }
            }
        }

    }

    bool IsCellHoverable(Vector3Int cell)
    {
        if (gridMemory == null) return false;
        
        // Check if cell is walkable (ground tiles)
        if (gridMemory.IsCellWalkable(cell)) return true;
        
        // Check if cell is a door and door hovering is enabled
        if (allowDoorHover && gridMemory.GridInfo != null)
        {
            int x = cell.x;
            int z = cell.z;
            if (x >= 0 && x < width && z >= 0 && z < height)
            {
                var tileType = gridMemory.GridInfo[x, gridY, z].type;
                return tileType == GridMemory.TileType.Door;
            }
        }
        
        return false;
    }


    void HandleCellClick(Vector3Int cell)
    {
        if (gridMemory == null || gridMemory.GridInfo == null) return;

        int x = cell.x;
        int z = cell.z;

        // Validate cell bounds
        if (x < 0 || x >= width || z < 0 || z >= height) return;

        var tile = gridMemory.GridInfo[x, gridY, z];

        // If it's a door tile, toggle between open and closed
        if (tile.type == GridMemory.TileType.Door)
        {
            bool isCurrentlyOpen = gridMemory.IsDoorOpen(x, z);
            
            if (isCurrentlyOpen)
            {
                // Close the door
                Debug.Log($"Door clicked at ({x}, {z}). Closing door.");
                gridMemory.RemoveStatus(x, z, GridMemory.TileStatus.DoorOpen);
                gridMemory.SetStatus(x, z, GridMemory.TileStatus.DoorClosed);
                gridMemory.SetIsOccupied(x, z, true); // Closed doors block movement
            }
            else
            {
                // Open the door
                Debug.Log($"Door clicked at ({x}, {z}). Opening door.");
                gridMemory.RemoveStatus(x, z, GridMemory.TileStatus.DoorClosed);
                gridMemory.SetStatus(x, z, GridMemory.TileStatus.DoorOpen);
                gridMemory.SetIsOccupied(x, z, false); // Open doors allow movement
            }
        }
    }

    /// <summary>
    /// update the position of the hover tile. If hovering over invalid location or if drawHoverCell is false, hide the tile.
    /// </summary>
    void UpdateHover()
    {
        bool show = HasHover && drawHoverCell;
        if (show)
        {
            // Compute hovered cell center.
            float mx = origin.x + (HoverCell.x + 0.5f) * cellSize;
            float mz = origin.y + (HoverCell.z + 0.5f) * cellSize;

            if (_selectTileInstance == null)
            {
                _selectTileInstance = Instantiate(selectTile, _overlayRoot);
                _selectTileInstance.name = "SelectTileInstance";
            }
            // Position and scale the select tile to match the cell.
            var t = _selectTileInstance.transform;
            t.position = new Vector3(mx, gridY + 0.1f, mz);
            t.localScale = new Vector3(cellSize * 0.1f, 1f, cellSize * 0.1f);
            _selectTileInstance.SetActive(true);
        }
        else
        {
            if (_selectTileInstance)
            {
                _selectTileInstance.SetActive(false);
            }
        }
    }

    // ---------- Utility ----------

    /// <summary>
    /// Destroy safely in play or edit mode.
    /// </summary>
    /// <param name="o"></param>
    static void SafeDestroy(UnityEngine.Object o) { if (!o) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    void ClearChildrenPlane(Transform parent, List<MeshRenderer> list)
    {
        if (parent) for (int i = parent.childCount - 1; i >= 0; i--) SafeDestroy(parent.GetChild(i).gameObject);
        list?.Clear();
    }
    void ClearOverlayChildren()
    {
        for (int i = _overlayRoot.childCount - 1; i >= 0; i--)
        {
            var child = _overlayRoot.GetChild(i).gameObject;
            if (child.name == "SelectTileInstance" || (selectTile && child.name == selectTile.name + "(Clone)"))
            {
                SafeDestroy(child);
            }
        }
    }
}