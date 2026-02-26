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
    public GridMemory gridMemory;
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
    public GridPathfinder pathfinder { get; private set; }
    public VisualIndicator visualIndicator { get; private set; }
    public MovementRange rangeHighlighter { get; private set; }
    public GridCoordinateConverter coordinateConverter { get; private set; }
    // State flags
    public bool isInitialized { get; private set; } = false;
    public bool isProcessingTurn = false;
    private GameObject currentPlayer = null;

    // Shared per-frame state for input coroutine
    public Camera currentCamera;
    public ITokenMovement currentMovement;

    // Input tracking
    public bool cancel = false;
    public Vector3Int lastClickedCell;


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
        if (autoFindGrid && !gridMemory)
            gridMemory = FindAnyObjectByType<GridMemory>();

        // Register cell selectability check with grid
        if (gridMemory != null)
        {
            gridMemory.IsCellSelectable = IsCellSelectableForCurrentCharacter;
        }
    }

    void OnDisable()
    {
        // Unregister selectability check
        if (gridMemory != null)
        {
            gridMemory.IsCellSelectable = null;
        }

        // Clean up subsystems
        rangeHighlighter?.ClearHighlights();
        visualIndicator?.Clear();
    }

    /// <summary>
    /// Initializes the character controller and subsystems
    /// </summary>
    public void Setup()
    {
        InitializeCoordinateConverter();
        InitializePathfinder();
        SpawnCharacters();
        InitializeMovementControllers();
        InitializeSubsystems();

        // Mark as initialized
        isInitialized = true;

        // Update pathfinder settings if changed in inspector
        if (pathfinder != null)
        {
            pathfinder.SetDiagonalMovement(allowDiagonalMovement, diagonalCost);
        }
    }

    // ideally this gets removed for performance, but is fine for now
    void Update()
    {
        // Check if system is ready
        if (!IsReadyForUpdate(out currentCamera)) return;

        // Get current character data
        if (!TryGetCurrentCharacterData(currentPlayer, out currentMovement))
            return;
    }

    /// <summary>
    /// Initializes the coordinate converter
    /// </summary>
    private void InitializeCoordinateConverter()
    {
        if (gridMemory != null)
        {
            coordinateConverter = new GridCoordinateConverter(gridMemory);
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
        if (gridMemory != null)
        {
            pathfinder = new GridPathfinder(gridMemory, allowDiagonalMovement, diagonalCost);
        }
        else
        {
            Debug.LogError("[GridCharacterController3D] Grid or GridMemory is null, cannot initialize pathfinder!");
        }
    }

    /// <summary>
    /// initializes movement controllers for each character
    /// </summary>
    private void InitializeMovementControllers()
    {
        foreach (var kvp in characters)
        {
            tokenMovements[kvp.Key] = new tokenMovement(
                kvp.Value.transform, stepHeight, maxRotation, ptLerp, yLerp);
        }
    }

    /// <summary>
    /// Initialize subsystems: VisualIndicator and MovementRangeHighlighter
    /// </summary>
    private void InitializeSubsystems()
    {
        visualIndicator = new VisualIndicator(this);
        rangeHighlighter = new MovementRange(this);

        //Debug.Log("[GridCharacterController3D] Subsystems initialized.");
    }

    /// <summary>
    /// Checks if the system is prepared for update operations 
    /// and retrieves the active camera if ready
    /// </summary>
    private bool IsReadyForUpdate(out Camera cam)
    {
        cam = null;

        if (!Application.isPlaying || !isInitialized) return false;

        if (!gridMemory)
        {
            if (autoFindGrid) gridMemory = FindAnyObjectByType<GridMemory>();
            if (!gridMemory) return false;
        }

        var renderer = gridMemory.GetComponent<GridRenderer3D>();
        cam = (renderer && renderer.targetCamera) ? renderer.targetCamera : Camera.main;
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

    #region Character Spawning

    //here is where you manually spawn characters for now
    //TODO make a more intuitive way to spawn characters on a map
    void SpawnCharacters()
    {
        float yPos = gridMemory ? gridMemory.GridY + yDrawOffset : 0.001f;

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
        GameObject player = Instantiate(prefab);
        player.name = name.Replace("Player", "Player ");
        player.transform.position = position;
        characters[player] = player;
        currentPlayer = currentPlayer ?? player;

        var renderer = player.GetComponent<MeshRenderer>();
        if (renderer && color != Color.white)
        {
            renderer.material.color = color;
        }

        gridMemory.SetCreaturePosition(player, coordinateConverter.GetCharacterCell(player));
    }

    void SnapToValidCell(GameObject obj)
    {
        if (!gridMemory) return;

        if (!coordinateConverter.TryGridWorldToCell(obj.transform.position, out var cell, clamp: true))
            return;

        if (!gridMemory.IsCellWalkable(cell))
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
        //Debug.Log("Setting active Player");
        rangeHighlighter.ClearHighlights();
        visualIndicator.Clear();

        currentPlayer = characterName;
        currentMovement = tokenMovements.ContainsKey(characterName) ? tokenMovements[characterName] : null;

        //Debug.Log($"[GridCharacterController3D] Active player set to {currentPlayer}");

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

        //Debug.Log($"[GridCharacterController3D] Executing movement for {characterName}");

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

        Vector3Int newCell = coordinateConverter.GetCharacterCell(character);
        rangeHighlighter.UpdateHighlights(newCell, maxMovementDistance);

        isProcessingTurn = false;

        //Debug.Log($"[GridCharacterController3D] Movement completed for {characterName}");
    }

    /// <summary>
    /// Gets occupants within a specified range from a token
    /// </summary>
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
        List<Vector3Int> areaCellsList = new List<Vector3Int>(areaCells);
        return gridMemory.GetOccupantsInArea(areaCellsList);
    }

    /// <summary>
    /// Gets the coordinate converter instance
    /// </summary>
    public GridCoordinateConverter GetCoordinateConverter()
    {
        return coordinateConverter;
    }
    /// <summary>
    /// Converts screen click to grid cell
    /// </summary>
    public bool TryGetClickedCell(Camera cam, out Vector3Int targetCell)
    {
        targetCell = Vector3Int.zero;

        if (!coordinateConverter.ScreenToXZPlane(cam, InputCompat.MousePositionScreen(), gridMemory.GridY, out Vector3 hit))
            return false;

        return coordinateConverter.TryGridWorldToCell(hit, out targetCell);
    }
    /// <summary>
    /// Validates clicked cell and calculates path
    /// </summary>
    public bool TryValidateAndGetPath(Camera cam, GameObject character, out List<Vector3Int> path)
    {
        path = null;

        if (!TryGetClickedCell(cam, out Vector3Int targetCell) ||
            !gridMemory.IsCellWalkable(targetCell) ||
            !rangeHighlighter.IsCellReachable(targetCell))
            return false;

        Vector3Int startCell = coordinateConverter.GetCharacterCell(character);
        var pathResult = pathfinder.FindPath(startCell, targetCell);

        if (!pathResult.found || pathResult.path == null || pathResult.path.Count < 2 ||
            (maxMovementDistance > 0 && pathResult.path.Count - 1 > maxMovementDistance))
            return false;

        path = pathResult.path;
        return true;
    }

    public bool TryValidateAndGetPathAI(Vector3Int startCell, Vector3Int targetCell, out List<Vector3Int> path)
    {
        path = null;

        if (!gridMemory.IsCellWalkable(targetCell))
            return false;

        var pathResult = pathfinder.FindPath(startCell, targetCell);

        if (!pathResult.found || pathResult.path == null || pathResult.path.Count < 2)
            return false;

        path = pathResult.path;
        return true;
    }

    /// <summary>
    /// Internal movement execution coroutine
    /// </summary>
    public IEnumerator ExecuteMovementInternal(GameObject actor, ITokenMovement movement, List<Vector3Int> path)
    {
        movement.setPath(path);
        yield return new WaitForSeconds(0.3f);
        movement.start();

        Vector3Int lastCell = coordinateConverter.GetCharacterCell(actor);

        while (movement.IsMoving())
        {
            yield return movement.update();

            Vector3Int currentCell = coordinateConverter.GetCharacterCell(actor);

            if (currentCell != lastCell)
            {
                gridMemory.MoveCreaturePosition(actor, currentCell, lastCell);
                lastCell = currentCell;
            }
        }

        Vector3Int finalCell = coordinateConverter.GetCharacterCell(actor);
        if (finalCell != lastCell)
        {
            gridMemory.MoveCreaturePosition(actor, finalCell, lastCell);
        }

        yield return new WaitForSeconds(0.5f);
        isProcessingTurn = false;

    }

    #endregion

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
}