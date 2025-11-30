using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class GridCharacterController3D : MonoBehaviour
{
    // references to grid and prefabs set in inspector
    [Header("References")]
    public GridRenderer3D grid;
    public GameObject prefab;
    public GameObject prefab2;
    public bool autoFindGrid = true;
    public GameObject rangeHighlightPrefab; // Red highlight prefab for reachable tiles

    // how the player appears on grid
    [Header("Spawn")]
    public float yDrawOffset = 0.001f;

    // controls movement speed
    [Header("Movement (XZ only)")]
    public float moveSpeed = 2f;
    [Tooltip("Maximum distance (in grid cells) a character can move in one turn. Set to 0 for unlimited.")]
    public int maxMovementDistance = 9;

    // movement animation settings
    [Header("Animation")]
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;
    public float JumpDuration = 0.5f;
    public Transform dummyTarget;
    public bool allowDiagonalMovement = true;
    public float diagonalCost = 1.414f;

    // path preview settings
    [Header("Path Preview")]
    [Tooltip("Material for the path preview line")]
    public Material pathPreviewMaterial;
    [Tooltip("Width of the path preview line")]
    public float pathPreviewWidth = 0.2f;
    [Tooltip("Height offset for the path preview line above ground")]
    public float pathPreviewHeight = 0.1f;
    [Tooltip("Color for the default path preview")]
    public Color defaultPreviewColor = new Color(1f, 1f, 0f, 0.7f); // Yellow
    [Tooltip("Color for the confirmed path preview")]
    public Color confirmedPreviewColor = new Color(1f, 0f, 0f, 0.7f); // Red
    [Tooltip("Time window for double-click detection (in seconds)")]
    public float doubleClickTime = 0.3f;

    // store both player objects by name 
    // using dictionary for easy access by name in camera
    private Dictionary<string, GameObject> characters = new Dictionary<string, GameObject>();

    // store each player's token movement controller
    // using dictionary for easy access by name in update loop
    private Dictionary<string, ITokenMovement> tokenMovements = new Dictionary<string, ITokenMovement>();

    // reference to main camera manager
    private CameraManager cameraManager;
    // dedicated pathfinding service
    private GridPathfinder pathfinder;
    // flag to check if everything is ready
    private bool isInitialized = false;
    // flag to prevent multiple movements during turn transition
    private bool isProcessingTurn = false;

    // Track current active player
    private string currentPlayer = "Player1";

    // Highlight tracking
    private List<GameObject> rangeHighlightInstances = new List<GameObject>();
    private HashSet<Vector3Int> currentReachableTiles = new HashSet<Vector3Int>();

    // Path preview tracking
    private GameObject pathPreviewObject;
    private LineRenderer pathPreviewLine;
    private List<Vector3Int> previewedPath;
    private bool isPathPreviewed = false;
    private float lastClickTime = 0f;
    private Vector3Int lastClickedCell;

    // called when component is enabled
    void OnEnable()
    {
        // try to find a grid in the scene automatically
        if (autoFindGrid && !grid)
            grid = FindAnyObjectByType<GridRenderer3D>();

        // Register the cell selectability check with the grid
        if (grid != null)
        {
            grid.IsCellSelectable = IsCellSelectableForCurrentCharacter;
        }
    }

    void OnDisable()
    {
        // Unregister the selectability check when disabled
        if (grid != null)
        {
            grid.IsCellSelectable = null;
        }

        // Clean up highlights
        ClearRangeHighlights();
        // Clean up path preview
        ClearPathPreview();
    }

    // called once when game starts
    void Start()
    {
        // initialize Dijkstra pathfinder with current grid settings
        InitializePathfinder();
        // spawn players on grid 
        SpawnCharacters();
        // initialize movement controllers for each player 
        InitializeMovementControllers();
        // initialize camera manager and set up cameras for each player
        InitializeCameraManager();
        // initialize path preview line renderer
        InitializePathPreview();
        // mark as initialized
        isInitialized = true;

        // Show initial reachable tiles for starting player
        UpdateReachableTilesHighlight(currentPlayer);
    }

    // initialize the pathfinding service
    private void InitializePathfinder()
    {
        if (grid != null)
        {
            // create new pathfinder with current grid and settings 
            pathfinder = new GridPathfinder(grid, allowDiagonalMovement, diagonalCost);
        }
        else
        {
            Debug.LogError("[GridCharacterController3D] Grid is null, cannot initialize pathfinder!");
        }
    }

    // set up movement controllers for each character
    private void InitializeMovementControllers()
    {
        foreach (var kvp in characters)
        {
            // create new tokenMovement for each character
            // kvp = KeyValuePair<string, GameObject>
            // kvp used to access dictionary entries
            tokenMovements[kvp.Key] = new tokenMovement(
                kvp.Value.transform, stepHeight, maxRotation, ptLerp, yLerp);
        }
    }

    // set up camera manager and link characters
    private void InitializeCameraManager()
    {
        try
        {
            cameraManager = CameraManager.GetInstance();
            if (cameraManager != null)
            {
                // set main camera
                cameraManager.setCamera(Camera.main);
                // for each kvp = KeyValuePair<string, GameObject> in characters dictionary
                foreach (var kvp in characters)
                {
                    // add character to camera manager using the display name (with space)
                    string displayName = kvp.Key.Replace("Player", "Player ");
                    cameraManager.addActor(displayName, kvp.Value);
                }
                SetCameraForCharacter(currentPlayer, CameraType.Pick);
            }

            foreach (var character in characters.Values)
            {
                SnapToValidCell(character);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("CameraManager error: " + e.Message);
        }
    }

    /// <summary>
    /// Initialize the path preview LineRenderer
    /// </summary>
    private void InitializePathPreview()
    {
        // Create a GameObject to hold the LineRenderer
        pathPreviewObject = new GameObject("PathPreview");
        pathPreviewObject.transform.SetParent(transform);

        // Add and configure LineRenderer
        pathPreviewLine = pathPreviewObject.AddComponent<LineRenderer>();

        // Set up material - create a default one if not assigned
        if (pathPreviewMaterial != null)
        {
            pathPreviewLine.material = pathPreviewMaterial;
        }
        else
        {
            // Create a simple unlit material
            pathPreviewLine.material = new Material(Shader.Find("Sprites/Default"));
        }

        // Configure line properties
        pathPreviewLine.startWidth = pathPreviewWidth;
        pathPreviewLine.endWidth = pathPreviewWidth;
        pathPreviewLine.startColor = defaultPreviewColor;
        pathPreviewLine.endColor = defaultPreviewColor;
        pathPreviewLine.positionCount = 0;
        pathPreviewLine.useWorldSpace = true;

        // Start hidden
        pathPreviewObject.SetActive(false);
    }

    // snap player to nearest valid walkable cell
    void SnapToValidCell(GameObject obj)
    {
        if (!grid) return;

        if (!TryGridWorldToCell(obj.transform.position, out var cell, clamp: true))
            return;

        if (!grid.IsCellWalkable(cell.x, cell.z))
            return;

        obj.transform.position = GridCellCenterWorld(cell.x, cell.z, yDrawOffset);
    }

    // get center world position for given grid coordinates
    Vector3 GridCellCenterWorld(int x, int z, float yOffset = 0f)
    {
        float wx = grid.origin.x + (x + 0.5f) * grid.cellSize;
        float wz = grid.origin.z + (z + 0.5f) * grid.cellSize;
        return new Vector3(wx, grid.gridY + yOffset, wz);
    }

    // update is called once per frame 
    void Update()
    {
        // update pathfinder settings if changed in inspector
        if (pathfinder != null)
        {
            // update diagonal movement settings
            pathfinder.SetDiagonalMovement(allowDiagonalMovement, diagonalCost);
        }

        // check if system is ready for update 
        if (!IsReadyForUpdate(out var cam)) return;
        // get current character data (using current player instead of hardcoded Player1)
        if (!TryGetCurrentCharacterData(currentPlayer, out var currentCharacter, out var currentMovement)) return;
        // handle player input for movement
        HandlePlayerInput(cam, currentPlayer, currentCharacter, currentMovement);
        // update camera manager
        cameraManager?.update();
    }

    // check if system is ready for update
    // private method created to keep Update() clean
    private bool IsReadyForUpdate(out Camera cam)
    {
        cam = null;

        // check if application is playing and system is initialized correctly
        if (!Application.isPlaying || !isInitialized) return false;

        // ensure grid and camera references are valid
        if (!grid)
        {
            if (autoFindGrid) grid = FindAnyObjectByType<GridRenderer3D>();
            if (!grid) return false;
        }

        // get camera reference from grid or main camera
        cam = grid.targetCamera ? grid.targetCamera : Camera.main;
        if (!cam) return false;

        return true;
    }

    // get current character GameObject and movement controller
    // private method created to keep Update() clean and modular
    private bool TryGetCurrentCharacterData(string characterName, out GameObject character, out ITokenMovement movement)
    {
        // default output values 
        return characters.TryGetValue(characterName, out character) &
               tokenMovements.TryGetValue(characterName, out movement);
    }

    // Callback for grid to check if a cell is selectable based on movement range
    private bool IsCellSelectableForCurrentCharacter(Vector3Int targetCell)
    {
        // Don't restrict selection during turn processing
        if (isProcessingTurn)
            return false;

        // Use cached reachable tiles for performance
        return currentReachableTiles.Contains(targetCell);
    }

    // handle player input for movement
    // private method created to keep Update() clean and modular
    private void HandlePlayerInput(Camera cam, string characterName, GameObject character, ITokenMovement movement)
    {
        // Handle right-click to cancel preview
        if (InputCompat.RightClickDown())
        {
            if (isPathPreviewed)
            {
                ClearPathPreview();
                Debug.Log("[GridCharacterController3D] Path preview cancelled.");
            }
            return;
        }

        // check for left mouse click
        if (!InputCompat.LeftClickDown() || isProcessingTurn)
            return;

        // get clicked cell on grid
        if (!TryGetClickedCell(cam, out Vector3Int targetCell)) return;
        // check if target cell is walkable
        if (!grid.IsCellWalkable(targetCell.x, targetCell.z)) return;

        // get character's current position
        Vector3Int startCell = GetCharacterCell(character);

        // check if target is within movement range using cached tiles
        if (!currentReachableTiles.Contains(targetCell))
        {
            Debug.Log($"[GridCharacterController3D] Target cell ({targetCell.x}, {targetCell.z}) is not reachable.");
            return;
        }

        // find path using pathfinder service
        var result = pathfinder.FindPath(startCell, targetCell);

        // if path found, verify path length is within movement distance
        if (result.found && result.path != null)
        {
            // path length check (path includes start cell, so subtract 1 for actual move distance)
            int pathLength = result.path.Count - 1;

            if (maxMovementDistance > 0 && pathLength > maxMovementDistance)
            {
                Debug.Log($"[GridCharacterController3D] Path length ({pathLength}) exceeds maximum movement distance ({maxMovementDistance}).");
                return;
            }

            // Check for double-click
            float timeSinceLastClick = Time.time - lastClickTime;
            bool isDoubleClick = isPathPreviewed &&
                                 lastClickedCell == targetCell &&
                                 timeSinceLastClick <= doubleClickTime;

            if (isDoubleClick)
            {
                // Double-click detected - confirm movement
                Debug.Log("[GridCharacterController3D] Double-click detected - confirming movement.");

                // mark as processing turn to prevent further input until movement ends
                isProcessingTurn = true;
                // Clear highlights during movement
                ClearRangeHighlights();
                // Clear path preview
                ClearPathPreview();
                // start movement coroutine for the character
                StartCoroutine(HandleTurn(character, movement, result.path));
            }
            else
            {
                // Single-click - show/update preview
                Debug.Log("[GridCharacterController3D] Single-click detected - showing path preview.");
                ShowPathPreview(result.path, false);

                // Update click tracking
                lastClickTime = Time.time;
                lastClickedCell = targetCell;
            }
        }
    }

    /// <summary>
    /// Displays a path preview using the LineRenderer
    /// </summary>
    /// <param name="path">The path to preview</param>
    /// <param name="isConfirmed">Whether this is a confirmed path (changes color)</param>
    private void ShowPathPreview(List<Vector3Int> path, bool isConfirmed)
    {
        if (path == null || path.Count < 2)
        {
            ClearPathPreview();
            return;
        }

        // Store the previewed path
        previewedPath = new List<Vector3Int>(path);
        isPathPreviewed = true;

        // Set up the LineRenderer
        pathPreviewLine.positionCount = path.Count;

        // Convert grid cells to world positions
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 worldPos = GridCellCenterWorld(path[i].x, path[i].z, pathPreviewHeight);
            pathPreviewLine.SetPosition(i, worldPos);
        }

        // Set color based on confirmation state
        Color lineColor = isConfirmed ? confirmedPreviewColor : defaultPreviewColor;
        pathPreviewLine.startColor = lineColor;
        pathPreviewLine.endColor = lineColor;

        // Show the preview
        pathPreviewObject.SetActive(true);

        Debug.Log($"[GridCharacterController3D] Path preview shown with {path.Count} waypoints.");
    }

    /// <summary>
    /// Clears the path preview
    /// </summary>
    private void ClearPathPreview()
    {
        if (pathPreviewObject != null)
        {
            pathPreviewObject.SetActive(false);
        }

        if (pathPreviewLine != null)
        {
            pathPreviewLine.positionCount = 0;
        }

        previewedPath = null;
        isPathPreviewed = false;
    }

    /// <summary>
    /// Finds all reachable tiles within movement range using depth-first search.
    /// </summary>
    /// <param name="start">Starting cell position</param>
    /// <param name="maxRange">Maximum movement range</param>
    /// <returns>HashSet of all reachable tiles within range</returns>
    private HashSet<Vector3Int> GetReachableTilesInRange(Vector3Int start, int maxRange)
    {
        // Return all tiles if unlimited range
        if (maxRange <= 0)
        {
            HashSet<Vector3Int> allTiles = new HashSet<Vector3Int>();
            for (int x = 0; x < grid.width; x++)
            {
                for (int z = 0; z < grid.height; z++)
                {
                    if (grid.IsCellWalkable(x, z))
                        allTiles.Add(new Vector3Int(x, 0, z));
                }
            }
            return allTiles;
        }

        HashSet<Vector3Int> reachable = new HashSet<Vector3Int>();
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        Stack<(Vector3Int cell, float cost)> stack = new Stack<(Vector3Int, float)>();

        // Start DFS
        stack.Push((start, 0f));

        while (stack.Count > 0)
        {
            var (current, currentCost) = stack.Pop();

            // Skip if already visited
            if (visited.Contains(current))
                continue;

            visited.Add(current);

            // Add to reachable if within range and walkable
            if (currentCost <= maxRange && grid.IsCellWalkable(current.x, current.z))
            {
                reachable.Add(current);

                // Explore neighbors
                Vector3Int[] neighbors = GetNeighbors(current);
                foreach (var neighbor in neighbors)
                {
                    // Check bounds and walkability
                    if (neighbor.x < 0 || neighbor.x >= grid.width ||
                        neighbor.z < 0 || neighbor.z >= grid.height)
                        continue;

                    if (!grid.IsCellWalkable(neighbor.x, neighbor.z))
                        continue;

                    if (visited.Contains(neighbor))
                        continue;

                    // Calculate movement cost
                    float moveCost = GetMovementCost(current, neighbor);
                    float newCost = currentCost + moveCost;

                    // Only add if within range
                    if (newCost <= maxRange)
                    {
                        stack.Push((neighbor, newCost));
                    }
                }
            }
        }

        return reachable;
    }

    /// <summary>
    /// Gets neighboring cells based on movement settings (cardinal and/or diagonal).
    /// </summary>
    private Vector3Int[] GetNeighbors(Vector3Int cell)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>();

        // Cardinal directions
        Vector3Int[] cardinalDirections = new[]
        {
            new Vector3Int(1, 0, 0),   // East
            new Vector3Int(-1, 0, 0),  // West
            new Vector3Int(0, 0, 1),   // North
            new Vector3Int(0, 0, -1)   // South
        };

        foreach (var dir in cardinalDirections)
        {
            neighbors.Add(cell + dir);
        }

        // Diagonal directions if allowed
        if (allowDiagonalMovement)
        {
            Vector3Int[] diagonalDirections = new[]
            {
                new Vector3Int(1, 0, 1),   // Northeast
                new Vector3Int(-1, 0, 1),  // Northwest
                new Vector3Int(1, 0, -1),  // Southeast
                new Vector3Int(-1, 0, -1)  // Southwest
            };

            foreach (var dir in diagonalDirections)
            {
                // Check if diagonal movement is valid (adjacent cells walkable)
                Vector3Int adjacent1 = cell + new Vector3Int(dir.x, 0, 0);
                Vector3Int adjacent2 = cell + new Vector3Int(0, 0, dir.z);

                bool adjacent1Valid = adjacent1.x >= 0 && adjacent1.x < grid.width &&
                                     adjacent1.z >= 0 && adjacent1.z < grid.height &&
                                     grid.IsCellWalkable(adjacent1.x, adjacent1.z);

                bool adjacent2Valid = adjacent2.x >= 0 && adjacent2.x < grid.width &&
                                     adjacent2.z >= 0 && adjacent2.z < grid.height &&
                                     grid.IsCellWalkable(adjacent2.x, adjacent2.z);

                if (adjacent1Valid && adjacent2Valid)
                {
                    neighbors.Add(cell + dir);
                }
            }
        }

        return neighbors.ToArray();
    }

    /// <summary>
    /// Calculates movement cost between two adjacent cells.
    /// </summary>
    private float GetMovementCost(Vector3Int from, Vector3Int to)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dz = Mathf.Abs(to.z - from.z);

        // Diagonal movement
        if (dx == 1 && dz == 1)
            return diagonalCost;

        // Cardinal movement
        return 1f;
    }

    /// <summary>
    /// Updates and displays reachable tile highlights for a character.
    /// </summary>
    private void UpdateReachableTilesHighlight(string characterName)
    {
        // Clear existing highlights
        ClearRangeHighlights();

        // Get character
        if (!characters.TryGetValue(characterName, out GameObject character))
            return;

        // Get character position
        Vector3Int startCell = GetCharacterCell(character);

        // Calculate reachable tiles
        currentReachableTiles = GetReachableTilesInRange(startCell, maxMovementDistance);

        // Create highlights if prefab is assigned
        if (rangeHighlightPrefab != null)
        {
            foreach (var cell in currentReachableTiles)
            {
                // Skip the starting cell
                if (cell.Equals(startCell))
                    continue;

                // Instantiate highlight
                GameObject highlight = Instantiate(rangeHighlightPrefab);
                highlight.name = $"RangeHighlight_{cell.x}_{cell.z}";

                // Position and scale highlight
                Vector3 worldPos = GridCellCenterWorld(cell.x, cell.z, 0.05f);
                highlight.transform.position = worldPos;
                highlight.transform.localScale = new Vector3(grid.cellSize * 0.1f, 1f, grid.cellSize * 0.1f);

                // Set red color
                var renderer = highlight.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(1f, 0f, 0f, 0.5f); // Red with transparency
                }

                rangeHighlightInstances.Add(highlight);
            }
        }
    }

    /// <summary>
    /// Clears all range highlight instances.
    /// </summary>
    private void ClearRangeHighlights()
    {
        foreach (var highlight in rangeHighlightInstances)
        {
            if (highlight != null)
                Destroy(highlight);
        }
        rangeHighlightInstances.Clear();
    }

    // check if target cell is within movement range using Manhattan distance as initial filter
    private bool IsWithinMovementRange(Vector3Int start, Vector3Int target)
    {
        // if maxMovementDistance is 0 or negative, allow unlimited movement
        if (maxMovementDistance <= 0)
            return true;

        // calculate Manhattan distance as a quick filter
        int manhattanDistance = Mathf.Abs(target.x - start.x) + Mathf.Abs(target.z - start.z);

        // if Manhattan distance exceeds max range, definitely out of range
        // (actual pathfinding distance will be equal or greater)
        if (manhattanDistance > maxMovementDistance)
            return false;

        // if diagonal movement is disabled, Manhattan distance is exact
        if (!allowDiagonalMovement)
            return manhattanDistance <= maxMovementDistance;

        // with diagonal movement, we need to check actual pathfinding distance
        // for cells close to the limit
        var result = pathfinder.FindPath(start, target);

        if (!result.found || result.path == null)
            return false;

        // path includes start cell, so subtract 1 for actual distance
        int actualDistance = result.path.Count - 1;
        return actualDistance <= maxMovementDistance;
    }

    // get grid cell from mouse click position
    private bool TryGetClickedCell(Camera cam, out Vector3Int targetCell)
    {
        // default output value
        targetCell = Vector3Int.zero;

        // convert mouse position to world hit on grid plane 
        if (!ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), grid.gridY, out Vector3 hit))
            return false;

        // convert world hit to grid cell
        return TryGridWorldToCell(hit, out targetCell);
    }

    // get current character's grid cell position 
    private Vector3Int GetCharacterCell(GameObject character)
    {
        // convert character world position to grid cell
        TryGridWorldToCell(character.transform.position, out Vector3Int cell, clamp: true);
        return cell;
    }

    /// <summary>
    /// Switches to the next player's turn
    /// </summary>
    private void SwitchToNextPlayer()
    {
        // Toggle between Player1 and Player2
        currentPlayer = currentPlayer == "Player1" ? "Player2" : "Player1";

        Debug.Log($"[GridCharacterController3D] Switching to {currentPlayer}'s turn");

        // Update camera for new player
        SetCameraForCharacter(currentPlayer, CameraType.Pick);

        // Update reachable tiles for new player
        UpdateReachableTilesHighlight(currentPlayer);
    }

    // coroutine that handles one player's full move
    // defines the sequence of actions during a turn 
    // using Collections primarily because of List<T> in path, which allows easy manipulation of grid cell positions
    private System.Collections.IEnumerator HandleTurn(GameObject actor, ITokenMovement movement, List<Vector3Int> path)
    {
        // define path for movement controller
        movement.setPath(path);

        // small pause before move starts
        yield return new WaitForSeconds(0.3f);

        // start movement
        movement.start();


        // wait until movement completes
        while (movement.IsMoving())
        {
            // Update the movement every frame
            // old position
            yield return movement.update();
            // new position 
            // update character position
        }

        // short pause when reaching destination
        yield return new WaitForSeconds(1.2f);

        // focus camera on character after move (use current player instead of hardcoded Player1)
        string displayName = actor.name; // Already has space from SpawnCharacter
        SetCameraForCharacter(currentPlayer, CameraType.Focus);

        // small pause for dramatic effect
        yield return new WaitForSeconds(0.3f);

        // Switch to next player after current player completes their turn
        SwitchToNextPlayer();

        // reset flag to allow next movement
        isProcessingTurn = false;
    }

    // spawn both characters on the grid
    void SpawnCharacters()
    {
        // calculate y position with offset for drawing above grid
        float yPos = grid ? grid.gridY + yDrawOffset : 0.001f;

        if (prefab == null)
        {
            Debug.LogError("[GridCharacterController3D] prefab is not assigned in the Inspector!");
            return;
        }

        // Use prefab for Player1
        SpawnCharacter("Player1", prefab, new Vector3(0f, yPos, 0f), Color.white);

        // Use prefab2 if assigned, otherwise fall back to prefab for Player2
        GameObject player2Prefab = prefab2 != null ? prefab2 : prefab;
        SpawnCharacter("Player2", player2Prefab, new Vector3(18.5f, yPos, 1.5f), Color.red);
    }

    // spawn a single character on the grid
    private void SpawnCharacter(string name, GameObject prefab, Vector3 position, Color color)
    {
        // instantiate player prefab
        GameObject player = Instantiate(prefab);
        // Set display name with space for visual purposes
        player.name = name.Replace("Player", "Player "); // "Player1" -> "Player 1"
        player.transform.position = position;
        // Store in dictionary with original name (without space) for consistent lookups
        characters[name] = player;

        // set player color if applicable
        var renderer = player.GetComponent<MeshRenderer>();
        if (renderer && color != Color.white)
        {
            renderer.material.color = color;
        }
    }

    // convert mouse position to world hit on plane
    bool ScreenToXZPlane(Camera cam, Vector2 screenPos, float planeY, out Vector3 hit)
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

    // convert world position to grid cell index
    bool TryGridWorldToCell(Vector3 world, out Vector3Int cell, bool clamp = false)
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

    // set camera to focus or pick mode for given character
    private void SetCameraForCharacter(string characterName, CameraType mode)
    {
        if (cameraManager != null)
        {
            // Convert dictionary key to display name (with space) for CameraManager
            string displayName = characterName.Replace("Player", "Player ");
            cameraManager.setCurrentActor(displayName);
            cameraManager.setMode(mode);
            cameraManager.ResetClock();
        }
    }

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
}