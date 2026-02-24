using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class GridMemory : IGridMemory
{
    public enum TileType
    {
        Ground,
        Wall,
        Void,
    }

    public enum TileStatus
    {
        Normal,
        FireI
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

    public override int Width { get; protected set; }
    public override int Height { get; protected set; }
    public override float CellSize { get; protected set; }
    public override Vector3 Origin { get; protected set; }
    public override int GridY { get; protected set; }

    public TILE[,,] GridInfo { get; private set; }

    // Delegate to check if a cell is selectable
    public System.Func<Vector3Int, bool> IsCellSelectable { get; set; }

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

                    // Initialize tile with default values
                    GridInfo[x, gridY, z] = new TILE
                    {
                        x = x,
                        z = z,
                        type = tileType,
                        isOccupied = gridData[x, z] == 2, // Walls are occupied
                        status = new TileStatus[] { TileStatus.Normal }
                    };
                }
            }
        }
        else
        {
            GridInfo = null;
        }
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
            Debug.Log("Failed to move creature from " + startPosition.ToString() + " to " + targetPosition.ToString());
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
            Debug.Log("Failed to set creature position at " + spawnPosition.ToString());
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
            Debug.Log("Failed to clear creature position at " + position.ToString());
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
        // Check if the tile type allows walking
        return GridInfo[position.x, GridY, position.z].type == TileType.Ground && !GridInfo[position.x, GridY, position.z].isOccupied;
    }

    public override IEnumerator TargetSelect(int range, CoroutineResult<GameObject> result)
    {
        //return a selected monster if valid
        yield return null;
    }

    internal class TileInfo
    {
        internal TileType type;
    }
}