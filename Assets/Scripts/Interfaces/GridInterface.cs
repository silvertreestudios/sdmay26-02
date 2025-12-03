using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class GridInterface : SingletonMonoBehaviour<GridInterface>
{
    public abstract void MoveCreaturePosition(GameObject token, Vector3Int targetPosition, Vector3Int startPosition);
    public abstract Boolean IsCellWalkable(Vector3Int position);
    public abstract IEnumerator TargetSelect(int range, CoroutineResult<GameObject> result);
    public abstract void SetCreaturePosition(GameObject token, Vector3Int spawnPosition);
    public abstract List<GameObject> GetOccupantsInArea(List<Vector3Int> area);
    
}
