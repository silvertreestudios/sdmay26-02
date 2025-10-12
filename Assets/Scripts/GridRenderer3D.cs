using System.Collections.Generic;              // List<T> etc.
using UnityEngine;                              // Unity engine APIs

[ExecuteAlways]                                // Run in Edit + Play mode
[DisallowMultipleComponent]                    // Only one per GameObject
public class GridRenderer : MonoBehaviour
{
    [Header("Grid (XZ plane)")]
    // Columns (>=1)
    [Min(1)] public int width = 20;
    // Rows (>=1)
    [Min(1)] public int height = 12;
    // World size of one cell
    [Min(0.01f)] public float cellSize = 1f;
    // Bottom-left world XZ of grid
    public Vector2 origin = Vector2.zero;   
    // Y height where grid lies
    public float gridY = 0f;       
    public Camera targetCamera;                

    // CAMERA CONFIG (subject to change) //
    [Header("Default Camera Pose")]
    // Apply default pose on enable
    public bool setCameraOnEnable = true;
    // true: Perspective, false: Ortho
    public bool defaultPerspective = true;
    // Default position
    public Vector3 defaultCamPosition = new(8f, 10f, -8f);
    // Default rotation
    public Vector3 defaultCamEuler = new(30f, 0f, 0f);
    // Default ortho size
    public float defaultOrthoSize = 10f;  

    // GRID APPEARANCE //
    [Header("Appearance")]
    // Grid line color
    public Color lineColor = new(1, 1, 1, 0.85f);
    // Axis color
    public Color axisColor = new(1, 0.55f, 0.1f, 0.95f);
    // Grid thickness in screen px
    public float lineThicknessPixels = 2f;
    // Toggle axes
    public bool drawAxes = true;               

    [Header("Walls")]
    // Wall color
    public Color wallColor = new(0.2f, 0.45f, 1f, 1f);
    // Wall px thickness
    [Min(1)] public float wallThicknessPixels = 10f;
    [System.Serializable] public struct WallSegment { public Vector2Int start, end; } // Grid-edge segment
    public List<WallSegment> walls = new() { new() { start = new(1, 3), end = new(10, 3) } }; // Example wall

    [Header("Hover/Click")]
    // Show hovered cell
    public bool drawHoverCell = true;
    bool _hoverEnabled = false;
    // Hover fill color
    public Color hoverFill = new(0.3f, 0.6f, 1f, 0.18f);
    // Hover outline color
    public Color hoverOutline = new(0.7f, 0.9f, 1f, 0.95f);
    // Click flash color
    public Color clickFlashColor = new(1f, 0.92f, 0.2f, 0.35f);
    // Flash duration (s)
    public float clickFlashDuration = 0.4f;

    // --- State ---
    public bool HasHover { get; private set; } // True if mouse is currently inside the grid bounds
    public Vector2Int HoverCell { get; private set; } // Current hovered cell
    struct Flash { public Vector2Int cell; public float until; public Color color; } // Flash entry

    // Collection of active flashes to render and expire over time.
    readonly List<Flash> _flashes = new();

    // Edge caches + dirty flag
    bool[] _h, _v; bool _wallsDirty = true;

    // 1x1 white sprite
    Sprite _white;
    // Hierarchy parents
    Transform _gridRoot, _wallRoot, _overlayRoot; 
    // Grid SRs
    readonly List<SpriteRenderer> _gridSRs = new();
    // Axis SRs
    readonly List<SpriteRenderer> _axisSRs = new();
    // Wall SRs
    readonly List<SpriteRenderer> _wallSRs = new();
    // Hover fill SR
    SpriteRenderer _hoverFillSR;                     
    readonly List<SpriteRenderer> _hoverEdgeSRs = new(4); // Hover edges
    readonly List<SpriteRenderer> _flashSRs = new();      // Flash quads

    Camera Cam => targetCamera ? targetCamera : (targetCamera = Camera.main); // Lazy camera
    float _lastPxWorldGrid, _lastPxWorldWall; // Cached px→world scales
    Vector3 _lastCamPos; Quaternion _lastCamRot; float _lastCamSizeOrFov; // Cached camera
    int _lastW, _lastH; float _lastCell; Vector2 _lastOrigin; float _lastY; // Cached layout

