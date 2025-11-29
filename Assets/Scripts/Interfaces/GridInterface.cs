using System;
using System.Collections;
using UnityEngine;

public abstract class GridInterface : SingletonMonoBehaviour<GridInterface>
{
    public abstract IEnumerator MoveCreaturePosition(GameObject token, Vector3Int targetPosition, Vector3Int startPosition);
    public abstract Boolean IsCellWalkable(Vector3Int position);
    public abstract IEnumerator targetSelect(int range, CoroutineResult<GameObject> result);
    public abstract void SetCreaturePosition(GameObject token, Vector3Int spawnPosition);

    
}
