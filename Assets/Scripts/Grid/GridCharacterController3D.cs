using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GridCharacterController3D : MonoBehaviour
{
    // Singleton instance
    private static GridCharacterController3D instance;
    public static GridCharacterController3D Instance => instance;

    // References to grid and prefabs set in inspector
    public GridRenderer3D grid;
    public GameObject prefab;
    public GameObject prefab2;
    public bool autoFindGrid = true;
    public GameObject rangeHighlightPrefab;

    // Spawn settings
    public float yDrawOffset = 0.001f;

    // Movement settings
    public float moveSpeed = 2f;
    public int maxMovementDistance = 9;

    // Animation settings
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;
    public float JumpDuration = 0.5f;
    public Transform dummyTarget;
    public bool allowDiagonalMovement = true;
    public float diagonalCost = 1.414f;

    // Visual indicator settings
    public Material indicatorMaterial;
    public float indicatorWidth = 0.2f;
    public float indicatorHeight = 0.1f;
    public Color defaultIndicatorColor = new Color(1f, 1f, 0f, 0.7f);
    public Color confirmedIndicatorColor = new Color(1f, 0f, 0f, 0.7f);
    public float doubleClickTime = 0.3f;
    public Color rangeHighlightColor = new Color(1f, 0f, 0f, 0.5f);
    public float rangeHighlightHeightOffset = 0.05f;

    // Character storage
    private Dictionary<GameObject, GameObject> characters = new Dictionary<GameObject, GameObject>();
    private Dictionary<GameObject, ITokenMovement> tokenMovements = new Dictionary<GameObject, ITokenMovement>();

    // Subsystem references
    private CameraManager camMan;
    // logic for pathfinding and movement
    private GridPathfinder pathfinder;
    // logic for visual indicator
    private VisualIndicator visualIndicator;
    // logic for movement range highlighting
    private MovementRange rangeHighlighter;
    // logic for converting between grid and world coordinates
    private GridCoordinateConverter coordinateConverter;

    // State flags
    private bool isInitialized = false;
    // Whether a turn is being processed
    private bool isProcessingTurn = false;
    private GameObject currentPlayer = null;

    // Input tracking
    private float lastClickTime = 0f;
    // Last clicked cell position
    private Vector3Int lastClickedCell;

    void Awake()
    {
        // Set up singleton
        if (instance != null && instance != this)
        { 
            Destroy(this.gameObject);
            return;
        }
        instance = this;
    }

    void OnEnable()
    {
        // Auto-find grid if needed
        if (autoFindGrid && !grid)
            grid = FindAnyObjectByType<GridRenderer3D>();

        // Register cell selectability check with grid
        if (grid != null)
        {
            grid.IsCellSelectable = IsCellSelectableForCurrentCharacter;
        }
    }

    void OnDisable()
    {
        // Unregister selectability check
        if (grid != null)
        {
            grid.IsCellSelectable = null;
        }

        // Clean up subsystems
        rangeHighlighter?.ClearHighlights();
        visualIndicator?.Clear();
    }

    /// <summary>
    /// Initializes the character controller and subsystems
    /// </summary>
    void Start()
    {
        InitializeCoordinateConverter();
        InitializePathfinder();
        SpawnCharacters();
        InitializeMovementControllers();
        InitializeCameraManager();
        InitializeSubsystems();
        // Mark as initialized
        isInitialized = true;

        // Start combat after initialization
        StartCoroutine(StartCombatAfterDelay());
    }

    void Update()
    {
        // Update pathfinder settings if changed in inspector
        if (pathfinder != null)
        {
            pathfinder.SetDiagonalMovement(allowDiagonalMovement, diagonalCost);
        }

        // Check if system is ready
        if (!IsReadyForUpdate(out var cam)) return;
        // Get current character data
        if (!TryGetCurrentCharacterData(currentPlayer, out var currentMovement))
            return;
        // Handle player input
        HandlePlayerInput(cam, currentPlayer, currentMovement);

        // Update camera
        camMan?.update();

        //test for the emination code: when press G, run get occupants in area for current player with range 3
        if (Input.GetKeyDown(KeyCode.G))
        {
            List<GameObject> occupants = GetOccupantsInArea(currentPlayer, 2);
            Debug.Log($"[GridCharacterController3D] Occupants in area: {occupants.Count}");
            foreach (var obj in occupants)
            {
                Debug.Log($" - {obj.name}");
            }
        }
        //reset highlights hen press H
        if (Input.GetKeyDown(KeyCode.H))
        {
            Vector3Int startCell = coordinateConverter.GetCharacterCell(currentPlayer);
            rangeHighlighter.UpdateHighlights(startCell, maxMovementDistance);
            Debug.Log("[GridCharacterController3D] Highlights reset.");
        }
    }

    /// <summary>
    /// Starts combat after a short delay to ensure all components are initialized
    /// </summary>
    private IEnumerator StartCombatAfterDelay()
    {
        yield return null;

        CombatManagerInterface combatManager = CombatManagerInterface.GetInstance();
        if (combatManager != null)
        {
            Debug.Log("[GridCharacterController3D] Starting combat...");
            combatManager.StartCombat();
        }
        else
        {
            Debug.LogError("[GridCharacterController3D] CombatManager not found!");
        }
    }

    /// <summary>
    /// Initializes the coordinate converter
    /// </summary>
    private void InitializeCoordinateConverter()
    {
        if (grid != null)
        {
            coordinateConverter = new GridCoordinateConverter(grid);
        }
        else
        {
            Debug.LogError("[GridCharacterController3D] Grid is null, cannot initialize coordinate converter!");
        }
    }

    /// <summary>
    /// Initializes the pathfinder
    /// </summary>
    private void InitializePathfinder()
    {
        if (grid != null)
        {
            pathfinder = new GridPathfinder(grid, allowDiagonalMovement, diagonalCost);
        }
        else
        {
            Debug.LogError("[GridCharacterController3D] Grid is null, cannot initialize pathfinder!");
        }
    }

    /// <summary>
    /// initializes movement controllers for each character
    /// </summary>
    private void InitializeMovementControllers()
    {
        // Create movement controllers for each character
        // key: character name, value: movement controller
        foreach (var kvp in characters)
        {
            // logic for moving tokens on the grid
            tokenMovements[kvp.Key] = new tokenMovement(
                kvp.Value.transform, stepHeight, maxRotation, ptLerp, yLerp);
        }
    }

    private void InitializeCameraManager()
    {
        try
        {
            camMan = CameraManager.GetInstance();
            if (camMan != null)
            {
                // set main camera
                camMan.setCamera(Camera.main);
                // for each kvp = KeyValuePair<string, GameObject> in characters dictionary
                foreach (var kvp in characters)
                {
                    // add character to camera manager
                    camMan.addActor(kvp.Key);
                }
                camMan.SetCameraForCharacter("Player1", CameraType.Pick);
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
    /// Initialize subsystems: VisualIndicator and MovementRangeHighlighter
    /// </summary>
    private void InitializeSubsystems()
    {
        // Initialize visual indicator
        visualIndicator = new VisualIndicator(
            parent: transform,
            material: indicatorMaterial,
            width: indicatorWidth,
            height: indicatorHeight,
            defaultPreviewColor: defaultIndicatorColor,
            confirmedPreviewColor: confirmedIndicatorColor,
            gridCellToWorld: coordinateConverter.GridCellCenterWorld);

        // Initialize movement range highlighter
        rangeHighlighter = new MovementRange(
            gridReference: grid,
            prefab: rangeHighlightPrefab,
            color: rangeHighlightColor,
            heightOffset: rangeHighlightHeightOffset,
            allowDiagonal: allowDiagonalMovement,
            diagCost: diagonalCost,
            gridCellToWorld: coordinateConverter.GridCellCenterWorld);

        Debug.Log("[GridCharacterController3D] Subsystems initialized.");
    }

    /// <summary>
    /// Checks if the system is prepared for update operations 
    /// and retrieves the active camera if ready
    /// </summary>
    /// <param name="cam"></param>
    /// <returns></returns>
    private bool IsReadyForUpdate(out Camera cam)
    {
        cam = null;
        // 
        if (!Application.isPlaying || !isInitialized) return false;

        if (!grid)
        {
            if (autoFindGrid) grid = FindAnyObjectByType<GridRenderer3D>();
            if (!grid) return false;
        }

        cam = grid.targetCamera ? grid.targetCamera : Camera.main;
        if (!cam) return false;

        return true;
    }

    private bool TryGetCurrentCharacterData(GameObject characterName, out ITokenMovement movement)
    {
        movement = null;
        return characters.ContainsKey(characterName) && 
               tokenMovements.TryGetValue(characterName, out movement);
    }

    private bool IsCellSelectableForCurrentCharacter(Vector3Int targetCell)
    {
        if (isProcessingTurn)
            return false;

        return rangeHighlighter.IsCellReachable(targetCell);
    }

    private void HandlePlayerInput(Camera cam, GameObject character, ITokenMovement movement)
    {
        // Handle right-click to cancel indicator
        if (InputCompat.RightClickDown())
        {
            if (visualIndicator.IsActive)
            {
                visualIndicator.Clear();
                Debug.Log("[GridCharacterController3D] Visual indicator cancelled.");
            }
            return;
        }

        // Check for left mouse click
        if (!InputCompat.LeftClickDown() || isProcessingTurn)
            return;

        // Validate and get path on left click
        if (TryValidateAndGetPath(cam, character, out List<Vector3Int> path))
        {
            // Check for double-click
            float timeSinceLastClick = Time.time - lastClickTime;
            bool isDoubleClick = visualIndicator.IsActive &&
                                 lastClickedCell == path[path.Count - 1] &&
                                 timeSinceLastClick <= doubleClickTime;

            if (isDoubleClick)
            {
                // Double-click detected - confirm movement
                Debug.Log("[GridCharacterController3D] Double-click detected - confirming movement.");

                isProcessingTurn = true;
                rangeHighlighter.ClearHighlights();
                visualIndicator.Clear();

                StartCoroutine(HandleTurn(character, movement, path));
            }
            else
            {
                // Single-click - show/update indicator
                Debug.Log("[GridCharacterController3D] Single-click detected - showing visual indicator.");
                visualIndicator.ShowPath(path, false);

                // Update click tracking
                lastClickTime = Time.time;
                lastClickedCell = path[path.Count - 1];
            }
        }
    }

    private bool TryValidateAndGetPath(Camera cam, GameObject character, out List<Vector3Int> path)
    {
        path = null;

        // Early exit: validate clicked cell exists
        if (!TryGetClickedCell(cam, out Vector3Int targetCell))
            return false;

        // Early exit: check if target is walkable and reachable (cheap checks first)
        if (!grid.IsCellWalkable(targetCell))
            return false;

        if (!rangeHighlighter.IsCellReachable(targetCell))
            return false;

        // Get start position
        Vector3Int startCell = coordinateConverter.GetCharacterCell(character);

        // Perform pathfinding (expensive operation)
        var pathResult = pathfinder.FindPath(startCell, targetCell);

        // Validate path exists
        if (!pathResult.found || pathResult.path == null || pathResult.path.Count < 2)
            return false;

        // Validate path length (if movement distance is limited)
        if (maxMovementDistance > 0)
        {
            int pathSteps = pathResult.path.Count - 1; // Exclude starting position
            if (pathSteps > maxMovementDistance)
                return false;
        }

        path = pathResult.path;
        return true;
    }

    private bool TryGetClickedCell(Camera cam, out Vector3Int targetCell)
    {
        targetCell = Vector3Int.zero;

        if (!coordinateConverter.ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), grid.gridY, out Vector3 hit))
            return false;

        return coordinateConverter.TryGridWorldToCell(hit, out targetCell);
    }

    /// <summary>
    /// Handles the turn execution for a character along a given path
    /// </summary>
    /// <param name="actor"></param> the character GameObject
    /// <param name="movement"></param> the movement controller for the character
    /// <param name="path"></param>
    /// <returns></returns>
    private IEnumerator HandleTurn(GameObject actor, ITokenMovement movement, List<Vector3Int> path)
    {
        movement.setPath(path);

        yield return new WaitForSeconds(0.3f);

        movement.start();
        // track last known cell for grid occupancy
        Vector3Int lastCell = coordinateConverter.GetCharacterCell(actor);

        // wait until movement completes
        while (movement.IsMoving())
        {
            // advance movement one frame
            yield return movement.update();

            // check current cell
            Vector3Int currentCell = coordinateConverter.GetCharacterCell(actor);

            // only touch grid if the actor actually entered a new cell
            if (currentCell != lastCell)
            {
                grid.MoveCreaturePosition(actor, currentCell, lastCell);
                lastCell = currentCell;
            }
        }

        // final safety update in case we ended exactly on a boundary
        Vector3Int finalCell = coordinateConverter.GetCharacterCell(actor);
        if (finalCell != lastCell)
        {
            grid.MoveCreaturePosition(actor, finalCell, lastCell);
        }

        yield return new WaitForSeconds(0.5f);

        isProcessingTurn = false;

        Debug.Log("[GridCharacterController3D] Movement completed.");
    }

    #region Character Spawning

    void SpawnCharacters()
    {
        float yPos = grid ? grid.gridY + yDrawOffset : 0.001f;

        if (prefab == null)
        {
            Debug.LogError("[GridCharacterController3D] prefab is not assigned in the Inspector!");
            return;
        }

        SpawnCharacter(prefab, new Vector3(.5f, yPos, .5f), Color.white);

        GameObject player2Prefab = prefab2 != null ? prefab2 : prefab;
        SpawnCharacter(player2Prefab, new Vector3(18.5f, yPos, 1.5f), Color.red);
    }

    private void SpawnCharacter(GameObject prefab, Vector3 position, Color color)
    {
        // Instantiate character GameObject
        GameObject player = Instantiate(prefab);
        player.name = name.Replace("Player", "Player ");
        player.transform.position = position;
        characters[player] = player;
        currentPlayer = currentPlayer ?? player;

        var renderer = player.GetComponent<MeshRenderer>();
        // Apply color if not white
        if (renderer && color != Color.white)
        {
            renderer.material.color = color;
        }

        //ANOTHER TEMP FIX, grid IS PROBABLY NOT THE RIGHT WAY TO CALL THESE METHODS BUT IDK HOW ELSE TO DO IT
        grid.SetCreaturePosition(player, coordinateConverter.GetCharacterCell(player));
    }

    void SnapToValidCell(GameObject obj)
    {
        if (!grid) return;

        if (!coordinateConverter.TryGridWorldToCell(obj.transform.position, out var cell, clamp: true))
            return;

        if (!grid.IsCellWalkable(cell))
            return;

        obj.transform.position = coordinateConverter.GridCellCenterWorld(cell.x, cell.z, yDrawOffset);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Gets the currently previewed path for a character
    /// </summary>
    public List<Vector3Int> GetPreviewedPathForCharacter(GameObject characterName)
    {
        if (characterName == currentPlayer && visualIndicator.IsActive)
        {
            return visualIndicator.CurrentPath;
        }
        return null;
    }

    /// <summary>
    /// Gets the character GameObject by name
    /// </summary>
    //public GameObject GetCharacter(string characterName)
    //{
    //    characters.TryGetValue(characterName, out GameObject character);
    //    return character;
    //}

    /// <summary>
    /// Gets the movement controller for a character
    /// </summary>
    public ITokenMovement GetMovementController(GameObject characterName)
    {
        tokenMovements.TryGetValue(characterName, out ITokenMovement movement);
        return movement;
    }

    /// <summary>
    /// Sets the active player and updates highlights
    /// </summary>
    public void SetActivePlayer(GameObject characterName)
    {
        Debug.Log("Setting active Player");
        rangeHighlighter.ClearHighlights();
        visualIndicator.Clear();

        GameObject currentPlayer = characterName;
        Debug.Log($"[GridCharacterController3D] Active player set to {currentPlayer}");

        // Update highlights for new player
        if (characters.TryGetValue(currentPlayer, out GameObject character))
        {
            Vector3Int startCell = coordinateConverter.GetCharacterCell(character);
            rangeHighlighter.UpdateHighlights(startCell, maxMovementDistance);
        }

        isProcessingTurn = false;
    }
    /// <summary>
    /// Executes movement for a character along a given path
    /// </summary>
    public IEnumerator ExecuteMovement(GameObject characterName, List<Vector3Int> path)
    {
        if (!characters.TryGetValue(characterName, out GameObject character))
        {
            Debug.LogError($"[GridCharacterController3D] Character {characterName} not found!");
            yield break;
        }

        if (!tokenMovements.TryGetValue(characterName, out ITokenMovement movement))
        {
            Debug.LogError($"[GridCharacterController3D] Movement controller for {characterName} not found!");
            yield break;
        }

        if (path == null || path.Count < 2)
        {
            Debug.LogWarning($"[GridCharacterController3D] Invalid path for {characterName}!");
            yield break;
        }

        Debug.Log($"[GridCharacterController3D] Executing movement for {characterName}");

        visualIndicator.Clear();
        rangeHighlighter.ClearHighlights();

        isProcessingTurn = true;

        movement.setPath(path);
        yield return new WaitForSeconds(0.3f);
        movement.start();

        while (movement.IsMoving())
        {
            yield return movement.update();
        }

        yield return new WaitForSeconds(0.5f);

        yield return new WaitForSeconds(0.3f);

        // Update highlights after movement
        Vector3Int newCell = coordinateConverter.GetCharacterCell(character);
        rangeHighlighter.UpdateHighlights(newCell, maxMovementDistance);

        isProcessingTurn = false;

        Debug.Log($"[GridCharacterController3D] Movement completed for {characterName}");
    }

    //I want to use an interface to access this method in the future
    //This method takes a player object and range as an input
    //returns a list of gameobjects within that range
    public List<GameObject> GetOccupantsInArea(GameObject token, int range)
    {
        if (!characters.ContainsValue(token))
        {
            Debug.LogError("[GridCharacterController3D] Token not recognized!");
            return new List<GameObject>();
        }

        Vector3Int centerCell = coordinateConverter.GetCharacterCell(token);
        HashSet<Vector3Int> areaCells = rangeHighlighter.CalculateEmination(centerCell, range);
        rangeHighlighter.UpdateAttackHighlights(centerCell, areaCells);
        //convert hashset to list
        List<Vector3Int> areaCellsList = new List<Vector3Int>(areaCells);
        return grid.GetOccupantsInArea(areaCellsList);
    }

    /// <summary>
    /// Gets the coordinate converter instance
    /// </summary>
    public GridCoordinateConverter GetCoordinateConverter()
    {
        return coordinateConverter;
    }


    #endregion

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
}