    static readonly Quaternion XZ_ROT = Quaternion.Euler(90f, 0f, 0f); // XY→XZ

    // ---------- Unity ----------

    // Ensures internal objects (roots and 1×1 sprite) exist, marks wall cache dirty, and optionally applies the default camera pose.
    void Awake()
    {
        // Create/find child roots
        EnsureRoots();

        // Create 1x1 sprite if needed
        EnsureWhite();

        // Force wall-cache rebuild
        MarkWallsDirty();                       
        if (setCameraOnEnable) ApplyDefaultCameraPose(); // Optional camera pose
    }

    // Re-creates infra, optionally sets the camera, disables hover state/visuals so nothing is highlighted on start, then does a full rebuild.
    void OnEnable()
    {
        EnsureRoots(); EnsureWhite();
        if (setCameraOnEnable) ApplyDefaultCameraPose();

        // Start with hover visuals disabled until first click
        _hoverEnabled = false;
        HasHover = false;
        HoverCell = new Vector2Int(-1, -1);

        FullRebuild();

        // Ensure any existing hover sprites are hidden at start
        if (_hoverFillSR) _hoverFillSR.enabled = false;
        foreach (var e in _hoverEdgeSRs) if (e) e.enabled = false;
    }

    // Runs in the editor when values change; rebuilds infra, marks walls dirty, and fully rebuilds visuals so the Scene view updates.
    void OnValidate()
    {
        // Keep infra valid in edit
        EnsureRoots(); EnsureWhite();

        // Walls may depend on sizes
        MarkWallsDirty();

        // Rebuild visuals
        FullRebuild();                          
    }

    /// Marks the wall cache dirty so it refreshes next frame.
    public void MarkWallsDirty() { _wallsDirty = true; }

    // Cleans up overlay children (hover + flash quads) and clears hover sprite references
    void OnDisable()
    {
        // Clean overlay (hover/flash)
        if (_overlayRoot)                       
            for (int i = _overlayRoot.childCount - 1; i >= 0; i--)
                SafeDestroy(_overlayRoot.GetChild(i).gameObject);
        // Drop hover refs
        _hoverFillSR = null;                    
        _hoverEdgeSRs.Clear();
    }

    // ---------- Update Loop ----------
    // Main loop: computes mouse hover on the XZ plane, updates/flushes flashes,
    // rebuilds walls if dirty, and reacts to layout/camera changes by rebuilding or rescaling visuals
    void Update()
    {
        var cam = Cam; if (!cam) return;

        // --- Hover detection ---
        // Mouse screen pos
        Vector3 mp = InputCompat.MousePositionScreen();
        // Project to plane
        if (ScreenToXZPlane(cam, mp, gridY, out Vector3 hit))
        {
            // World→cell
            Vector2Int cell = WorldToCellXZ(hit);
            // Bounds check
            bool inside = (uint)cell.x < (uint)width && (uint)cell.y < (uint)height; 
            // Store hover state
            HasHover = inside;                   
            // Update visuals on change
            if (inside && cell != HoverCell) { HoverCell = cell; UpdateHoverVisual(); }
            // Click → flash cell
            if (inside && InputCompat.LeftClickDown())
                FlashCell(HoverCell, clickFlashDuration, clickFlashColor);
        }
        // Not over grid plane
        else HasHover = false;

        // --- Flash expiration ---
        // If any active flashes
        if (_flashes.Count > 0)                  
        {
            // Clock
            float now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            // Compact “alive” flashes
            int wIdx = 0;                        
            for (int r = 0; r < _flashes.Count; r++)
                if (now < _flashes[r].until) _flashes[wIdx++] = _flashes[r];
            // Trim and rebuild visuals
            if (wIdx < _flashes.Count)           
            {
                _flashes.RemoveRange(wIdx, _flashes.Count - wIdx);
                RebuildFlashes();
            }
        }

        // --- Walls: refresh if dirty ---

        // Cache needs rebuild?
        if (_wallsDirty)                         
        {
            _wallsDirty = false;
            // Recompute edge blocks
            RebuildWallCache();

            // Rebuild wall sprites
            RebuildWalls();                      
        }

        // --- Change detection ---

        // Grid dimensions/origin changed?
        if (LayoutChanged())                     
        {
            // Full visuals
            RebuildGrid(); RebuildAxes(); RebuildWalls();
            // Overlays
            UpdateHoverVisual(); RebuildFlashes();
            // Save snapshot
            CacheState(cam);                    
        }
        else if (CamChanged(cam))               // Camera/px scale changed?
        {
            UpdateGrid(); UpdateAxes(); UpdateWalls();    // Rescale only
            UpdateHoverVisual(); RebuildFlashes();        // Overlays
            CacheState(cam);                    // Save snapshot
        }
    }

