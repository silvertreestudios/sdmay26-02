using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public abstract class GridAPI : SingletonMonoBehaviour<GridAPI>
{
    // set dependencies
    protected GridCharacterController3D Controller => GridCharacterController3D.GetInstance();


    public abstract bool IsIdle();
    public abstract bool IsMoving();

    // cancel current action
    public abstract void CancelCurrentAction();

    // contstruct stride
    public abstract IEnumerator Stride(GameObject character, CoroutineResult<bool> canceled);
    // construct Strike
    public abstract IEnumerator Strike(GameObject attacker, int range, CoroutineResult<GameObject> target, CoroutineResult<bool> canceled);

}