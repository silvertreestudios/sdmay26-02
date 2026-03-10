using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class GridCharacterController3D : SingletonMonoBehaviour<GridCharacterController3D>
{
    // References to grid and prefabs set in inspector
    public GridMemory gridMemory;
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
    private HashSet<GameObject> characters = new HashSet<GameObject>();
    private Dictionary<GameObject, ITokenMovement> tokenMovements = new Dictionary<GameObject, ITokenMovement>();

    // Subsystem references
    public GridPathfinder pathfinder { get; private set; }
    public VisualIndicator visualIndicator { get; private set; }
    public MovementRange rangeHighlighter { get; private set; }
    public GridCoordinateConverter coordinateConverter { get; private set; }
    // State flags
    public bool isProcessingTurn = false;
    private GameObject currentPlayer => CombatManagerInterface.GetInstance()?.WhosTurn();

    // Shared per-frame state for input coroutine
    public Camera currentCamera;

    // Input tracking
    public Vector3Int lastClickedCell;

    void OnDisable()
    {
        // Clean up subsystems
        rangeHighlighter?.ClearHighlights();
        visualIndicator?.Clear();
    }

    /// <summary>
    /// Initializes the character controller and subsystems
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // Auto-find grid if needed
        if (autoFindGrid && !gridMemory)
            gridMemory = FindAnyObjectByType<GridMemory>();

        if (gridMemory == null)
        {
            Debug.LogError("[GridCharacterController3D] gridMemory is null, cannot initialize!");
            return;
        }

        coordinateConverter = new GridCoordinateConverter(gridMemory);
        pathfinder = new GridPathfinder(gridMemory, allowDiagonalMovement, diagonalCost);

        visualIndicator = new VisualIndicator(this);
        rangeHighlighter = new MovementRange(this);

        var renderer = gridMemory.GetComponent<GridRenderer3D>();
        currentCamera = (renderer && renderer.targetCamera) ? renderer.targetCamera : Camera.main;
    }

    #region Creature Placement

    public void PlaceCreature(GameObject token)
    {
        characters.Add(token);
        gridMemory.SetCreaturePosition(token, coordinateConverter.GetCharacterCell(token));
        tokenMovements[token] = new tokenMovement(token.transform, stepHeight, maxRotation, ptLerp, yLerp);
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
    /// Gets occupants within a specified range from a token
    /// </summary>
    public List<GameObject> StrikeOccupantsInArea(GameObject token, int range)
    {
        if (!characters.Contains(token))
        {
            Debug.LogError("[GridCharacterController3D] Token not recognized!");
            return new List<GameObject>();
        }

        Vector3Int centerCell = coordinateConverter.GetCharacterCell(token);
        HashSet<Vector3Int> areaCells = rangeHighlighter.CalculateEmination(centerCell, range);
        List<Vector3Int> areaCellsList = new List<Vector3Int>(areaCells);
        List<GameObject> allOccupants = gridMemory.GetOccupantsInArea(areaCellsList);
        // filter out occupants that are in freindly standing witht the active player
        foreach (GameObject occupant in allOccupants.ToArray())
        {
            if(TeamRules.GetInstance().IsFriendly(token.GetComponent<Team>().Name, occupant.GetComponent<Team>().Name ))
            {
                allOccupants.Remove(occupant);
            }
        }
        return allOccupants;
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

    public bool TryValidateAndGetPathAI(Vector3Int startCell, Vector3Int targetCell, out List<Vector3Int> path, bool ignoreTargetOccupancy = false)
    {
        path = null;

        // a bit of a bandaid patch to allow for efficient pathfinding directly to a target
        if (ignoreTargetOccupancy && gridMemory != null)
        {
            // Temporarily clear occupancy on the target cell so the pathfinder can
            // route through it. The caller is responsible for not actually moving there.
            bool wasOccupied = gridMemory.GetIsOccupied(targetCell.x, targetCell.z);
            if (wasOccupied)
                gridMemory.SetIsOccupied(targetCell.x, targetCell.z, false);

            var tempResult = pathfinder.FindPath(startCell, targetCell);

            if (wasOccupied)
                gridMemory.SetIsOccupied(targetCell.x, targetCell.z, true);

            if (!tempResult.found || tempResult.path == null || tempResult.path.Count < 2)
                return false;

            path = tempResult.path;
            return true;
        }

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

            if (currentCell != lastCell && gridMemory.IsCellSelectableTraversal(currentCell))
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
}