    // ---------- Camera ----------
    void ApplyDefaultCameraPose()
    {
        var cam = Cam; if (!cam) return;        // Camera guard
        cam.orthographic = !defaultPerspective; // Projection mode
        if (!defaultPerspective) cam.orthographicSize = defaultOrthoSize; // Ortho size
        // Pose in one call
        cam.transform.SetPositionAndRotation(    
            defaultCamPosition,
            Quaternion.Euler(defaultCamEuler)
        );
    }

    // ---------- Hover ----------

    // creates the hover fill quad and four edge line sprites if they don’t exist.
    void EnsureHoverSprites()
    {
        // Already created
        if (_hoverFillSR) return;               
        // Fill quad
        _hoverFillSR = NewSR(_overlayRoot, "HoverFill", 10, hoverFill); 
        // Make 4 edge lines
        _hoverEdgeSRs.Clear();                   
        for (int i = 0; i < 4; i++)
            _hoverEdgeSRs.Add(NewSR(_overlayRoot, "HoverEdge" + i, 11, hoverOutline));
    }

    // Shows/hides the hover sprites and positions/scales them to outline the current HoverCell at the correct pixel thickness.
    void UpdateHoverVisual()
    {
        // Ensure SRs exist
        EnsureHoverSprites();                    // Ensure SRs exist
        bool show = HasHover && drawHoverCell;   // Visibility flag
        _hoverFillSR.enabled = show;             // Toggle fill
        foreach (var e in _hoverEdgeSRs) e.enabled = show; // Toggle edges
        if (!show) return;                       // Nothing to place

        float thick = PixelsToWorld(Cam, lineThicknessPixels); // px→world
        float xa = origin.x + HoverCell.x * cellSize;          // Cell bounds
        float za = origin.y + HoverCell.y * cellSize;
        float xb = xa + cellSize, zb = za + cellSize;

        _hoverFillSR.transform.SetPositionAndRotation( // Centered fill
            P((xa + xb) * 0.5f, (za + zb) * 0.5f), XZ_ROT);
        _hoverFillSR.transform.localScale = new Vector3(cellSize, cellSize, 1f);
        _hoverFillSR.color = hoverFill;

        var L = _hoverEdgeSRs[0]; var R = _hoverEdgeSRs[1];   // Edge SRs
        var B = _hoverEdgeSRs[2]; var T = _hoverEdgeSRs[3];

        L.transform.SetPositionAndRotation(P(xa, (za + zb) * 0.5f), XZ_ROT); // Left
        L.transform.localScale = new Vector3(thick, cellSize, 1f);

        R.transform.SetPositionAndRotation(P(xb, (za + zb) * 0.5f), XZ_ROT); // Right
        R.transform.localScale = new Vector3(thick, cellSize, 1f);

        B.transform.SetPositionAndRotation(P((xa + xb) * 0.5f, za), XZ_ROT); // Bottom
        B.transform.localScale = new Vector3(cellSize, thick, 1f);

        T.transform.SetPositionAndRotation(P((xa + xb) * 0.5f, zb), XZ_ROT); // Top
        T.transform.localScale = new Vector3(cellSize, thick, 1f);

        foreach (var e in _hoverEdgeSRs) e.color = hoverOutline; // Edge color
    }

    // ---------- Grid + Walls + Flash ----------

