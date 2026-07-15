using UnityEngine;

public class ViewModel : TokenMeshSelection
{
    public bool rotate { get; set; }
    public float rotationSpeed { get; set; }

    protected override void Start()
    {
        base.Start();
        rotate = true;
        rotationSpeed = 20f;
    }

    public void setMeshName(string name)
    {
        UpdateTokenMesh(name);
    }

    protected override void Update()
    {
        if (rotate)
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }
}
