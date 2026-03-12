using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class GridMemory : IGridMemory
{
    public enum TileType
    {
        Ground,
        Door,
        Wall,
        Void,
    }

    public enum TileStatus
    {
        Normal,
        FireI,
        DoorOpen,
        DoorClosed,
    }

    public struct TILE
    {
        public int x;
        public int y;
        public int z;
        public TileType type;
        //tile can be occupied without an occupant (e.g., obstacle)
        public bool isOccupied;
        public GameObject occupant;
        public TileStatus[] status;
    }

    private TeamRules teamRules => TeamRules.GetInstance();
    private CombatManagerInterface combatManager => CombatManagerInterface.GetInstance();
    public override int Width { get; protected set; }
    public override int Height { get; protected set; }
    public override float CellSize { get; protected set; }
    public override Vector3 Origin { get; protected set; }
    public override int GridY { get; protected set; }

    public TILE[,,] GridInfo { get; private set; }


    public override void Initialize(int width, int height, int gridY, float cellSize, Vector3 origin, int[,] gridData)
    {
        this.Width = width;
        this.Height = height;
        this.GridY = gridY;
        this.CellSize = cellSize;
        this.Origin = origin;

        if (gridData != null)
        {
            GridInfo = new TILE[width, gridY + 1, height];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Determine tile type based on grid data
                    TileType tileType = TileType.Void;
                    // ground
                    if (gridData[x, z] == 1)
                        tileType = TileType.Ground;
                    // wall
                    else if (gridData[x, z] == 2)
                        tileType = TileType.Wall;
                    // closed door
                    else if (gridData[x, z] == 3)
                        tileType = TileType.Door;

                    // Initialize tile with default values
                    TileStatus[] initialStatus;
                    if (gridData[x, z] == 3)
                    {
                        initialStatus = new TileStatus[] { TileStatus.DoorClosed };
                    }
                    else
                    {
                        initialStatus = new TileStatus[] { TileStatus.Normal };
                    }

                    GridInfo[x, gridY, z] = new TILE
                    {
                        x = x,
                        z = z,
                        type = tileType,
                        isOccupied = gridData[x, z] == 2 || gridData[x, z] == 3, // Walls and closed doors are occupied
                        status = initialStatus
                    };
                }
            }
        }
        else
        {
            GridInfo = null;
        }
    }

    public override bool IsDoor(int x, int z)
    {
        if (GridInfo == null || x < 0 || x >= Width || z < 0 || z >= Height) return false;
        return GridInfo[x, GridY, z].type == TileType.Door;
    }

    public override bool IsDoorOpen(int x, int z)
    {
        if (!IsDoor(x, z)) return false;
        return HasStatus(x, z, TileStatus.DoorOpen);
    }

    public override void ToggleDoor(int x, int z)
    {
        if (!IsDoor(x, z)) return;

        if (HasStatus(x, z, TileStatus.DoorOpen))
        {
            // Close door
            RemoveStatus(x, z, TileStatus.DoorOpen);
            SetStatus(x, z, TileStatus.DoorClosed);
            SetIsOccupied(x, z, true); // closed doors block movement
        }
        else
        {
            // Open door
            RemoveStatus(x, z, TileStatus.DoorClosed);
            SetStatus(x, z, TileStatus.DoorOpen);
            SetIsOccupied(x, z, false); // open doors allow movement
        }
    }

    // Helper method to remove a status
    public override void RemoveStatus(int x, int z, TileStatus statusToRemove)
    {
        if (GridInfo == null || x < 0 || x >= Width || z < 0 || z >= Height) return;

        var statuses = new List<TileStatus>(GridInfo[x, GridY, z].status);
        statuses.Remove(statusToRemove);
        GridInfo[x, GridY, z].status = statuses.ToArray();
    }

    //creates a list of current statuses, then adds the new status if not already present. might need a cleaner way to do this.
    public override void SetStatus(int x, int z, TileStatus statusToSet)
    {
        if (GridInfo == null || x < 0 || x >= Width || z < 0 || z >= Height) return;
        if (!System.Array.Exists(GridInfo[x, GridY, z].status, status => status == statusToSet))
        {
            var statuses = new List<TileStatus>(GridInfo[x, GridY, z].status);
            statuses.Add(statusToSet);
            GridInfo[x, GridY, z].status = statuses.ToArray();
        }
    }
    public override bool HasStatus(int x, int z, TileStatus statusToCheck)
    {
        if (GridInfo == null || x < 0 || x >= Width || z < 0 || z >= Height) return false;
        return System.Array.Exists(GridInfo[x, GridY, z].status, status => status == statusToCheck);
    }

    public override bool GetIsOccupied(int x, int z)
    {
        if (GridInfo == null || x < 0 || x >= Width || z < 0 || z >= Height) return false;
        return GridInfo[x, GridY, z].isOccupied;
    }

    public override void SetIsOccupied(int x, int z, bool occupied)
    {
        if (GridInfo == null || x < 0 || x >= Width || z < 0 || z >= Height) return;
        GridInfo[x, GridY, z].isOccupied = occupied;
    }

    public override void MoveCreaturePosition(GameObject token, Vector3Int targetPosition, Vector3Int startPosition)
    {
        //make sure we are moving the right character
        if (token == null || GridInfo[startPosition.x, GridY, startPosition.z].occupant != token)
        {
            Debug.LogWarning("Failed to move creature from " + startPosition.ToString() + " to " + targetPosition.ToString());
            return;
        }
        GridInfo[startPosition.x, GridY, startPosition.z].isOccupied = false;
        GridInfo[startPosition.x, GridY, startPosition.z].occupant = null;
        GridInfo[targetPosition.x, GridY, targetPosition.z].isOccupied = true;
        GridInfo[targetPosition.x, GridY, targetPosition.z].occupant = token;
        return;
    }
    public override void SetCreaturePosition(GameObject token, Vector3Int spawnPosition)
    {
        //make sure we are placing a valid character and the tile is not already occupied
        if (token == null || GridInfo[spawnPosition.x, GridY, spawnPosition.z].isOccupied)
        {
            Debug.LogWarning("Failed to set creature position at " + spawnPosition.ToString());
            return;
        }
        GridInfo[spawnPosition.x, GridY, spawnPosition.z].isOccupied = true;
        GridInfo[spawnPosition.x, GridY, spawnPosition.z].occupant = token;
    }

    public override void ClearCreaturePosition(GameObject token, Vector3Int position)
    {
        //make sure we are clearing the right character
        if (token == null || GridInfo[position.x, GridY, position.z].occupant != token)
        {
            Debug.LogWarning("Failed to clear creature position at " + position.ToString());
            return;
        }
        GridInfo[position.x, GridY, position.z].isOccupied = false;
        GridInfo[position.x, GridY, position.z].occupant = null;
    }

    public override List<GameObject> GetOccupantsInArea(List<Vector3Int> area)
    {
        List<GameObject> occupants = new List<GameObject>();
        foreach (Vector3Int point in area)
        {
            if (GridInfo[point.x, GridY, point.z].isOccupied && GridInfo[point.x, GridY, point.z].occupant != null)
            {
                occupants.Add(GridInfo[point.x, GridY, point.z].occupant);
            }
        }
        return occupants;
    }

    public override bool IsCellWalkable(Vector3Int position)
    {
        if (GridInfo == null) return false;
        // if x or z are out of bounds, return false
        if (position.x < 0 || position.x >= Width) return false;
        if (position.z < 0 || position.z >= Height) return false;
        
        TILE tile = GridInfo[position.x, GridY, position.z];
        
        // Ground tiles are walkable if not occupied or occupied by friendly
        if (tile.type == TileType.Ground)
        {
            if (!tile.isOccupied) return true;
            
            // Check if occupied by friendly unit
            if (tile.occupant == null || combatManager == null || teamRules == null) return false;
            
            var currentTurn = combatManager.WhosTurn();
            if (currentTurn == null) return false;
            
            var occupantTeam = tile.occupant.GetComponent<Team>();
            var currentTeam = currentTurn.GetComponent<Team>();
            
            if (occupantTeam == null || currentTeam == null) return false;
            if (string.IsNullOrEmpty(occupantTeam.Name) || string.IsNullOrEmpty(currentTeam.Name)) return false;
            
            try
            {
                return teamRules.IsFriendly(occupantTeam.Name, currentTeam.Name);
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                Debug.LogWarning($"Team not found: {occupantTeam.Name}, {currentTeam.Name}");
                return false;
            }
        }
        
        // Open doors are walkable if not occupied
        if (tile.type == TileType.Door)
            return HasStatus(position.x, position.z, TileStatus.DoorOpen) && !tile.isOccupied;
        
        return false;
    }

    public override bool IsCellSelectableTraversal(Vector3Int position)
    {
        if (GridInfo == null) return false;
        // if x or z are out of bounds, return false
        if (position.x < 0 || position.x >= Width) return false;
        if (position.z < 0 || position.z >= Height) return false;
        
        TILE tile = GridInfo[position.x, GridY, position.z];
        
        // Can't select void tiles for traversal
        if (tile.type == TileType.Void) return false;
        // Can't select occupied tiles for traversal
        if (tile.isOccupied) return false;
        // Can select ground tiles
        if (tile.type == TileType.Ground) return true;
        // Can select open doors
        return tile.type == TileType.Door && HasStatus(position.x, position.z, TileStatus.DoorOpen);
    }

    public override bool IsCellSelectableAction(Vector3Int position)
    {
        if (GridInfo == null) return false;
        // if x or z are out of bounds, return false
        if (position.x < 0 || position.x >= Width) return false;
        if (position.z < 0 || position.z >= Height) return false;
        
        TILE tile = GridInfo[position.x, GridY, position.z];
        // Can't select wall tiles for actions
        return tile.type != TileType.Wall;
    }

    public override bool IsCellHoverable(Vector3Int position, bool allowDoorHover)
    {
        if (GridInfo == null) return false;
        
        // Check if cell is walkable (with all the safety checks inside)
        if (IsCellWalkable(position)) return true;
        
        // Check if hovering over doors is allowed
        if (!allowDoorHover) return false;
        if (position.x < 0 || position.x >= Width) return false;
        if (position.z < 0 || position.z >= Height) return false;
        
        return GridInfo[position.x, GridY, position.z].type == TileType.Door;
    }

    public override void HandleCellClick(Vector3Int cell)
    {
        if (GridInfo == null) return;
        // Validate cell bounds
        if (cell.x < 0 || cell.x >= Width) return;
        if (cell.z < 0 || cell.z >= Height) return;

        // If it's a door tile, toggle between open and closed
        if (GridInfo[cell.x, GridY, cell.z].type == TileType.Door)
        {
            ToggleDoor(cell.x, cell.z);
            Debug.Log($"Door clicked at ({cell.x}, {cell.z}). Door is now {(IsDoorOpen(cell.x, cell.z) ? "open" : "closed")}.");
        }
    }

    public override IEnumerator TargetSelect(int range, CoroutineResult<GameObject> result)
    {
        //return a selected monster if valid
        yield return null;
    }
}