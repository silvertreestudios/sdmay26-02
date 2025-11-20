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

    // how the player appears on grid
    [Header("Spawn")]
    public float yDrawOffset = 0.001f;

    // controls movement speed
    [Header("Movement (XZ only)")]
    public float moveSpeed = 2f;

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

    // called when component is enabled
    void OnEnable()
    {
        // try to find a grid in the scene automatically
        if (autoFindGrid && !grid)
            grid = FindAnyObjectByType<GridRenderer3D>();
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
        // mark as initialized
        isInitialized = true;
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
                    // add character to camera manager
                    cameraManager.addActor(kvp.Key, kvp.Value);
                }
                SetCameraForCharacter("Player1", CameraType.Pick);
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
        // get current character data (using Player1 as default)
        if (!TryGetCurrentCharacterData("Player1", out var currentCharacter, out var currentMovement)) return;
        // handle player input for movement
        HandlePlayerInput(cam, "Player1", currentCharacter, currentMovement);
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

    // handle player input for movement
    // prvate method created to keep Update() clean and modular
    private void HandlePlayerInput(Camera cam, string characterName, GameObject character, ITokenMovement movement)
    {
        // check for left mouse click
        if (!InputCompat.LeftClickDown() || isProcessingTurn)
            return;

        // get clicked cell on grid
        if (!TryGetClickedCell(cam, out Vector3Int targetCell)) return;
        // check if target cell is walkable
        if (!grid.IsCellWalkable(targetCell.x, targetCell.z)) return;

        // find path using pathfinder service
        Vector3Int startCell = GetCharacterCell(character);
        var result = pathfinder.FindPath(startCell, targetCell);

        // if path found, start movement coroutine
        if (result.found && result.path != null)
        {
            // mark as processing turn to prevent further input until movement ends
            isProcessingTurn = true;
            // start movement coroutine for the character
            StartCoroutine(HandleTurn(character, movement, result.path));
        }
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
            yield return movement.update();
        }

        // short pause when reaching destination
        yield return new WaitForSeconds(1.2f);

        // focus camera on character after move
        SetCameraForCharacter(actor.name, CameraType.Focus);

        // small pause for dramatic effect
        yield return new WaitForSeconds(0.3f);

        // reset flag to allow next movement
        isProcessingTurn = false;
    }

    // spawn both characters on the grid
    void SpawnCharacters()
    {
        // calculate y position with offset for drawing above grid
        float yPos = grid ? grid.gridY + yDrawOffset : 0.001f;
        // spawn player 1 and player 2 at specified positions
        SpawnCharacter("Player1", prefab, new Vector3(0f, yPos, 0f), Color.white);
        SpawnCharacter("Player2", prefab2, new Vector3(18.5f, yPos, 1.5f), Color.red);
    }

    // spawn a single character on the grid
    private void SpawnCharacter(string name, GameObject prefab, Vector3 position, Color color)
    {
        // instantiate player prefab
        GameObject player = Instantiate(prefab);
        player.name = name.Replace("Player", "Player "); // "Player1" -> "Player 1"
        player.transform.position = position;
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
            cameraManager.setCurrentActor(characterName);
            cameraManager.setMode(mode);
            cameraManager.ResetClock();
        }
    }

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
}