using UnityEngine;

public class TileOld
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

    public TileOld(Type type, Vector3 worldPosition, Vector3Int gridPosition)
    {
        TileType = type;
        WorldPosition = worldPosition;
        GridPosition = gridPosition;
    }
}