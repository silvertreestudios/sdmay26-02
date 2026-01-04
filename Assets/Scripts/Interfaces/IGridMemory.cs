using System.Collections.Generic;
using UnityEngine;

public interface IGridMemory
{
    int Width { get; }
    int Height { get; }
    float CellSize { get; }
    Vector3 Origin { get; }
    int GridY { get; }
    GridMemory.TILE[,,] GridInfo { get; }

    bool IsWalkable(int x, int z);
    bool GetIsOccupied(int x, int z);
    void SetIsOccupied(int x, int z, bool occupied);
    void SetStatus(int x, int z, GridMemory.TileStatus statusToSet);
    bool HasStatus(int x, int z, GridMemory.TileStatus statusToCheck);
    void MoveCreaturePosition(GameObject token, Vector3Int targetPosition, Vector3Int startPosition);
    void SetCreaturePosition(GameObject token, Vector3Int spawnPosition);
    List<GameObject> GetOccupantsInArea(List<Vector3Int> area);
}
