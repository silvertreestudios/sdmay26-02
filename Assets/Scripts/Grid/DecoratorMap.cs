using UnityEngine;

public class DecoratorMap
{
    private static DecoratorMap instance;
    // public static property to access singleton
    public static DecoratorMap Instance
    {
        get
        {
            // initialization of singleton instance
            if (instance == null)
            {
                instance = new DecoratorMap();
            }
            return instance;
        }
    }

    private Tile[,] tileMap;
    private IGridMemory gridMemory;

    // private constructor to enforce singleton pattern
    private DecoratorMap() { }

    public void Initialize(IGridMemory gridMemory)
    {
        this.gridMemory = gridMemory;
        GenerateTileMap();
    }

    private void GenerateTileMap()
    {
        if (gridMemory == null) return;

        // create 2d array matching grid dimensions
        tileMap = new Tile[gridMemory.Width, gridMemory.Height];

        for (int z = 0; z < gridMemory.Height; z++)
        {
            for (int x = 0; x < gridMemory.Width; x++)
            {
                Vector3Int gridPos = new Vector3Int(x, gridMemory.GridY, z);
                // calculate world position at center of tile
                Vector3 worldPos = gridMemory.Origin + new Vector3(
                    (x + 0.5f) * gridMemory.CellSize,
                    gridMemory.GridY,
                    (z + 0.5f) * gridMemory.CellSize
                );

                // map gridmemory tiletype to tile.type
                Tile.Type tileType = ConvertTileType(x, z);
                tileMap[x, z] = new Tile(tileType, worldPos, gridPos);
            }
        }
    }

    private Tile.Type ConvertTileType(int x, int z)
    {
        // cast to concrete type to access gridinfo
        if (gridMemory is GridMemory gridMem)
        {
            var tile = gridMem.GridInfo[x, gridMemory.GridY, z];
            // convert between enum types using switch expression
            return tile.type switch
            {
                GridMemory.TileType.Ground => Tile.Type.Walkable,
                GridMemory.TileType.Wall => Tile.Type.Wall,
                GridMemory.TileType.Void => Tile.Type.Void,
                _ => Tile.Type.Walkable  
            };
        }
        return Tile.Type.Ground;
    }

    // returns 2d array of tiles with type and world position
    public Tile[,] GetTileMap()
    {
        return tileMap;
    }

    // returns single tile at grid coordinates
    public Tile GetTile(int x, int z)
    {
        // bounds checking before array access
        if (tileMap == null || x < 0 || x >= gridMemory.Width || z < 0 || z >= gridMemory.Height)
            return null;
        return tileMap[x, z];
    }

    // returns single tile at grid position vector
    public Tile GetTile(Vector3Int gridPosition)
    {
        return GetTile(gridPosition.x, gridPosition.z);
    }
}