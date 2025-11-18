using System.Collections.Generic;
using System.Drawing;
using UnityEngine;
using UnityEngine.InputSystem;
[DisallowMultipleComponent]
public class GridCharacterController3D : MonoBehaviour
{
    [Header("References")]
    // Grid to move on
    public GridRenderer3D grid;
    // prefab to visualize the player
    public GameObject prefab;
    // Try to find a GridRenderer automatically if none is assigned
    public bool autoFindGrid = true;
    
    [Header("Spawn")]
    // Sorting order so the character renders above the grid
    public int sortingOrder = 200;

    [Header("Movement (XZ only)")]
    public float moveSpeed = 2f;
    // Slight Y offset so the character draws on top of the grid
    public float yDrawOffset = 0.001f;

    //Ryan's Animation Stuff
    [Header("Animation")]
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;
    public float JumpDuration = 0.5f;
    public Transform dummyTarget;

    //-----------Private variables-------------
    private ITokenMovement tokenMovement;
    private List<Vector3Int> path_buffer = new List<Vector3Int>();
    //used for the one at a time movement
    private bool isProcessingPath = false;
    private Vector3Int startCell;
    private Vector3Int nextCell;
    private bool camLocked = false;

    // Instance of the visualized character (prefab or generated)
    GameObject _character;
    // SpriteRenderer used to display the character
    SpriteRenderer _sr;
    // Current path as a list of grid cells to follow (x,y,z)
    List<Vector3Int> _path = null;
    // Index of the next waypoint in the path
    int _pathIndex = 0;
    // Convenience: true while we still have waypoints to follow
    bool _isMoving => _path != null && _pathIndex < _path.Count;

    private CameraManager cameraManager;
    
    //idea is use this to interupt setting new points
    private bool ableToContinue = true;

    // Called when component becomes active
    void OnEnable()
    {
        // If requested and missing, look up the first GridRenderer in the scene
        if (autoFindGrid && !grid) grid = FindAnyObjectByType<GridRenderer3D>();
    }

    // Called on the first frame the component is active
    void Start()
    {
        // Create or instantiate the character visual
        SpawnCharacter();
        // Move the visual to a valid grid cell center if needed
        SnapToValidCellIfNeeded();

        //Ryan's Animation Stuff: intialize tokenMovement
        tokenMovement = new tokenMovement(_character.transform, stepHeight, maxRotation, ptLerp, yLerp);

        try
        {
            cameraManager = CameraManager.GetInstance();
            cameraManager.addActor("PlayerCharacter", _character);
            cameraManager.setCamera(Camera.main);
            cameraManager.setCurrentActor("PlayerCharacter");
            cameraManager.setMode(CameraType.Pick);
        } catch (System.Exception e)
        {
            Debug.LogError("CameraManager instance not found: " + e.Message);
        }
    }

    // Create the character GameObject and renderer (from prefab or generated circle)
    void SpawnCharacter()
    {
        // If a prefab is provided, instantiate it
        if (prefab)
        {
            // Instantiate with default rotation (no Quaternion ops)
            _character = Instantiate(prefab);
            // Match the prefab's name for clarity
            _character.name = prefab.name;
            // Cache the SpriteRenderer if present
            _sr = _character.GetComponent<SpriteRenderer>();
            // Ensure it draws above the grid
            if (_sr) _sr.sortingOrder = sortingOrder;
        }
        // Place the character at grid Y level (plus tiny offset to avoid z-fighting)
        float yPos;

        if (grid)
            yPos = grid.gridY + yDrawOffset;
        else
            yPos = 0.001f;

        _character.transform.position = new Vector3(0f, yPos, 0f);
    }