    // Complete refresh: rebuilds grid, axes, wall cache & walls, updates hover and flash overlays, and caches current state.
    void FullRebuild()
    {
        RebuildGrid(); RebuildAxes(); RebuildWallCache(); RebuildWalls(); // Core
        UpdateHoverVisual(); RebuildFlashes();                            // Overlay
        CacheState(Cam);                                                  // Snapshot
    }

    // Destroys old grid line sprites and recreates new vertical and horizontal line sprites in the right positions, then calls UpdateGrid.
    void RebuildGrid()
    {
        ClearChildren(_gridRoot, _gridSRs);   // Remove old grid SRs
        float x0 = origin.x, z0 = origin.y;   // Origin
        float gw = width * cellSize, gh = height * cellSize; // Extents

        for (int x = 0; x <= width; x++)      // Vertical lines
        {
            var sr = NewSR(_gridRoot, $"V{x}", 0, lineColor);
            sr.transform.SetPositionAndRotation(P(x0 + x * cellSize, z0 + gh * 0.5f), XZ_ROT);
            _gridSRs.Add(sr);
        }
        for (int z = 0; z <= height; z++)     // Horizontal lines
        {
            var sr = NewSR(_gridRoot, $"H{z}", 0, lineColor);
            sr.transform.SetPositionAndRotation(P(x0 + gw * 0.5f, z0 + z * cellSize), XZ_ROT);
            _gridSRs.Add(sr);
        }
        UpdateGrid();                         // Apply thickness scaling
    }

    /// <summary>Rescales grid lines to match pixel thickness in world units.</summary>
    void UpdateGrid()
    {
        var cam = Cam; if (!cam) return;      // Camera guard
        float thick = PixelsToWorld(cam, lineThicknessPixels); // px→world
        float gw = width * cellSize, gh = height * cellSize;   // Extents
        int idx = 0;                           // Iterate SR list

        for (int x = 0; x <= width; x++)       // Vertical scales
            _gridSRs[idx++].transform.localScale = new Vector3(thick, gh, 1f);

        for (int z = 0; z <= height; z++)      // Horizontal scales
            _gridSRs[idx++].transform.localScale = new Vector3(gw, thick, 1f);
    }

    /// <summary>Creates axis sprites if they cross the grid; then calls <see cref="UpdateAxes"/>.</summary>
    void RebuildAxes()
    {
        ClearChildren(null, _axisSRs);        // Clear axis SRs
        if (!drawAxes) return;                // Skip if off

        float x0 = origin.x, z0 = origin.y;   // Origin
        float gw = width * cellSize, gh = height * cellSize; // Extents
        if (z0 <= 0 && 0 <= z0 + gh)          // If Z=0 crosses grid
            _axisSRs.Add(NewSR(_gridRoot, "AxisX", 1, axisColor));
        if (x0 <= 0 && 0 <= x0 + gw)          // If X=0 crosses grid
            _axisSRs.Add(NewSR(_gridRoot, "AxisZ", 1, axisColor));
        UpdateAxes();                         // Size/rotate axes
    }

    /// <summary>Rescales axis sprites to match pixel thickness and grid span.</summary>
    void UpdateAxes()
    {
        var cam = Cam; if (!cam) return;      // Camera guard
        float thick = PixelsToWorld(cam, lineThicknessPixels); // px→world
        float gw = width * cellSize, gh = height * cellSize;   // Extents
        foreach (var sr in _axisSRs)
        {
            if (!sr) continue;
            bool isXaxis = sr.name == "AxisX";                  // Which axis?
            sr.transform.localScale = isXaxis ? new Vector3(gw, thick, 1f)
                                              : new Vector3(thick, gh, 1f); // Length + thickness
            sr.transform.rotation = XZ_ROT;                      // XY→XZ
            sr.color = axisColor;                               // Color
        }
    }

