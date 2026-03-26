using UnityEngine;
using UnityEngine.Animations;

public class ViewModel : TokenMeshSelection
{
    public bool rotate {get; set;}
    public float rotationSpeed {get; set;}
    

    // Timer stuff for mesh switching
    private float meshTimer = 0f;
    private int currentMeshIndex = 0;

    
    protected new void Start()
    {
        base.Start(); 
        rotate = true; 
        rotationSpeed = 20f;
    }

    // use this to change the mesh 
    public void setMeshName(string name)
    {
        UpdateTokenMesh(name);
    }

    
    protected new void Update()
    {
        if (rotate)
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
        

        // Mesh switching logic, switch every 2 sec 
        // meshTimer += Time.deltaTime;
        // if (meshTimer >= 2f && TokenOptions.Length > 0)
        // {
        //     meshTimer = 0f;
        //     Debug.Log("Switching mesh to: " + TokenOptions[currentMeshIndex].Name);
        //     setMeshName(TokenOptions[currentMeshIndex].Name);
        //     currentMeshIndex = (currentMeshIndex + 1) % TokenOptions.Length;
        // }
    }
}