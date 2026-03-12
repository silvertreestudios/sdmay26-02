using UnityEngine;
using UnityEngine.Animations;

public class ViewModel : TokenMeshSelection
{
    public bool rotate {get; set;}
    public float rotationSpeed {get; set;}

    
    protected new void Start()
    {
        base.Start(); 
        rotate = true; 
        rotationSpeed = 20f;
    }

    // use this to change the mesh 
    public void setMeshName(string name)
    {
        TokenMeshToFind = name;
        UpdateTokenMesh();
    }

    
    protected new void Update()
    {
        if (rotate)
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
    }
}