    /// <summary>Creates wall sprites from <see cref="walls"/> and positions them; then calls <see cref="UpdateWalls"/>.</summary>
    void RebuildWalls()
    {
        ClearChildren(_wallRoot, _wallSRs);  // Remove old wall SRs
        if (walls == null || walls.Count == 0) return; // Nothing to draw

        float x0 = origin.x, z0 = origin.y;  // Origin
        foreach (var s in walls)             // Each segment
        {
            var sr = NewSR(_wallRoot, "Wall", 2, wallColor);
            if (s.start.y == s.end.y)        // Horizontal wall
            {
                float z = z0 + s.start.y * cellSize;
                float xa = x0 + Mathf.Min(s.start.x, s.end.x) * cellSize;
                float xb = x0 + Mathf.Max(s.start.x, s.end.x) * cellSize;
                sr.transform.SetPositionAndRotation(P((xa + xb) * 0.5f, z), XZ_ROT);
            }
            else                              // Vertical wall
            {
                float x = x0 + s.start.x * cellSize;
                float za = z0 + Mathf.Min(s.start.y, s.end.y) * cellSize;
                float zb = z0 + Mathf.Max(s.start.y, s.end.y) * cellSize;
                sr.transform.SetPositionAndRotation(P(x, (za + zb) * 0.5f), XZ_ROT);
            }
            _wallSRs.Add(sr);                 // Track SR
        }
        UpdateWalls();                        // Apply thickness scaling
    }

    /// <summary>Rescales wall sprites to match pixel thickness and segment length.</summary>
    void UpdateWalls()
    {
        var cam = Cam; if (!cam) return;     // Camera guard
        float thick = PixelsToWorld(cam, wallThicknessPixels); // px→world
        float x0 = origin.x, z0 = origin.y; // Origin

        int i = 0;                           // Iterate parallel to walls list
        foreach (var s in walls)
        {
            var sr = _wallSRs[i++]; if (!sr) continue; sr.color = wallColor; // Ensure color
            if (s.start.y == s.end.y)        // Horizontal: scale X by length
            {
                float xa = x0 + Mathf.Min(s.start.x, s.end.x) * cellSize;
                float xb = x0 + Mathf.Max(s.start.x, s.end.x) * cellSize;
                sr.transform.localScale = new Vector3(Mathf.Max(0, xb - xa), thick, 1f);
            }
            else                              // Vertical: scale Z by length
            {
                float za = z0 + Mathf.Min(s.start.y, s.end.y) * cellSize;
                float zb = z0 + Mathf.Max(s.start.y, s.end.y) * cellSize;
                sr.transform.localScale = new Vector3(thick, Mathf.Max(0, zb - za), 1f);
            }
        }
    }

    /// <summary>Ensures enough flash sprites exist and positions/scales them for active flashes.</summary>
    void RebuildFlashes()
    {
        while (_flashSRs.Count < _flashes.Count) // Ensure enough SRs
            _flashSRs.Add(NewSR(_overlayRoot, "Flash", 12, clickFlashColor));
        for (int i = 0; i < _flashSRs.Count; i++) // Enable only used ones
            _flashSRs[i].enabled = i < _flashes.Count;

        var cam = Cam; if (!cam) return;     // Camera guard
        for (int i = 0; i < _flashes.Count; i++) // Place/size per flash
        {
            var f = _flashes[i]; var sr = _flashSRs[i];
            float xa = origin.x + f.cell.x * cellSize;
            float za = origin.y + f.cell.y * cellSize;
            sr.transform.SetPositionAndRotation(P(xa + cellSize * 0.5f, za + cellSize * 0.5f), XZ_ROT);
            sr.transform.localScale = new Vector3(cellSize, cellSize, 1f);
            sr.color = f.color;
        }
    }

    // ---------- Utility ----------
    static void SafeDestroy(Object o)        // Destroy right way for mode
    {
        if (!o) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }

    void ClearChildren(Transform parent, List<SpriteRenderer> list) // Wipe children + list
    {
        if (parent)
            for (int i = parent.childCount - 1; i >= 0; i--)
                SafeDestroy(parent.GetChild(i).gameObject);
        list?.Clear();
    }

    Vector3 P(float x, float z) => new Vector3(x, gridY, z); // World pos on plane

