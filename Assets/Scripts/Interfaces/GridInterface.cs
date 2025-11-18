using System.Collections;
using UnityEngine;

public abstract class GridInterface : SingletonMonoBehaviour<GridInterface>
{
    public abstract IEnumerator MoveCreature(GameObject token);
    public abstract IEnumerator targetSelect(int range, CoroutineResult<GameObject> result);

    
}
