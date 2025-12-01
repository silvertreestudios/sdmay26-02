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
    [Header("References")]
    public GridRenderer3D grid;
    public GameObject prefab;
    public GameObject prefab2;
    public bool autoFindGrid = true;
    public GameObject rangeHighlightPrefab;

    // Spawn settings
    [Header("Spawn")]
    public float yDrawOffset = 0.001f;

    // Movement settings
    [Header("Movement (XZ only)")]
    public float moveSpeed = 2f;
    [Tooltip("Maximum distance (in grid cells) a character can move in one turn. Set to 0 for unlimited.")]
    public int maxMovementDistance = 9;

    // Animation settings
    [Header("Animation")]
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;
    public float JumpDuration = 0.5f;
    public Transform dummyTarget;
    public bool allowDiagonalMovement = true;
    public float diagonalCost = 1.414f;

    // Visual indicator settings
    [Header("Visual Indicator")]
    [Tooltip("Material for the visual indicator line")]
    public Material indicatorMaterial;
    [Tooltip("Width of the visual indicator line")]
    public float indicatorWidth = 0.2f;
    [Tooltip("Height offset for the visual indicator line above ground")]
    public float indicatorHeight = 0.1f;
    [Tooltip("Color for the default indicator")]
    public Color defaultIndicatorColor = new Color(1f, 1f, 0f, 0.7f);
    [Tooltip("Color for the confirmed indicator")]
    public Color confirmedIndicatorColor = new Color(1f, 0f, 0f, 0.7f);
    [Tooltip("Time window for double-click detection (in seconds)")]
    public float doubleClickTime = 0.3f;

    [Header("Highlights")]
    [Tooltip("Color for range highlight visuals")]
    public Color rangeHighlightColor = new Color(1f, 0f, 0f, 0.5f);
    [Tooltip("Height offset for range highlights")]
    public float rangeHighlightHeightOffset = 0.05f;

    // Character storage
    private Dictionary<string, GameObject> characters = new Dictionary<string, GameObject>();
    private Dictionary<string, ITokenMovement> tokenMovements = new Dictionary<string, ITokenMovement>();
    private Dictionary<string, ActionController> actionControllers = new Dictionary<string, ActionController>();

    // Subsystem references
    private CameraManager cameraManager;
    private GridPathfinder pathfinder;
    private VisualIndicator visualIndicator;
    private MovementRange rangeHighlighter;

    // State flags
    private bool isInitialized = false;
    private bool isProcessingTurn = false;
    private string currentPlayer = "Player1";

    // Input tracking
    private float lastClickTime = 0f;
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

    void Start()
    {
        InitializePathfinder();
        SpawnCharacters();
        InitializeMovementControllers();
        InitializeCameraManager();
        InitializeSubsystems();

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
        if (!TryGetCurrentCharacterData(currentPlayer, out var currentCharacter, out var currentMovement))
            return;

        // Handle player input
        HandlePlayerInput(cam, currentPlayer, currentCharacter, currentMovement);

        // Update camera
        cameraManager?.update();
    }

    #region Initialization

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

    private void InitializeMovementControllers()
    {
        foreach (var kvp in characters)
        {
            tokenMovements[kvp.Key] = new tokenMovement(
                kvp.Value.transform, stepHeight, maxRotation, ptLerp, yLerp);
        }
    }

    private void InitializeCameraManager()
    {
        try
        {
            cameraManager = CameraManager.GetInstance();
            if (cameraManager != null)
            {
                cameraManager.setCamera(Camera.main);

                foreach (var kvp in characters)
                {
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
            gridCellToWorld: GridCellCenterWorld);

        // Initialize movement range highlighter
        rangeHighlighter = new MovementRange(
            gridReference: grid,
            prefab: rangeHighlightPrefab,
            color: rangeHighlightColor,
            heightOffset: rangeHighlightHeightOffset,
            allowDiagonal: allowDiagonalMovement,
            diagCost: diagonalCost,
            gridCellToWorld: GridCellCenterWorld);

        Debug.Log("[GridCharacterController3D] Subsystems initialized.");
    }

    #endregion

    #region Update Loop

    private bool IsReadyForUpdate(out Camera cam)
    {
        cam = null;

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

    private bool TryGetCurrentCharacterData(string characterName, out GameObject character, out ITokenMovement movement)
    {
        return characters.TryGetValue(characterName, out character) &
               tokenMovements.TryGetValue(characterName, out movement);
    }

    private bool IsCellSelectableForCurrentCharacter(Vector3Int targetCell)
    {
        if (isProcessingTurn)
            return false;

        return rangeHighlighter.IsCellReachable(targetCell);
    }

    #endregion

    #region Input Handling

    private void HandlePlayerInput(Camera cam, string characterName, GameObject character, ITokenMovement movement)
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

        if (!TryGetClickedCell(cam, out Vector3Int targetCell)) return false;
        if (!grid.IsCellWalkable(targetCell.x, targetCell.z)) return false;
        if (!rangeHighlighter.IsCellReachable(targetCell)) return false;

        Vector3Int startCell = GetCharacterCell(character);
        var result = pathfinder.FindPath(startCell, targetCell);

        if (!result.found || result.path == null) return false;

        int pathLength = result.path.Count - 1;
        if (maxMovementDistance > 0 && pathLength > maxMovementDistance) return false;

        path = result.path;
        return true;
    }

    private bool TryGetClickedCell(Camera cam, out Vector3Int targetCell)
    {
        targetCell = Vector3Int.zero;

        if (!ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), grid.gridY, out Vector3 hit))
            return false;

        return TryGridWorldToCell(hit, out targetCell);
    }

    #endregion

    #region Movement Execution

    private IEnumerator HandleTurn(GameObject actor, ITokenMovement movement, List<Vector3Int> path)
    {
        movement.setPath(path);

        yield return new WaitForSeconds(0.3f);

        movement.start();

        while (movement.IsMoving())
        {
            yield return movement.update();
        }

        yield return new WaitForSeconds(0.5f);

        SetCameraForCharacter(currentPlayer, CameraType.Focus);

        yield return new WaitForSeconds(0.3f);

        // Automatically end turn
        string characterName = actor.name.Replace("Player ", "Player");
        if (actionControllers.TryGetValue(characterName, out ActionController actionController))
        {
            Debug.Log($"[GridCharacterController3D] Automatically ending turn for {characterName}");
            actionController.EndTurn();
        }
        else
        {
            Debug.LogError($"[GridCharacterController3D] Could not find ActionController for {characterName}");
        }

        isProcessingTurn = false;

        Debug.Log("[GridCharacterController3D] Movement completed.");
    }

    #endregion

    #region Character Spawning

    void SpawnCharacters()
    {
        float yPos = grid ? grid.gridY + yDrawOffset : 0.001f;

        if (prefab == null)
        {
            Debug.LogError("[GridCharacterController3D] prefab is not assigned in the Inspector!");
            return;
        }

        SpawnCharacter("Player1", prefab, new Vector3(0f, yPos, 0f), Color.white);

        GameObject player2Prefab = prefab2 != null ? prefab2 : prefab;
        SpawnCharacter("Player2", player2Prefab, new Vector3(18.5f, yPos, 1.5f), Color.red);
    }

    private void SpawnCharacter(string name, GameObject prefab, Vector3 position, Color color)
    {
        GameObject player = Instantiate(prefab);
        player.name = name.Replace("Player", "Player ");
        player.transform.position = position;
        characters[name] = player;

        var renderer = player.GetComponent<MeshRenderer>();
        if (renderer && color != Color.white)
        {
            renderer.material.color = color;
        }

        ActionController actionController = player.GetComponent<ActionController>();
        if (actionController == null)
        {
            actionController = player.AddComponent<ActionController>();
        }
        actionControllers[name] = actionController;
    }

    void SnapToValidCell(GameObject obj)
    {
        if (!grid) return;

        if (!TryGridWorldToCell(obj.transform.position, out var cell, clamp: true))
            return;

        if (!grid.IsCellWalkable(cell.x, cell.z))
            return;

        obj.transform.position = GridCellCenterWorld(cell.x, cell.z, yDrawOffset);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Gets the currently previewed path for a character
    /// </summary>
    public List<Vector3Int> GetPreviewedPathForCharacter(string characterName)
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
    public GameObject GetCharacter(string characterName)
    {
        characters.TryGetValue(characterName, out GameObject character);
        return character;
    }

    /// <summary>
    /// Gets the movement controller for a character
    /// </summary>
    public ITokenMovement GetMovementController(string characterName)
    {
        tokenMovements.TryGetValue(characterName, out ITokenMovement movement);
        return movement;
    }

    /// <summary>
    /// Sets the active player and updates highlights
    /// </summary>
    public void SetActivePlayer(string characterName)
    {
        rangeHighlighter.ClearHighlights();
        visualIndicator.Clear();

        currentPlayer = characterName;
        Debug.Log($"[GridCharacterController3D] Active player set to {currentPlayer}");

        SetCameraForCharacter(currentPlayer, CameraType.Pick);

        // Update highlights for new player
        if (characters.TryGetValue(currentPlayer, out GameObject character))
        {
            Vector3Int startCell = GetCharacterCell(character);
            rangeHighlighter.UpdateHighlights(startCell, maxMovementDistance);
        }

        isProcessingTurn = false;
    }

    /// <summary>
    /// Executes movement for a character along a given path
    /// </summary>
    public IEnumerator ExecuteMovement(string characterName, List<Vector3Int> path)
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

        SetCameraForCharacter(characterName, CameraType.Focus);
        yield return new WaitForSeconds(0.3f);

        // Update highlights after movement
        Vector3Int newCell = GetCharacterCell(character);
        rangeHighlighter.UpdateHighlights(newCell, maxMovementDistance);

        isProcessingTurn = false;

        Debug.Log($"[GridCharacterController3D] Movement completed for {characterName}");
    }

    #endregion

    #region Coordinate Conversion

    Vector3 GridCellCenterWorld(int x, int z, float yOffset = 0f)
    {
        float wx = grid.origin.x + (x + 0.5f) * grid.cellSize;
        float wz = grid.origin.z + (z + 0.5f) * grid.cellSize;
        return new Vector3(wx, grid.gridY + yOffset, wz);
    }

    Vector3Int GetCharacterCell(GameObject character)
    {
        TryGridWorldToCell(character.transform.position, out Vector3Int cell, clamp: true);
        return cell;
    }

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

    #endregion

    #region Camera Control

    private void SetCameraForCharacter(string characterName, CameraType mode)
    {
        if (cameraManager != null)
        {
            string displayName = characterName.Replace("Player", "Player ");
            cameraManager.setCurrentActor(displayName);
            cameraManager.setMode(mode);
            cameraManager.ResetClock();
        }
    }

    #endregion

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
}