    float PixelsToWorld(Camera cam, float pixels)           // px→world at plane depth
    {
        pixels = Mathf.Max(1f, pixels);                     // Avoid zero
        if (cam.orthographic)                               // Ortho scale
            return (cam.orthographicSize * 2f / Screen.height) * pixels;

        float depth = DepthToXZPlane(cam);                  // Perspective: measure
        Vector3 a = cam.ScreenToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 b = cam.ScreenToWorldPoint(new Vector3(0f, pixels, depth));
        return (b - a).magnitude;
    }

    float DepthToXZPlane(Camera cam)                        // Distance to Y=gridY plane
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, gridY, 0f));
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        return plane.Raycast(ray, out float t) ? t : Mathf.Abs(gridY - cam.transform.position.y);
    }

    bool ScreenToXZPlane(Camera cam, Vector2 screenPos, float planeY, out Vector3 hit) // Screen→plane
    {
        if (cam.orthographic)                               // Ortho projection
        {
            var wp = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(planeY - cam.transform.position.y)));
            hit = new Vector3(wp.x, planeY, wp.z);
            return true;
        }
        var ray = cam.ScreenPointToRay(screenPos);          // Perspective ray
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float t)) { hit = ray.GetPoint(t); return true; }
        hit = default; return false;                        // Missed
    }

    // ---------- Change Detection ----------
    bool CamChanged(Camera cam)                             // Camera or px→world changed?
    {
        if (!cam) return false;
        return _lastCamPos != cam.transform.position ||
               _lastCamRot != cam.transform.rotation ||
               (_lastCamSizeOrFov != (cam.orthographic ? cam.orthographicSize : cam.fieldOfView)) ||
               !Mathf.Approximately(_lastPxWorldGrid, PixelsToWorld(cam, lineThicknessPixels)) ||
               !Mathf.Approximately(_lastPxWorldWall, PixelsToWorld(cam, wallThicknessPixels));
    }

    bool LayoutChanged()                                    // Grid settings changed?
    {
        return _lastW != width ||
               _lastH != height ||
               !Mathf.Approximately(_lastCell, cellSize) ||
               _lastOrigin != origin ||
               !Mathf.Approximately(_lastY, gridY);
    }

    void CacheState(Camera cam)                             // Save snapshot for comparisons
    {
        _lastW = width; _lastH = height; _lastCell = cellSize; _lastOrigin = origin; _lastY = gridY;
        if (cam)
        {
            _lastCamPos = cam.transform.position;
            _lastCamRot = cam.transform.rotation;
            _lastCamSizeOrFov = cam.orthographic ? cam.orthographicSize : cam.fieldOfView;
            _lastPxWorldGrid = PixelsToWorld(cam, lineThicknessPixels);
            _lastPxWorldWall = PixelsToWorld(cam, wallThicknessPixels);
        }
    }

    // ---------- Internals ----------
    Vector2Int WorldToCellXZ(Vector3 w)                     // World→cell (floored)
    {
        int cx = Mathf.FloorToInt((w.x - origin.x) / cellSize);
        int cz = Mathf.FloorToInt((w.z - origin.y) / cellSize);
        return new Vector2Int(cx, cz);
    }

    public bool TryWorldToCellXZ(Vector3 world, out Vector2Int cell) // World→cell if in-bounds
    {
        cell = WorldToCellXZ(world);
        return (uint)cell.x < (uint)width && (uint)cell.y < (uint)height;
    }

    public Vector3 CellCenterWorldXZ(int x, int z, float yOffset = 0f) // Cell center world pos
    {
        float wx = origin.x + (x + 0.5f) * cellSize;
        float wz = origin.y + (z + 0.5f) * cellSize;
        return new Vector3(wx, gridY + yOffset, wz);
    }

    // Add flash
    public void FlashCell(Vector2Int cell, float duration = 0.4f, Color? color = null)
    {
        if ((uint)cell.x >= (uint)width || (uint)cell.y >= (uint)height) return;
        // Clock
        float now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        _flashes.Add(new Flash
        {
            cell = cell,
            until = now + (duration > 0 ? duration : clickFlashDuration),
            color = color ?? clickFlashColor
        });
        RebuildFlashes();                                   // Update visuals
    }

    // ---------- Roots & Sprites ----------
    void EnsureRoots()                                      // Create/find Grid/Walls/Overlay
    {
        Transform Get(string n)
        {
            var t = transform.Find(n);
            if (!t) { var go = new GameObject(n); t = go.transform; t.SetParent(transform, false); }
            return t;
        }
        _gridRoot = Get("Grid");
        _wallRoot = Get("Walls");
        _overlayRoot = Get("Overlay");
    }

    void EnsureWhite()                                      // Make 1x1 white sprite
    {
        if (_white) return;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "GridWhite" };
        tex.SetPixel(0, 0, Color.white); tex.Apply(false, true);
        _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        _white.name = "GridWhiteSprite";
    }

    SpriteRenderer NewSR(Transform parent, string name, int order, Color col) // New SR helper
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _white; sr.color = col; sr.sortingOrder = order;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 2D look
        sr.receiveShadows = false;
        sr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        sr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return sr;
    }

    // ---------- Wall Cache ----------
    void EnsureEdgeArrays()                                 // Allocate/clear H/V arrays
    {
        int hSize = width * (height + 1);
        int vSize = (width + 1) * height;
        if (_h == null || _h.Length != hSize) _h = new bool[hSize];
        if (_v == null || _v.Length != vSize) _v = new bool[vSize];
        System.Array.Clear(_h, 0, _h.Length);
        System.Array.Clear(_v, 0, _v.Length);
    }

    int HIndex(int xCell, int gridlineZ) => gridlineZ * width + xCell;          // H edge id
    int VIndex(int gridlineX, int zCell) => zCell * (width + 1) + gridlineX;    // V edge id

    void RebuildWallCache()                                   // Convert segments→blocked edges
    {
        if (walls == null) { _h = _v = null; return; }        // No walls
        EnsureEdgeArrays();                                   // Ensure arrays
        for (int i = 0; i < walls.Count; i++)                 // Each segment
        {
            var s = walls[i];
            if (s.start.y == s.end.y)                         // Horizontal
            {
                int z = s.start.y;
                int a = Mathf.Min(s.start.x, s.end.x);
                int b = Mathf.Max(s.start.x, s.end.x);
                for (int x = a; x < b; x++)                   // Mark edges
                    if ((uint)x < (uint)width && (uint)z <= (uint)height) _h[HIndex(x, z)] = true;
            }
            else if (s.start.x == s.end.x)                    // Vertical
            {
                int x = s.start.x;
                int a = Mathf.Min(s.start.y, s.end.y);
                int b = Mathf.Max(s.start.y, s.end.y);
                for (int z = a; z < b; z++)                   // Mark edges
                    if ((uint)z < (uint)height && (uint)x <= (uint)width) _v[VIndex(x, z)] = true;
            }
        }
    }

    public bool IsEdgeBlocked(Vector2Int a, Vector2Int b)     // Cardinal neighbor blocked?
    {
        int dx = b.x - a.x, dz = b.y - a.y;
        if (Mathf.Abs(dx) + Mathf.Abs(dz) != 1) return true;  // Not 4-neighbor → treat blocked
        if (dx == 1) return IsVerticalWallBetween(a.x + 1, a.y);
        if (dx == -1) return IsVerticalWallBetween(a.x, a.y);
        if (dz == 1) return IsHorizontalWallBetween(a.x, a.y + 1);
        if (dz == -1) return IsHorizontalWallBetween(a.x, a.y);
        return true;
    }

    bool IsVerticalWallBetween(int gridlineX, int zCell)      // Check V edge cache
    {
        if (_v == null) RebuildWallCache();
        return _v != null &&
               (uint)zCell < (uint)height &&
               (uint)gridlineX <= (uint)width &&
               _v[VIndex(gridlineX, zCell)];
    }

    bool IsHorizontalWallBetween(int xCell, int gridlineZ)    // Check H edge cache
    {
        if (_h == null) RebuildWallCache();
        return _h != null &&
               (uint)xCell < (uint)width &&
               (uint)gridlineZ <= (uint)height &&
               _h[HIndex(xCell, gridlineZ)];
    }
}
