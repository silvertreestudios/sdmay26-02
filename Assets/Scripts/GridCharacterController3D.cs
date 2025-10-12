// Bring in generic collections (List<T>, etc.)
using System.Collections.Generic;
// Bring in Unity engine APIs (MonoBehaviour, Vector3, etc.)
using UnityEngine;

// Prevent multiple instances of this component on the same GameObject
[DisallowMultipleComponent]
public class GridCharacterController : MonoBehaviour
{
    // ===== Inspector: References =====
    // Reference to the GridRenderer in the scene (assigned in Inspector or auto-found)
    [Header("References")]
    public GridRenderer grid;               // assign in Inspector; auto-found if null
    // Optional prefab to use for the character visual (if null, we draw a runtime circle)
    public GameObject prefab;               // optional visual
    // Whether to automatically find a GridRenderer if none is assigned
    public bool autoFindGrid = true;

    // ===== Inspector: Spawn settings (used only if prefab is null) =====
    // Name to give the runtime-generated character GameObject
    [Header("Spawn (if no prefab)")]
    public string instanceName = "PlayerCircle";
    // Color of the runtime-generated circle sprite
    public Color runtimeColor = Color.black;
    // World-space diameter of the runtime-generated circle sprite
    public float runtimeDiameter = 0.5f;
    // Texture resolution of the runtime-generated circle sprite
    [Range(32, 1024)] public int runtimeTextureSize = 128;
    // Sorting order for the SpriteRenderer so it renders on top of the grid
    public int sortingOrder = 200;

    // ===== Inspector: Movement on XZ grid =====
    // Movement speed in world units per second (on the XZ plane)
    [Header("Movement (XZ only)")]
    public float moveSpeed = 2f;            // world units/sec on XZ
    // Distance tolerance to consider we have reached a cell center
    public float arrivalThreshold = 0.01f;
    // Small Y offset so the sprite renders slightly above the grid to avoid z-fighting
    public float yDrawOffset = 0.001f;      // sits slightly above grid so it renders over it

    // Backing field holding the instantiated character object (prefab or runtime circle)
    GameObject _character;
    // Cached SpriteRenderer reference on the character (if any)
    SpriteRenderer _sr;
    // Current path as a sequence of grid cells (x,z) produced by Dijkstra
    List<Vector2Int> _path = null;          // sequence of cells (x,z)
    // Current index into the _path list (the next cell we’re moving toward)
    int _pathIndex = 0;
    // Convenience boolean indicating whether we’re currently following a path
    bool _isMoving => _path != null && _pathIndex < _path.Count;

    // Unity lifecycle: called when the component is enabled
    void OnEnable()
    {
        // If allowed, auto-find a GridRenderer when none is set
        if (autoFindGrid && !grid) grid = FindObjectOfType<GridRenderer>();
    }

    // Unity lifecycle: called on the first frame this script is active
    void Start()
    {
        // Create or instantiate the character visual
        // (prefab if provided, otherwise a generated circle sprite)
        // Create/instantiate the visual
        SpawnCharacter();
        // Snap the character to a valid grid cell center if needed
        // Ensure it starts exactly on a valid grid cell
        SnapToValidCellIfNeeded();
    }

    // Instantiate the character visual (prefab or generated circle)
    void SpawnCharacter()
    {
        // If a prefab is provided, instantiate it
        if (prefab)
        {
            // Spawn the prefab at the world origin (will be repositioned after)
            // Spawn it at origin
            _character = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            // Name it the same as the prefab for clarity
            _character.name = prefab.name;
            // Fetch a SpriteRenderer if present on the prefab
            _sr = _character.GetComponent<SpriteRenderer>();
            // If found, set sorting order so it draws above the grid
            if (_sr) _sr.sortingOrder = sortingOrder;
        }
        else
        {
            // No prefab: create a new GameObject and add a SpriteRenderer
            // New GameObject to hold the sprite
            _character = new GameObject(instanceName);
            // Add SpriteRenderer component
            _sr = _character.AddComponent<SpriteRenderer>();
            // Create a circle sprite at runtime and assign to the renderer
            _sr.sprite = CreateCircleSprite(runtimeTextureSize, runtimeColor);
            // Ensure it renders over the grid lines
            _sr.sortingOrder = sortingOrder;
            // Scale the sprite so its world-space size matches runtimeDiameter
            float s = runtimeDiameter / _sr.sprite.bounds.size.x;
            _character.transform.localScale = Vector3.one * s;
        }

        // Place the character on the grid Y plane (with a slight draw offset)
        // place on grid Y
        _character.transform.position = new Vector3(0f, grid ? grid.gridY + yDrawOffset : 0.001f, 0f);
    }

