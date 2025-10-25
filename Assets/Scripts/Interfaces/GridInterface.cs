using System.Collections;
using UnityEngine;

public abstract class GridInterface : SingletonMonoBehaviour<GridInterface>
{
    public abstract IEnumerator MoveCreature();

    
}
