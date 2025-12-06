using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class GridRenderer3D : GridInterface
{
    // ---------- Public, serialized settings ----------

    public enum TileType
    {
        Ground,
        Wall,
        Void
    }

    public enum TileStatus
    {
        Normal,
        Fire
    }

    public struct TILE
    {
        public int x;
        public int y;
        public int z;
        public TileType type;
        //tile can be occupied without an occupant (e.g., obstacle)
        public bool isOccupied;
        public GameObject occupant;
        public TileStatus[] status;
    }

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

    [Header("Appearance")]
    // Color of each cell quad.
    public Color cellFillColor = new(0.07f, 0.1f, 0.16f, 0.8f);
    // Desired gap/axis thickness measured in pixels on screen.
    public float lineThicknessPixels = 1f;

    [Header("Hover/Click")]
    // Whether to draw hover visual.
    public bool drawHoverCell = true;

    // True when mouse ray hits grid and cell is inside bounds.
    public bool HasHover { get; private set; }
    // Current hovered cell (grid indices), defaults to invalid.
    public Vector3Int HoverCell { get; private set; } = new(-1, 0, -1);
    [SerializeField] private GameObject selectTile;

    // ---------- Grid Data ----------
    //3d array of tile info
    public TILE[,,] gridInfo;

    // Delegate to check if a cell is selectable (used by GridCharacterController3D)
    public System.Func<Vector3Int, bool> IsCellSelectable { get; set; }

   
    public void SetStatus(int x, int z, TileStatus statusToSet)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return;
        if (!System.Array.Exists(gridInfo[x, gridY, z].status, status => status == statusToSet))
        {
            var statuses = new List<TileStatus>(gridInfo[x, gridY, z].status);
            statuses.Add(statusToSet);
            gridInfo[x, gridY, z].status = statuses.ToArray();
        }
    }

    public bool HasStatus(int x, int z, TileStatus statusToCheck)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return false;
        return System.Array.Exists(gridInfo[x, gridY, z].status, status => status == statusToCheck);
    }

    public bool getIsOccupied(int x, int z)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return false;
        return gridInfo[x, gridY, z].isOccupied;
    }

    public void setIsOccupied(int x, int z, bool occupied)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return;
        gridInfo[x, gridY, z].isOccupied = occupied;
    }
    public bool IsWalkable(int x, int z)
    {
        // if tiles array is null, treat all cells as non-walkable
        if (gridInfo == null) return false;
        // if x or z are out of bounds, return false
        if (x < 0 || x >= width) return false;
        if (z < 0 || z >= height) return false;
        // Check if the tile type allows walking
        return gridInfo[x, gridY, z].type == TileType.Ground && !gridInfo[x, gridY, z].isOccupied;
    }

    // Parent rotated so children (2D sprites) lie on XZ plane.
    Transform _plane;
    // Buckets for grid, overlay/hover.
    Transform _gridRoot, _overlayRoot;

    // ---------- Sprite references ----------
    // One SR per cell.
    // readonly List<SpriteRenderer> _cells = new();
    //trying to use plane meshes instead of sprites so we dont have to deal with the camera. this will also makes textures easier. pysics will also be easier
    readonly List<MeshRenderer> _cells = new();

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
            bool canHover = inside && IsWalkable(cell.x, cell.z);

            if (canHover && !cell.Equals(HoverCell)) { HoverCell = cell; HasHover = true; UpdateHover(); }
            else if (!canHover) HasHover = false;
        }
        else HasHover = false;

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

        if (gridData != null)
        {
            // Create new tile grid
            gridInfo = new TILE[width, gridY + 1, height];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Initialize tile with default values
                    gridInfo[x, gridY, z] = new TILE
                    {
                        x = x,
                        z = z,
                        type = gridData[x, z] == 1 ? TileType.Ground : TileType.Void,
                        isOccupied = false,
                        status = new TileStatus[] { TileStatus.Normal }
                    };

                    // Skip tile creation for non-walkable cells
                    if (gridInfo[x, gridY, z].type == TileType.Void) continue;

                    var tile = Instantiate(groundTile, _gridRoot.transform);
                    tile.name = $"C{x}_{z}";
                    var tileTransform = tile.transform;
                    tileTransform.position = new Vector3(x0 + (x + 0.5f) * cellSize, gridY, z0 + (z + 0.5f) * cellSize);
                    tileTransform.localScale = new Vector3(cellSize * 0.1f, 1f, cellSize * 0.1f);
                    var meshRenderer = tile.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        // Use sharedMaterial to avoid creating instances
                        meshRenderer.sharedMaterial.color = cellFillColor;
                        _cells.Add(meshRenderer);
                    }
                }
            }
        }
        else
        {   // If no image, clear tiles
            gridInfo = null;
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

    public override void MoveCreaturePosition(GameObject token, Vector3Int targetPosition, Vector3Int startPosition)
    {
        //make sure we are moving the right character
        if(token == null || gridInfo[startPosition.x, gridY, startPosition.z].occupant != token)
        {
            Debug.Log("Failed to move creature from " + startPosition.ToString() + " to " + targetPosition.ToString());
            return;
        }
        gridInfo[startPosition.x, gridY, startPosition.z].isOccupied = false;
        gridInfo[startPosition.x, gridY, startPosition.z].occupant = null;
        gridInfo[targetPosition.x, gridY, targetPosition.z].isOccupied = true;
        gridInfo[targetPosition.x, gridY, targetPosition.z].occupant = token;
        return; 
    }

    public override void SetCreaturePosition(GameObject token, Vector3Int spawnPosition)
    {
        //make sure we are placing a valid character and the tile is not already occupied
        if (token == null || gridInfo[spawnPosition.x, gridY, spawnPosition.z].isOccupied)
        {
            Debug.Log("Failed to set creature position at " + spawnPosition.ToString());
            return;
        }
        gridInfo[spawnPosition.x, gridY, spawnPosition.z].isOccupied = true;
        gridInfo[spawnPosition.x, gridY, spawnPosition.z].occupant = token;
        
    }

    public override Boolean IsCellWalkable(Vector3Int position)
    {
        return IsWalkable(position.x, position.z);
    }
    public override IEnumerator TargetSelect(int range, CoroutineResult<GameObject> result)
    {
        //return a selected monster if valid
        yield return null;
    }
    //this will return an array of game objects that are found in the given list of points
    public override List<GameObject> GetOccupantsInArea(List<Vector3Int> area)
    {
        List<GameObject> occupants = new List<GameObject>();
        foreach (Vector3Int point in area)
        {
            if (gridInfo[point.x, gridY, point.z].isOccupied && gridInfo[point.x, gridY, point.z].occupant != null)
            {
                occupants.Add(gridInfo[point.x, gridY, point.z].occupant);
            }
        }
        return occupants;
    }
}