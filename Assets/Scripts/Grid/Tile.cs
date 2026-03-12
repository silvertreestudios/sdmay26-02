using UnityEngine;

public class Tile
{
    public enum Type
    {
        Walkable,
        Ground,
        Void,
        Wall,
        Door
    }
    public Type TileType { get; set; }
    public Vector3 WorldPosition { get; private set; }
    public Vector3Int GridPosition { get; private set; }

    public Tile(Type type, Vector3 worldPosition, Vector3Int gridPosition)
    {
        TileType = type;
        WorldPosition = worldPosition;
        GridPosition = gridPosition;
    }
}