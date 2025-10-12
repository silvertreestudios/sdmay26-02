using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GridCharacterController : MonoBehaviour
{
    [Header("References")]
    public GridRenderer grid;               // assign in Inspector; auto-found if null
    public GameObject prefab;               // optional visual
    public bool autoFindGrid = true;

    [Header("Spawn (if no prefab)")]
    public string instanceName = "PlayerCircle";
    public Color runtimeColor = Color.black;
    public float runtimeDiameter = 0.5f;
    [Range(32, 1024)] public int runtimeTextureSize = 128;
    public int sortingOrder = 200;

    [Header("Movement (XZ only)")]
    public float moveSpeed = 2f;            // world units/sec on XZ
    public float arrivalThreshold = 0.01f;
    public float yDrawOffset = 0.001f;      // sits slightly above grid so it renders over it

    GameObject _character;
    SpriteRenderer _sr;
    List<Vector2Int> _path = null;          // sequence of cells (x,z)
    int _pathIndex = 0;
    bool _isMoving => _path != null && _pathIndex < _path.Count;

    void OnEnable()
    {
        if (autoFindGrid && !grid) grid = FindObjectOfType<GridRenderer>();
    }

    void Start()
    {
        // Create/instantiate the visual
        SpawnCharacter();
        // Ensure it starts exactly on a valid grid cell
        SnapToValidCellIfNeeded();
    }

    void SpawnCharacter()
    {
        if (prefab)
        {
            // Spawn it at origin
            _character = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _character.name = prefab.name;
            _sr = _character.GetComponent<SpriteRenderer>();
            if (_sr) _sr.sortingOrder = sortingOrder;
        }
        else
        {
            // New GameObject to hold the sprite
            _character = new GameObject(instanceName);
            _sr = _character.AddComponent<SpriteRenderer>();
            _sr.sprite = CreateCircleSprite(runtimeTextureSize, runtimeColor);
            _sr.sortingOrder = sortingOrder;
            float s = runtimeDiameter / _sr.sprite.bounds.size.x;
            _character.transform.localScale = Vector3.one * s;
        }

        // place on grid Y
        _character.transform.position = new Vector3(0f, grid ? grid.gridY + yDrawOffset : 0.001f, 0f);
    }

    void Update()
    {
        // Only move in Play mode
        if (!Application.isPlaying) return;
        if (!grid)
        {
            if (autoFindGrid) grid = FindObjectOfType<GridRenderer>();
            if (!grid) return;
        }

        var cam = grid.targetCamera ? grid.targetCamera : Camera.main;
        if (!cam) return;

        // Left-click → set a new path target
        if (InputCompat.LeftClickDown())
        {
            if (!ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), grid.gridY, out Vector3 hit)) return;

            if (!grid.TryWorldToCellXZ(hit, out Vector2Int targetCell)) return;
            grid.FlashCell(targetCell, grid.clickFlashDuration, grid.clickFlashColor);

            // current cell from character's world position (XZ)
            if (!grid.TryWorldToCellXZ(_character.transform.position, out Vector2Int startCell))
                startCell = ClampToGridXZ(_character.transform.position);

            var result = Dijkstra(startCell, targetCell);
            if (!result.found) { _path = null; }
            else
            {
                _path = result.path ?? new List<Vector2Int>();
                _pathIndex = (_path.Count > 1 && _path[0] == startCell) ? 1 : 0;

                // Snap start to exact center on XZ (lock Y)
                var startCenter = grid.CellCenterWorldXZ(startCell.x, startCell.y, yDrawOffset);
                _character.transform.position = startCenter;
            }
        }

        // Follow path along XZ
        if (_isMoving)
        {
            var cell = _path[_pathIndex];
            Vector3 target = grid.CellCenterWorldXZ(cell.x, cell.y, yDrawOffset);

            var p = _character.transform.position;
            // ensure Y stays locked
            p.y = grid.gridY + yDrawOffset;

            float step = moveSpeed * Time.deltaTime;
            var newPos = Vector3.MoveTowards(p, target, step);
            newPos.y = grid.gridY + yDrawOffset; // hard-lock Y
            _character.transform.position = newPos;

            if ((newPos - target).sqrMagnitude <= arrivalThreshold * arrivalThreshold)
            {
                _pathIndex++;
                if (_pathIndex >= _path.Count) _path = null;
            }
        }
    }

    // -------- Helpers (XZ) --------

    // Raycast from screen to plane Y = gridY
    bool ScreenToXZPlane(Camera cam, Vector2 screenPos, float planeY, out Vector3 hit)
    {
        if (cam.orthographic)
        {
            var wp = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(planeY - cam.transform.position.y)));
            hit = new Vector3(wp.x, planeY, wp.z);
            return true;
        }
        else
        {
            var ray = cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out float t))
            {
                hit = ray.GetPoint(t);
                return true;
            }
        }
        hit = default;
        return false;
    }

    Vector2Int ClampToGridXZ(Vector3 pos)
    {
        int gx = Mathf.Clamp(Mathf.FloorToInt((pos.x - grid.origin.x) / grid.cellSize), 0, grid.width - 1);
        int gz = Mathf.Clamp(Mathf.FloorToInt((pos.z - grid.origin.y) / grid.cellSize), 0, grid.height - 1);
        return new Vector2Int(gx, gz);
    }

    void SnapToValidCellIfNeeded()
    {
        if (!grid) return;
        if (!grid.TryWorldToCellXZ(_character.transform.position, out var cell))
            cell = ClampToGridXZ(_character.transform.position);

        _character.transform.position = grid.CellCenterWorldXZ(cell.x, cell.y, yDrawOffset);
    }

    // -------- Dijkstra on grid (same as yours; cells are (x,z)) --------

    struct Node { public Vector2Int pos; public float dist; }
    class MinHeap
    {
        readonly List<Node> _d = new();
        public int Count => _d.Count;
        public void Push(Node n) { _d.Add(n); SiftUp(_d.Count - 1); }
        public Node Pop() { var r = _d[0]; int last = _d.Count - 1; _d[0] = _d[last]; _d.RemoveAt(last); if (_d.Count > 0) SiftDown(0); return r; }
        void SiftUp(int i) { while (i > 0) { int p = (i - 1) >> 1; if (_d[i].dist >= _d[p].dist) break; (_d[i], _d[p]) = (_d[p], _d[i]); i = p; } }
        void SiftDown(int i) { for (; ; ) { int l = (i << 1) + 1, r = l + 1, s = i; if (l < _d.Count && _d[l].dist < _d[s].dist) s = l; if (r < _d.Count && _d[r].dist < _d[s].dist) s = r; if (s == i) break; (_d[i], _d[s]) = (_d[s], _d[i]); i = s; } }
    }

    (bool found, float distance, List<Vector2Int> path) Dijkstra(Vector2Int start, Vector2Int target)
    {
        int w = grid.width, h = grid.height, total = w * h;
        int Idx(Vector2Int p) => p.x + p.y * w;

        var dist = new float[total];
        for (int i = 0; i < total; i++) dist[i] = float.PositiveInfinity;
        var prev = new Vector2Int?[total];
        var heap = new MinHeap();

        dist[Idx(start)] = 0f;
        heap.Push(new Node { pos = start, dist = 0f });

        var dirs = new Vector2Int[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };
        var costs = new float[] { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

        while (heap.Count > 0)
        {
            var node = heap.Pop();
            var u = node.pos; int uIdx = Idx(u);
            if (node.dist != dist[uIdx]) continue;

            if (u == target)
            {
                var path = new List<Vector2Int>();
                var cur = target;
                while (true)
                {
                    path.Add(cur);
                    if (cur == start) break;
                    var p = prev[Idx(cur)]; if (!p.HasValue) break; cur = p.Value;
                }
                path.Reverse();
                return (true, dist[uIdx], path);
            }

            for (int i = 0; i < dirs.Length; i++)
            {
                var v = u + dirs[i];
                if ((uint)v.x >= (uint)w || (uint)v.y >= (uint)h) continue;

                if (IsMoveBlocked(u, v)) continue;

                int vIdx = Idx(v);
                float alt = dist[uIdx] + costs[i];
                if (alt < dist[vIdx]) { dist[vIdx] = alt; prev[vIdx] = u; heap.Push(new Node { pos = v, dist = alt }); }
            }
        }
        return (false, -1f, null);
    }

    bool IsMoveBlocked(Vector2Int u, Vector2Int v)
    {
        int dx = v.x - u.x, dy = v.y - u.y;
        int manhattan = Mathf.Abs(dx) + Mathf.Abs(dy);
        if (manhattan == 1) return grid.IsEdgeBlocked(u, v);
        if (manhattan == 2 && Mathf.Abs(dx) == 1 && Mathf.Abs(dy) == 1)
        {
            var stepX = new Vector2Int(u.x + dx, u.y);
            var stepY = new Vector2Int(u.x, u.y + dy);
            return grid.IsEdgeBlocked(u, stepX) || grid.IsEdgeBlocked(u, stepY);
        }
        return true;
    }

    // Simple runtime circle
    Sprite CreateCircleSprite(int size, Color color)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        int cx = size / 2, cy = size / 2; float r = (size - 1) * 0.5f; var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx + 0.5f, dy = y - cy + 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01((r - d) / 1.5f);
                tex.SetPixel(x, y, Color.Lerp(clear, color, t));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit: size);
    }
}
