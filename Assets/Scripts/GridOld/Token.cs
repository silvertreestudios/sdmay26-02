using UnityEngine;

public class Token : MonoBehaviour
{
    void Awake()
    {
        var c = GridCharacterController3D.GetInstance();
        c.PlaceCreature(this.gameObject);
    }
}
