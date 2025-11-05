// ...new file...
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ITokenMovement
{
    bool IsMoving { get; }
    int setPathPoints(List<Vector3Int> points);
    void moveAlongPath(float time);
    void lookAt(Vector3Int target);
    int setMoveToPoint(Vector3Int target);
    IEnumerator moveToPoint(float time);
}