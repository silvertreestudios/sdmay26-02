using UnityEngine;

public class Tile
{
    public enum Type
    {
        Ground,
        Wall,
        Void,
        Water,
        Lava,
        Ice
        // Easily expandable
    }

    public Type TileType { get; private set; }
    public Vector3 WorldPosition { get; private set; }
    public Vector3Int GridPosition { get; private set; }

    public Tile(Type type, Vector3 worldPosition, Vector3Int gridPosition)
    {
        TileType = type;
        WorldPosition = worldPosition;
        GridPosition = gridPosition;
    }
}