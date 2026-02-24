using System.Collections.Generic;
using System;
using System.Collections;
using UnityEngine;

public abstract class IGridMemory : SingletonMonoBehaviour<IGridMemory>
{
    //not sure if this is proper structre, ask cole
    public abstract int Width { get; protected set; }
    public abstract int Height { get; protected set; }
    public abstract int GridY { get; protected set; }
    public abstract float CellSize { get; protected set; }
    public abstract Vector3 Origin { get; protected set; }
    public object GridInfo { get; internal set; }

    ///

    public abstract void Initialize(int width, int height, int gridY, float cellSize, Vector3 origin, int[,] gridData);

    public abstract bool GetIsOccupied(int x, int z);
    public abstract void SetIsOccupied(int x, int z, bool occupied);
    public abstract void SetStatus(int x, int z, GridMemory.TileStatus statusToSet);
    public abstract bool HasStatus(int x, int z, GridMemory.TileStatus statusToCheck);
    public abstract void ClearCreaturePosition(GameObject token, Vector3Int position);
    public abstract void MoveCreaturePosition(GameObject token, Vector3Int targetPosition, Vector3Int startPosition);
    public abstract Boolean IsCellWalkable(Vector3Int position);
    public abstract IEnumerator TargetSelect(int range, CoroutineResult<GameObject> result);
    public abstract void SetCreaturePosition(GameObject token, Vector3Int spawnPosition);
    public abstract List<GameObject> GetOccupantsInArea(List<Vector3Int> area);
}
