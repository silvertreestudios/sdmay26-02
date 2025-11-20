// ...new file...
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ITokenMovement
{
    int setPath(List<Vector3Int> path);
    int setPoint(Vector3 point);
    int setLookAt(Vector3 target);
    bool IsMoving();
    void stop();
    void start();
    IEnumerator update();
}