    // Per-frame update while playing
    void Update()
    {
        // Ignore updates in edit mode
        if (!Application.isPlaying) return;
        // Ensure we have a grid reference (try auto-find once per frame until found)
        if (!grid)
        {
            if (autoFindGrid) grid = FindAnyObjectByType<GridRenderer3D>();
            if (!grid) return;
        }

        // Use the grid’s camera if set, otherwise the main camera
        var cam = grid.targetCamera ? grid.targetCamera : Camera.main;
        // If no camera exists, we can’t pick cells from the mouse
        if (!cam) return;



        // On left mouse click, compute a path to the clicked cell
        if (InputCompat.LeftClickDown())
        {
            // Convert mouse position to a world hit on the plane Y = gridY
            if (!ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), grid.gridY, out Vector3 hit)) return;

            // Convert that world hit to a target grid cell (fail if outside grid)
            if (!TryGridWorldToCell(hit, out Vector3Int targetCell)) return;

            // Set destination tile,
            // reject clicks on non-walkable cells
            if (!grid.IsCellWalkable(targetCell.x, targetCell.z)) { Debug.Log("Cell is occupied"); return; }


            // Determine the current cell based on character position
            if (!TryGridWorldToCell(_character.transform.position, out startCell))
                startCell = ClampToGridXZ(_character.transform.position);

            // Run Dijkstra (with wall checks) to find a path
            var result = Dijkstra(startCell, targetCell);