    // Unity lifecycle: called every frame
    void Update()
    {
        // Only run movement logic while the game is playing (not in Edit mode)
        // Only move in Play mode
        if (!Application.isPlaying) return;
        // Ensure we have a valid GridRenderer reference
        if (!grid)
        {
            // Try to auto-find if allowed
            if (autoFindGrid) grid = FindObjectOfType<GridRenderer>();
            // If still not found, abort this frame
            if (!grid) return;
        }

        // Select a camera to use for screen-to-world conversion (grid’s camera or main)
        var cam = grid.targetCamera ? grid.targetCamera : Camera.main;
        // If no camera is available, abort
        if (!cam) return;

        // Handle mouse input: left click sets a new target cell and computes a path
        // Left-click → set a new path target
        if (InputCompat.LeftClickDown())
        {
            // Convert mouse position to a world hit point on the plane Y = gridY
            if (!ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), grid.gridY, out Vector3 hit)) return;

            // Convert that world position to a grid cell (x,z)
            if (!grid.TryWorldToCellXZ(hit, out Vector2Int targetCell)) return;
            // Briefly flash the clicked cell for feedback
            grid.FlashCell(targetCell, grid.clickFlashDuration, grid.clickFlashColor);

            // Determine the start cell from the character’s current world position
            // (fallback to clamped coordinates if conversion fails)
            // current cell from character's world position (XZ)
            if (!grid.TryWorldToCellXZ(_character.transform.position, out Vector2Int startCell))
                startCell = ClampToGridXZ(_character.transform.position);

