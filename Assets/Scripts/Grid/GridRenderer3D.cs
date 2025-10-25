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
        public int z;
        public TileType type;
        public bool isOccupied;
        public TileStatus[] status;
    }

    [Header("Grid (XZ plane)")]
    [SerializeField] private ImageToGrid imageToGrid;

    public int width;
    public int height;
    // World size of one square cell.
    [Min(0.01f)] public float cellSize = 1f;
    // Bottom-left corner of the grid in world XZ.
    public Vector2 origin = Vector2.zero;
    // Fixed Y height where the grid is drawn.
    public float gridY = 0f;
    // explicit camera (falls back to Camera.main).
    public Camera targetCamera;
    // refrence to tile prefab
    [SerializeField] private GameObject groundTile;

    [Header("Appearance")]
    // Color of each cell quad.
    public Color cellFillColor = new(0.07f, 0.1f, 0.16f, 0.8f);
    // Desired gap/axis thickness measured in pixels on screen.
    public float lineThicknessPixels = 1f;

    [Header("Walls")]
    // Wall tint.
    public Color wallColor = new(0.2f, 0.45f, 1f, 1f);
    // Wall thickness in pixels.
    [Min(1)] public float wallThicknessPixels = 10f;
    // Minimal wall segment representation in grid coords (inclusive endpoints).
    [System.Serializable] public struct WallSegment { public Vector2Int start, end; }
    // Example data so the system shows something by default.
    public List<WallSegment> walls = new() { new() { start = new(1, 3), end = new(10, 3) } };

    [Header("Hover/Click")]
    // Whether to draw hover visual.
    public bool drawHoverCell = true;

    // True when mouse ray hits grid and cell is inside bounds.
    public bool HasHover { get; private set; }
    // Current hovered cell (grid indices), defaults to invalid.
    public Vector2Int HoverCell { get; private set; } = new(-1, -1);
    [SerializeField] private GameObject selectTile;

    // ---------- Grid Data ----------
    public TILE[,] gridInfo;

    public void SetStatus(int x, int z, TileStatus statusToSet)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return;
        if (!System.Array.Exists(gridInfo[x, z].status, status => status == statusToSet))
        {
            var statuses = new List<TileStatus>(gridInfo[x, z].status);
            statuses.Add(statusToSet);
            gridInfo[x, z].status = statuses.ToArray();
        }
    }

    public bool HasStatus(int x, int z, TileStatus statusToCheck)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return false;
        return System.Array.Exists(gridInfo[x, z].status, status => status == statusToCheck);
    }

    public bool getIsOccupied(int x, int z)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return false;
        return gridInfo[x, z].isOccupied;
    }

    public void setIsOccupied(int x, int z, bool occupied)
    {
        if (gridInfo == null || x < 0 || x >= width || z < 0 || z >= height) return;
        gridInfo[x, z].isOccupied = occupied;
    }
    public bool IsCellWalkable(int x, int z)
    {
        // if tiles array is null, treat all cells as non-walkable
        if (gridInfo == null) return false;
        // if x or z are out of bounds, return false
        if (x < 0 || x >= width) return false;
        if (z < 0 || z >= height) return false;
        // Check if the tile type allows walking
        return gridInfo[x, z].type == TileType.Ground && !gridInfo[x, z].isOccupied;
    }
    // ---------- Wall cache state ----------

    // Horizontal/vertical blocked edges caches; rebuild gate.
    bool[] _h, _v; bool _wallsDirty = true;

    // Shared 1×1 white sprite for every quad; static so all instances reuse it.
    static Sprite _white;

    // ---------- Scene graph (parents) ----------

    // Parent rotated so children (2D sprites) lie on XZ plane.
    Transform _plane;
    // Buckets for grid, walls, overlay/hover.
    Transform _gridRoot, _wallRoot, _overlayRoot;

    // ---------- Sprite references ----------
    // One SR per cell.
    // readonly List<SpriteRenderer> _cells = new();
    //trying to use plane meshes instead of sprites so we dont have to deal with the camera. this will also makes textures easier. pysics will also be easier
    readonly List<MeshRenderer> _cells = new();

    // One SR per wall segment.
    readonly List<SpriteRenderer> _wallSRs = new();
    // Instance of the select tile prefab for selection visual.
    private GameObject _selectTileInstance;


    // ---------- Small helpers to remove repetition ----------

    // Get a camera (cache into targetCamera if null).
    Camera Cam() => targetCamera ? targetCamera : (targetCamera = Camera.main);

    /// <summary>
    /// Compute the distance from the camera to the horizontal plane at Y = gridY.
    /// </summary>
    /// <param name="cam"></param>
    /// <returns></returns>
    float PlaneDepth(Camera cam)
    {
        // Define a plane parallel to the XZ plane with an upward normal (Y+), located at y = gridY.
        var plane = new Plane(Vector3.up, new Vector3(0f, gridY, 0f));
        // Cast a ray starting at the camera position and pointing along the camera's forward direction.
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        // If the ray intersects the plane, return the hit distance; otherwise fall back to vertical distance to the plane.
        return plane.Raycast(ray, out float t) ? t : Mathf.Abs(gridY - cam.transform.position.y);
    }

    /// <summary>
    /// Converts a given number of screen pixels into the equivalent
    /// world-space distance at the grid’s depth so on-screen line thickness stays consistent
    /// </summary>
    /// <param name="cam"></param>
    /// <param name="px"></param>
    /// <returns></returns>
    float PxToWorld(Camera cam, float px)
    {
        // never < 1 px
        px = Mathf.Max(1f, px);
        // ortho: direct scale
        if (cam.orthographic)
            return (cam.orthographicSize * 2f / Screen.height) * px;
        // perspective: measure at depth
        float d = PlaneDepth(cam);

        Vector3 a = cam.ScreenToWorldPoint(new Vector3(0, 0, d));
        Vector3 b = cam.ScreenToWorldPoint(new Vector3(0, px, d));
        return (b - a).magnitude;
    }

    /// <summary>
    /// Position/scale a sprite on the XZ plane (inherits 90° rotation).
    /// </summary>
    /// <param name="t"></param>
    /// <param name="x"></param>
    /// <param name="z"></param>
    /// <param name="sx"></param>
    /// <param name="sz"></param>
    void SetTRS(Transform t, float x, float z, float sx, float sz)
    {
        // world position on plane
        t.position = new Vector3(x, gridY, z);
        // keep local upright
        t.localRotation = Quaternion.identity;
        // scale in local XY
        t.localScale = new Vector3(sx, sz, 1f);
    }

    /// <summary>
    /// Create a SpriteRenderer child with shared white sprite and basic flags.
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="name"></param>
    /// <param name="order"></param>
    /// <param name="col"></color>
    /// <returns></returns>
    SpriteRenderer NewSR(Transform parent, string name, int order, Color col)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _white; sr.sortingOrder = order; sr.color = col;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        sr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        sr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return sr;
    }

    // ---------- Unity lifecycle ----------

    // First-time setup; build everything once.
    void Awake() { Init(); FullRebuild(); }

    // Ensure ready when enabled; hide hover initially.
    void OnEnable() { Init(); FullRebuild(); HasHover = false; }

    // Rebuild when inspector values change in edit mode.
    void OnValidate() { Init(); FullRebuild(); }

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
            var cell = new Vector2Int(
                Mathf.FloorToInt((hit.x - origin.x) / cellSize),
                Mathf.FloorToInt((hit.z - origin.y) / cellSize));
            // Inside bounds?
            bool inside = (uint)cell.x < (uint)width && (uint)cell.y < (uint)height;
            bool canHover = inside && IsCellWalkable(cell.x, cell.y);

            if (canHover && cell != HoverCell) { HoverCell = cell; HasHover = true; UpdateHover(); }
            else if (!canHover) HasHover = false;
        }
        else HasHover = false;

        // Rebuild wall visuals if the cache got invalidated.
        if (_wallsDirty) { _wallsDirty = false; RebuildWallCache(); RebuildWalls(); }

        // Keep all visuals pixel-consistent as the camera moves/zooms.
        UpdateGrid();
        UpdateWalls(cam);
        UpdateHover();
    }

    // ---------- One-time init ----------

    /// <summary>
    /// Create shared sprite and scene graph parents.
    /// </summary>
    void Init()
    {
        // Create shared 1×1 white sprite once.
        if (!_white)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "GridWhite" };
            tex.SetPixel(0, 0, Color.white); tex.Apply(false, true);
            _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _white.name = "GridWhiteSprite";
        }

        // Find or create the plane root and its children.
        if (!_plane)
        {
            _plane = transform.Find("PlaneXZ");
            if (!_plane) { _plane = new GameObject("PlaneXZ").transform; _plane.SetParent(transform, false); }
            _gridRoot = GetOrMake(_plane, "Grid");
            _wallRoot = GetOrMake(_plane, "Walls");
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
        RebuildWallCache();
        RebuildWalls();
        var cam = Cam(); if (cam) { UpdateGrid(); UpdateWalls(cam); UpdateHover(); }
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
            gridInfo = new TILE[width, height];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Initialize tile with default values
                    gridInfo[x, z] = new TILE
                    {
                        x = x,
                        z = z,
                        type = gridData[x, z] == 1 ? TileType.Ground : TileType.Void,
                        isOccupied = false,
                        status = new TileStatus[] { TileStatus.Normal }
                    };

                    // Skip tile creation for non-walkable cells
                    if (gridInfo[x, z].type == TileType.Void) continue;

                    var tile = Instantiate(groundTile, _gridRoot.transform);
                    tile.name = $"C{x}_{z}";
                    var tileTransform = tile.transform;
                    tileTransform.position = new Vector3(x0 + (x + 0.5f) * cellSize, gridY, z0 + (z + 0.5f) * cellSize);
                    tileTransform.localScale = new Vector3(cellSize * 0.1f, 1f, cellSize * 0.1f);
                    var meshRenderer = tile.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
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
    /// Use to update all cells rendered on screen.
    /// </summary>
    void UpdateGrid()
    {
        // Iterate all cell renderers to apply scale and color.
        for (int i = 0; i < _cells.Count; i++)
        {
            var meshRenderer = _cells[i];
            if (!meshRenderer) continue;
            meshRenderer.material.color = cellFillColor;
        }
    }


    /// <summary>
    /// Destroy old walls; create one SR per segment at its center (length set later).
    /// </summary>
    void RebuildWalls()
    {
        // Remove previously built wall sprites and clear the list.
        ClearChildren(_wallRoot, _wallSRs);
        // If there are no segments, nothing to build.
        if (walls == null || walls.Count == 0) return;

        // Cache origin for quick conversion to world.
        float x0 = origin.x, z0 = origin.y;
        // For each wall segment specified in grid coordinates…
        foreach (var s in walls)
        {
            // Create a renderer for that wall.
            var sr = NewSR(_wallRoot, "Wall", 3, wallColor);
            // Horizontal segment when both endpoints share the same row (y index).
            if (s.start.y == s.end.y)
            {
                // World Z coordinate for that grid row.
                float z = z0 + s.start.y * cellSize;
                // World X endpoints along that row.
                float xa = x0 + Mathf.Min(s.start.x, s.end.x) * cellSize;
                float xb = x0 + Mathf.Max(s.start.x, s.end.x) * cellSize;
                // Center the wall between endpoints; store its X length (Y thickness is added later).
                SetTRS(sr.transform, (xa + xb) * 0.5f, z, xb - xa, 1f);
            }
            // Otherwise, treat as a vertical segment (shared column).
            else
            {
                // World X for that column.
                float x = x0 + s.start.x * cellSize;
                // World Z endpoints along that column.
                float za = z0 + Mathf.Min(s.start.y, s.end.y) * cellSize;
                float zb = z0 + Mathf.Max(s.start.y, s.end.y) * cellSize;
                // Center the wall between endpoints; store its Y length (X thickness is added later).
                SetTRS(sr.transform, x, (za + zb) * 0.5f, 1f, zb - za);
            }
            // Track the wall SR for thickness/color updates.
            _wallSRs.Add(sr);
        }
        // If a camera is available, immediately size thickness to pixel-accurate value.
        var cam = Cam(); if (cam) UpdateWalls(cam);
    }

    /// <summary>
    /// Make walls have pixel-accurate thickness (and keep color live).
    /// </summary>
    /// <param name="cam"></param>
    void UpdateWalls(Camera cam)
    {
        float thick = PxToWorld(cam, wallThicknessPixels);
        for (int i = 0; i < _wallSRs.Count; i++)
        {
            var sr = _wallSRs[i]; if (!sr) continue; sr.color = wallColor;
            var sc = sr.transform.localScale;                    // current length stored in one axis
            bool horiz = sc.x >= sc.y;                           // simple heuristic to decide orientation
            sr.transform.localScale = horiz ? new Vector3(Mathf.Max(0, sc.x), thick, 1f)
                                            : new Vector3(thick, Mathf.Max(0, sc.y), 1f);
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
            float mz = origin.y + (HoverCell.y + 0.5f) * cellSize;

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



    // ---------- Walls cache & queries ----------

    /// <summary>
    /// External signal to recompute walls.
    /// </summary>
    public void MarkWallsDirty() => _wallsDirty = true;

    /// <summary>
    /// Ensure (re)allocated caches and clear them.
    /// </summary>
    void EnsureEdgeArrays()
    {
        int hN = width * (height + 1), vN = (width + 1) * height;
        if (_h == null || _h.Length != hN) _h = new bool[hN];
        if (_v == null || _v.Length != vN) _v = new bool[vN];
        System.Array.Clear(_h, 0, _h.Length);
        System.Array.Clear(_v, 0, _v.Length);
    }
    // Indexing helpers for caches.
    int H(int xCell, int gridlineZ) => gridlineZ * width + xCell;
    int V(int gridlineX, int zCell) => zCell * (width + 1) + gridlineX;

    /// <summary>
    /// Convert segments → blocked edge arrays.
    /// </summary>
    void RebuildWallCache()
    {
        if (walls == null) { _h = _v = null; return; }
        EnsureEdgeArrays();
        for (int i = 0; i < walls.Count; i++)
        {
            var s = walls[i];
            if (s.start.y == s.end.y)                            // horizontal segment
            {
                int z = s.start.y, a = Mathf.Min(s.start.x, s.end.x), b = Mathf.Max(s.start.x, s.end.x);
                for (int x = a; x < b; x++)
                    if ((uint)x < (uint)width && (uint)z <= (uint)height) _h[H(x, z)] = true;
            }
            else if (s.start.x == s.end.x)                       // vertical segment
            {
                int x = s.start.x, a = Mathf.Min(s.start.y, s.end.y), b = Mathf.Max(s.start.y, s.end.y);
                for (int z = a; z < b; z++)
                    if ((uint)z < (uint)height && (uint)x <= (uint)width) _v[V(x, z)] = true;
            }
        }
    }

    /// <summary>
    /// Returns whether moving from a→b (4-neighbor only) is blocked by a wall.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public bool IsEdgeBlocked(Vector3Int a, Vector3Int b)
    {
        int dx = b.x - a.x, dz = b.y - a.y;
        // non-cardinal moves are disallowed
        if (Mathf.Abs(dx) + Mathf.Abs(dz) != 1) return true;
        // cross vertical at x+1
        if (dx == 1) return IsV(a.x + 1, a.y);
        // cross vertical at x
        if (dx == -1) return IsV(a.x, a.y);
        // cross horizontal at z+1
        if (dz == 1) return IsH(a.x, a.y + 1);
        // cross horizontal at z
        if (dz == -1) return IsH(a.x, a.y);
        return true;
    }

    /// <summary>
    /// Read vertical edge cache build if null).
    /// </summary>
    /// <param name="gridlineX"></param>
    /// <param name="zCell"></param>
    /// <returns></returns>
    bool IsV(int gridlineX, int zCell)
    {
        if (_v == null) RebuildWallCache();
        return _v != null && (uint)zCell < (uint)height && (uint)gridlineX <= (uint)width && _v[V(gridlineX, zCell)];
    }
    /// <summary>
    /// Read horizontal edge cache (build if null).
    /// </summary>
    /// <param name="xCell"></param>
    /// <param name="gridlineZ"></param>
    /// <returns></returns>
    bool IsH(int xCell, int gridlineZ)
    {
        if (_h == null) RebuildWallCache();
        return _h != null && (uint)xCell < (uint)width && (uint)gridlineZ <= (uint)height && _h[H(xCell, gridlineZ)];
    }

    // ---------- Utility ----------

    /// <summary>
    /// Destroy safely in play or edit mode.
    /// </summary>
    /// <param name="o"></param>
    static void SafeDestroy(Object o) { if (!o) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    /// <summary>
    /// Delete children (optionally clear an SR list).
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="list"></param>
    void ClearChildren(Transform parent, List<SpriteRenderer> list)
    {
        if (parent) for (int i = parent.childCount - 1; i >= 0; i--) SafeDestroy(parent.GetChild(i).gameObject);
        list?.Clear();
    }
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

    public override IEnumerator MoveCreature()
    {
        //beep boop
        yield return null;
    }
}