            if (result.found && result.path != null)
            {
                _path = result.path;
                isProcessingPath = true;
                tokenMovement.setPath(_path);
                cameraManager.ResetClock();
            }
            else// If no route, clear any existing path
            {
                _path = null;
                _pathIndex = 0;
                isProcessingPath = false;
                Debug.Log("No path found");
            }
            
        }


        //Ryan's Animation Stuff: Camera and movement interactivity
        //////////////////////
        ///WORK IN PROGRESS///
        //////////////////////
        // Stop movement and look at target
        if (Input.GetKeyDown(KeyCode.Q))
        {
            tokenMovement.stop();
            tokenMovement.setLookAt(dummyTarget.position);
            cameraManager.ResetClock();
            cameraManager.setMode(CameraType.Target);
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            cameraManager.ResetClock();
            tokenMovement.start();
            camLocked = false;
        }
        
        // Camera follow logic based on movement state
        if (tokenMovement.IsMoving() && camLocked == false)
        {
            cameraManager.setMode(CameraType.Focus);
            camLocked = true;
        } else if (!tokenMovement.IsMoving() && camLocked == true)
        {
            camLocked = false;
            cameraManager.ResetClock();
            tokenMovement.setLookAt(dummyTarget.position);
            // cameraManager.setMode(CameraType.Target);
            cameraManager.setMode(CameraType.Pick);
        }

        //These need to be running every frame
        //cameraManager.DebugLogCameraManager();
        cameraManager.update();
        StartCoroutine(tokenMovement.update());
    }















    // ===== Helpers for XZ-plane picking and grid conversion =====

    bool ScreenToXZPlane(Camera cam, Vector2 screenPos, float planeY, out Vector3 hit)
    {
        // Orthographic: direct ScreenToWorldPoint at the plane’s depth
        if (cam.orthographic)
        {
            var wp = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(planeY - cam.transform.position.y)));
            hit = new Vector3(wp.x, planeY, wp.z);
            return true;
        }
        // Perspective: raycast the screen ray against the plane
        else
        {
            var ray = cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out float t)) { hit = ray.GetPoint(t); return true; }
        }
        hit = default; return false;
    }

    // Convert a world position to a grid cell (returns false if outside grid bounds)
    bool TryGridWorldToCell(Vector3 world, out Vector3Int cell)
    {
        // Compute cell indices by subtracting origin and dividing by cell size
        int cx = Mathf.FloorToInt((world.x - grid.origin.x) / grid.cellSize);
        int cz = Mathf.FloorToInt((world.z - grid.origin.z) / grid.cellSize);
        // Package in a Vector3Int (y is 0 since we're working on XZ plane)
        cell = new Vector3Int(cx, 0, cz);
        // Ensure indices are inside [0,width) and [0,height)
        return (uint)cx < (uint)grid.width && (uint)cz < (uint)grid.height;
    }

    // Clamp an arbitrary world position to the nearest valid cell indices
    Vector3Int ClampToGridXZ(Vector3 pos)
    {
        // Convert to grid space, floor to cell, then clamp to edges
        int gx = Mathf.Clamp(Mathf.FloorToInt((pos.x - grid.origin.x) / grid.cellSize), 0, grid.width - 1);
        int gz = Mathf.Clamp(Mathf.FloorToInt((pos.z - grid.origin.z) / grid.cellSize), 0, grid.height - 1);
        // Return the clamped cell
        return new Vector3Int(gx, 0, gz);
    }

    // Ensure the character’s current position is at a valid cell center
    void SnapToValidCellIfNeeded()
    {
        // If there’s no grid yet, nothing to do
        if (!grid) return;
        // Try to read the current cell; clamp if out of bounds
        if (!TryGridWorldToCell(_character.transform.position, out var cell))
            cell = ClampToGridXZ(_character.transform.position);

        // if current cell isn't walkable, search a small neighborhood for one
        if (!grid.IsCellWalkable(cell.x, cell.z))
        {
            bool found = false;
            for (int r = 0; r <= 3 && !found; r++)
            {
                for (int dz = -r; dz <= r && !found; dz++)
                    for (int dx = -r; dx <= r && !found; dx++)
                    {
                        int nx = Mathf.Clamp(cell.x + dx, 0, grid.width - 1);
                        int nz = Mathf.Clamp(cell.z + dz, 0, grid.height - 1);
                        if (grid.IsCellWalkable(nx, nz)) { cell = new Vector3Int(nx, 0, nz); found = true; }
                    }
            }
            if (!grid.IsCellWalkable(cell.x, cell.z)) return; // nothing found; keep position as-is
        }

        // Place the character precisely at that cell center
        _character.transform.position = GridCellCenterWorld(cell.x, cell.z, yDrawOffset);
    }

    // Get the world-space coordinates of the center of cell (x,z)
    Vector3 GridCellCenterWorld(int x, int z, float yOffset = 0f)
    {
        // Midpoint in world along X based on origin and cell size
        float wx = grid.origin.x + (x + 0.5f) * grid.cellSize;
        // Midpoint in world along Z based on origin and cell size
        float wz = grid.origin.z + (z + 0.5f) * grid.cellSize;
        // Return a Vector3 at grid Y plus optional offset
        return new Vector3(wx, grid.gridY + yOffset, wz);
    }

    // ===== Dijkstra pathfinding on a 2D grid (with diagonals) =====

    // Node used in the priority queue (position + distance)
    struct Node { public Vector3Int pos; public float dist; }

    // Minimal binary heap for the Dijkstra frontier
    class MinHeap
    {
        // Internal array list for heap storage
        readonly List<Node> _d = new();
        // Number of elements currently in the heap
        public int Count => _d.Count;
        // Insert a node and bubble it up
        public void Push(Node n) { _d.Add(n); SiftUp(_d.Count - 1); }
        // Remove and return the smallest node (distance)
        public Node Pop() { var r = _d[0]; int last = _d.Count - 1; _d[0] = _d[last]; _d.RemoveAt(last); if (_d.Count > 0) SiftDown(0); return r; }
        // Restore heap by moving node upward
        void SiftUp(int i) { while (i > 0) { int p = (i - 1) >> 1; if (_d[i].dist >= _d[p].dist) break; (_d[i], _d[p]) = (_d[p], _d[i]); i = p; } }
        // Restore heap by moving node downward
        void SiftDown(int i) { for (; ; ) { int l = (i << 1) + 1, r = l + 1, s = i; if (l < _d.Count && _d[l].dist < _d[s].dist) s = l; if (r < _d.Count && _d[r].dist < _d[s].dist) s = r; if (s == i) break; (_d[i], _d[s]) = (_d[s], _d[i]); i = s; } }
    }

    // Dijkstra's algorithm from start to target; returns whether found, total distance, and the path
    (bool found, float distance, List<Vector3Int> path) Dijkstra(Vector3Int start, Vector3Int target)
    {
        // Grid width/height and total cells
        int w = grid.width, h = grid.height, total = w * h;
        // Convert (x,z) to linear array index
        int Idx(Vector3Int p) => p.x + p.z * w;

        // Distance array initialized to +∞
        var dist = new float[total];
        for (int i = 0; i < total; i++) dist[i] = float.PositiveInfinity;
        // Predecessor array for path reconstruction
        var prev = new Vector3Int?[total];
        // Min-heap frontier
        var heap = new MinHeap();

        // Start node distance is zero; push into heap
        dist[Idx(start)] = 0f;
        heap.Push(new Node { pos = start, dist = 0f });

        // 8-neighborhood directions (4-cardinal + 4-diagonals)
        var dirs = new Vector3Int[] {
            new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
            new(1, 0, 1), new(1, 0, -1), new(-1, 0, 1), new(-1, 0, -1)
        };
        // Costs for cardinal (1) and diagonal (~√2)
        var costs = new float[] {
            1f, 1f, 1f, 1f,
            1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f
        };

        // Process until frontier is empty
        while (heap.Count > 0)
        {
            // Pop the closest node so far
            var node = heap.Pop();
            // Current position and index
            var u = node.pos; int uIdx = Idx(u);
            // Skip outdated heap entries
            if (node.dist != dist[uIdx]) continue;

            // Early exit if we reached the target
            if (u == target)
            {
                // Rebuild the path by following predecessors
                var path = new List<Vector3Int>();
                var cur = target;
                while (true)
                {
                    path.Add(cur);
                    if (cur.Equals(start)) break;
                    var p = prev[Idx(cur)]; if (!p.HasValue) break; cur = p.Value;
                }
                // Reverse so it goes start→target
                path.Reverse();
                // Return success with final distance and path
                return (true, dist[uIdx], path);
            }

            // Relax edges to neighbors
            for (int i = 0; i < dirs.Length; i++)
            {
                // Neighbor cell
                var v = u + dirs[i];
                // Skip if out of grid bounds
                if ((uint)v.x >= (uint)w || (uint)v.z >= (uint)h) continue;

                // cannot step onto a non-walkable destination
                if (!grid.IsCellWalkable(v.x, v.z)) continue;

                // Skip if a wall blocks this move (including diagonal corner cutting)
                if (IsMoveBlocked(u, v)) continue;

                // Candidate distance via u
                int vIdx = Idx(v);
                float alt = dist[uIdx] + costs[i];
                // If shorter, record and push to heap
                if (alt < dist[vIdx]) { dist[vIdx] = alt; prev[vIdx] = u; heap.Push(new Node { pos = v, dist = alt }); }
            }
        }
        // No path found
        return (false, -1f, null);


    }

    // Check whether moving from u to v is blocked by walls
    bool IsMoveBlocked(Vector3Int u, Vector3Int v)
    {
        // Compute delta components and Manhattan distance
        int dx = v.x - u.x, dy = v.z - u.z;
        int manhattan = Mathf.Abs(dx) + Mathf.Abs(dy);
        // Cardinal move: ask grid if the edge is blocked
        if (manhattan == 1) return grid.IsEdgeBlocked(u, v);
        // Diagonal move: disallow if either orthogonal step is blocked (no corner cutting)
        if (manhattan == 2 && Mathf.Abs(dx) == 1 && Mathf.Abs(dy) == 1)
        {
            // The two orthogonal steps that compose the diagonal
            var stepX = new Vector3Int(u.x + dx, 0, u.z);
            var stepZ = new Vector3Int(u.x, 0, u.z + dy);
            // Block diagonal if either side is blocked
            return grid.IsEdgeBlocked(u, stepX) || grid.IsEdgeBlocked(u, stepZ);
        }
        // Any other move (non-adjacent) is invalid/blocked
        return true;
    }
}