            // Run Dijkstra to find a path from startCell to targetCell
            var result = Dijkstra(startCell, targetCell);
            // If no path found, clear current path
            if (!result.found) { _path = null; }
            else
            {
                // Store the computed path (or empty list fallback)
                _path = result.path ?? new List<Vector2Int>();
                // If the first node equals the start, begin from the next node to start moving immediately
                _pathIndex = (_path.Count > 1 && _path[0] == startCell) ? 1 : 0;

                // Snap the character exactly to the center of its start cell (preserve Y offset)
                // Snap start to exact center on XZ (lock Y)
                var startCenter = grid.CellCenterWorldXZ(startCell.x, startCell.y, yDrawOffset);
                _character.transform.position = startCenter;
            }
        }

        // If we have a path, move toward the current target cell center
        // Follow path along XZ
        if (_isMoving)
        {
            // Get the next cell to move toward
            var cell = _path[_pathIndex];
            // Compute its world center on XZ (with Y draw offset)
            Vector3 target = grid.CellCenterWorldXZ(cell.x, cell.y, yDrawOffset);

            // Current position of the character
            var p = _character.transform.position;
            // Force Y to the grid plane + offset (avoid drift)
            // ensure Y stays locked
            p.y = grid.gridY + yDrawOffset;

            // Distance we can move this frame
            float step = moveSpeed * Time.deltaTime;
            // Move toward the target point with constant speed
            var newPos = Vector3.MoveTowards(p, target, step);
            // Re-lock Y after the move
            newPos.y = grid.gridY + yDrawOffset; // hard-lock Y
            // Apply the new position
            _character.transform.position = newPos;

            // If close enough to the target, advance to the next node
            if ((newPos - target).sqrMagnitude <= arrivalThreshold * arrivalThreshold)
            {
                _pathIndex++;
                // If we reached the end of the path, stop moving
                if (_pathIndex >= _path.Count) _path = null;
            }
        }
    }

    // ===== Helpers for XZ-plane interaction =====

    // Cast from the screen position to a horizontal plane at Y = planeY
    // Returns the world hit point if successful
    // Raycast from screen to plane Y = gridY
    bool ScreenToXZPlane(Camera cam, Vector2 screenPos, float planeY, out Vector3 hit)
    {
        // Orthographic camera handling: convert screen directly to world with depth based on plane
        if (cam.orthographic)
        {
            // Map screen to world at the distance from camera to plane
            var wp = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(planeY - cam.transform.position.y)));
            // Place the hit on the plane (override Y)
            hit = new Vector3(wp.x, planeY, wp.z);
            // Success for orthographic case
            return true;
        }
        else
        {
            // Perspective camera: build a ray from the screen point
            var ray = cam.ScreenPointToRay(screenPos);
            // Construct a plane parallel to XZ at height planeY
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            // Intersect the ray with the plane
            if (plane.Raycast(ray, out float t))
            {
                // Get the intersection point
                hit = ray.GetPoint(t);
                // Success for perspective case
                return true;
            }
        }
        // If we reach here, no hit—return default and false
        hit = default;
        return false;
    }

    // Clamp an arbitrary world position to a valid grid cell index (x,z)
    Vector2Int ClampToGridXZ(Vector3 pos)
    {
        // Compute grid x index from world x, clamped to [0, width-1]
        int gx = Mathf.Clamp(Mathf.FloorToInt((pos.x - grid.origin.x) / grid.cellSize), 0, grid.width - 1);
        // Compute grid z index from world z (stored in origin.y), clamped to [0, height-1]
        int gz = Mathf.Clamp(Mathf.FloorToInt((pos.z - grid.origin.y) / grid.cellSize), 0, grid.height - 1);
        // Return the clamped grid coordinates
        return new Vector2Int(gx, gz);
    }

    // If the character is not centered on a valid cell, snap it to the nearest valid cell center
    void SnapToValidCellIfNeeded()
    {
        // If no grid reference, do nothing
        if (!grid) return;
        // Try to convert current world position to a grid cell
        if (!grid.TryWorldToCellXZ(_character.transform.position, out var cell))
            // If that fails, clamp the position to a valid index range
            cell = ClampToGridXZ(_character.transform.position);

        // Move the character to the exact cell center on XZ at the correct Y offset
        _character.transform.position = grid.CellCenterWorldXZ(cell.x, cell.y, yDrawOffset);
    }

    // ===== Dijkstra pathfinding on a uniform-cost 8-connected XZ grid =====
    // Returns (found, total distance, path list)
    // -------- Dijkstra on grid  --------

    // Node representation for heap storage (position + current distance)
    struct Node { public Vector2Int pos; public float dist; }
    // Minimal binary heap for Node based on dist
    class MinHeap
    {
        // Internal dynamic array to store heap items
        readonly List<Node> _d = new();
        // Number of items currently in the heap
        public int Count => _d.Count;
        // Insert a new node and sift it up to restore heap order
        public void Push(Node n) { _d.Add(n); SiftUp(_d.Count - 1); }
        // Remove and return the smallest node (root), then sift down to restore heap
        public Node Pop() { var r = _d[0]; int last = _d.Count - 1; _d[0] = _d[last]; _d.RemoveAt(last); if (_d.Count > 0) SiftDown(0); return r; }
        // Move node at index i up the heap until parent has smaller/equal dist
        void SiftUp(int i) { while (i > 0) { int p = (i - 1) >> 1; if (_d[i].dist >= _d[p].dist) break; (_d[i], _d[p]) = (_d[p], _d[i]); i = p; } }
        // Move node at index i down the heap selecting the smaller child
        void SiftDown(int i) { for (; ; ) { int l = (i << 1) + 1, r = l + 1, s = i; if (l < _d.Count && _d[l].dist < _d[s].dist) s = l; if (r < _d.Count && _d[r].dist < _d[s].dist) s = r; if (s == i) break; (_d[i], _d[s]) = (_d[s], _d[i]); i = s; } }
    }

    // Compute the shortest path from start to target using Dijkstra
    (bool found, float distance, List<Vector2Int> path) Dijkstra(Vector2Int start, Vector2Int target)
    {
        // Cache grid dimensions and total cell count
        int w = grid.width, h = grid.height, total = w * h;
        // Convert a (x,y) cell to a flat array index
        int Idx(Vector2Int p) => p.x + p.y * w;

        // Distance array initialized to +∞
        var dist = new float[total];
        for (int i = 0; i < total; i++) dist[i] = float.PositiveInfinity;
        // Predecessor array for path reconstruction
        var prev = new Vector2Int?[total];
        // Min-heap priority queue for frontier nodes
        var heap = new MinHeap();

        // Seed the start node with distance 0
        dist[Idx(start)] = 0f;
        // Push the start node into the heap
        heap.Push(new Node { pos = start, dist = 0f });

        // 8-connected neighborhood (orthogonal + diagonal)
        var dirs = new Vector2Int[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };
        // Movement costs for each corresponding direction (1 for orthogonal, √2 for diagonal)
        var costs = new float[] { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

        // Main Dijkstra loop until frontier is empty
        while (heap.Count > 0)
        {
            // Pop the node with the smallest tentative distance
            var node = heap.Pop();
            // Current cell
            var u = node.pos; int uIdx = Idx(u);
            // Skip if this heap entry is stale
            if (node.dist != dist[uIdx]) continue;

            // If we reached the target, reconstruct the path
            if (u == target)
            {
                // Path accumulator
                var path = new List<Vector2Int>();
                // Start from the target and walk predecessors back to start
                var cur = target;
                while (true)
                {
                    // Add this cell to the path
                    path.Add(cur);
                    // Stop when we reached the start cell
                    if (cur == start) break;
                    // Fetch predecessor and continue
                    var p = prev[Idx(cur)]; if (!p.HasValue) break; cur = p.Value;
                }
                // Reverse to get start→target order
                path.Reverse();
                // Return success with total distance and path
                return (true, dist[uIdx], path);
            }

            // Explore neighbors of the current cell
            for (int i = 0; i < dirs.Length; i++)
            {
                // Candidate neighbor position
                var v = u + dirs[i];
                // Skip if out of bounds (unsigned compare trick for speed)
                if ((uint)v.x >= (uint)w || (uint)v.y >= (uint)h) continue;

                // Check if movement from u to v is blocked by edges/walls
                if (IsMoveBlocked(u, v)) continue;

                // Flat index for neighbor
                int vIdx = Idx(v);
                // Alternative distance via u
                float alt = dist[uIdx] + costs[i];
                // Relax edge if a shorter path is found
                if (alt < dist[vIdx]) { dist[vIdx] = alt; prev[vIdx] = u; heap.Push(new Node { pos = v, dist = alt }); }
            }
        }
        // If we empty the heap without finding the target, return failure
        return (false, -1f, null);
    }

    // Check whether moving from u to v is blocked (handles diagonals/corner cutting)
    bool IsMoveBlocked(Vector2Int u, Vector2Int v)
    {
        // Delta along x and y
        int dx = v.x - u.x, dy = v.y - u.y;
        // Manhattan distance to categorize orthogonal vs diagonal
        int manhattan = Mathf.Abs(dx) + Mathf.Abs(dy);
        // Orthogonal move: delegate to grid edge blocker
        if (manhattan == 1) return grid.IsEdgeBlocked(u, v);
        // Diagonal move: disallow if either orthogonal step is blocked (prevents corner cut)
        if (manhattan == 2 && Mathf.Abs(dx) == 1 && Mathf.Abs(dy) == 1)
        {
            // Intermediate step in x direction
            var stepX = new Vector2Int(u.x + dx, u.y);
            // Intermediate step in y direction
            var stepY = new Vector2Int(u.x, u.y + dy);
            // Block if either edge is blocked
            return grid.IsEdgeBlocked(u, stepX) || grid.IsEdgeBlocked(u, stepY);
        }
        // Any other move shape is invalid for our neighborhood
        return true;
    }

    // Create a simple anti-aliased filled circle sprite at runtime
    // Simple runtime circle
    Sprite CreateCircleSprite(int size, Color color)
    {
        // Create a new ARGB32 texture; disable mipmaps; set bilinear filtering; clamp edges
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        // Center coordinates and radius in pixels
        int cx = size / 2, cy = size / 2; float r = (size - 1) * 0.5f; var clear = new Color(0, 0, 0, 0);
        // Iterate over each pixel row
        for (int y = 0; y < size; y++)
            // Iterate over each pixel column
            for (int x = 0; x < size; x++)
            {
                // Offset from center (0.5 bias for pixel center sampling)
                float dx = x - cx + 0.5f, dy = y - cy + 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                // Edge softness factor for a simple AA falloff
                float t = Mathf.Clamp01((r - d) / 1.5f);
                // Lerp from transparent to solid color based on falloff
                tex.SetPixel(x, y, Color.Lerp(clear, color, t));
            }
        // Upload pixel changes to the GPU
        tex.Apply();
        // Create and return a Sprite with pixels-per-unit equal to texture size (1 unit diameter ~= size px)
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit: size);
    }
}
