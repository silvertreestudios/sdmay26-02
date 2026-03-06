using System;
using System.Collections.Generic;
using Game.Creature;
using UnityEngine;

public class TokenMeshSelection : MonoBehaviour
{
    public TokenMeshes[] TokenOptions = new TokenMeshes[1];
    public BaseMeshes[] BaseOptions = new BaseMeshes[1];
    GameObject tokenObject;
    GameObject baseObject;
    CreatureComponent creatureComponent;
    MeshRenderer TokenMeshRenderer;
    MeshFilter TokenMeshFilter;
    MeshRenderer BaseMeshRenderer;
    MeshFilter BaseMeshFilter;

    private string TokenMeshToFind;

    #if UNITY_EDITOR
    void OnValidate()
    {
        // Only update in editor when values change and object is in a scene
        if (gameObject.scene.IsValid())
        {
            UpdateTokenMesh();
        }
    }
    #endif

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        UpdateTokenMesh();
    }
 
    void UpdateTokenMesh()
    {
        // Safety check for destroyed objects
        if (this == null || gameObject == null) return;


        tokenObject = transform.GetChild(0).gameObject;
        baseObject = transform.GetChild(1).gameObject;



        TokenMeshFilter = tokenObject.GetComponent<MeshFilter>();
        TokenMeshRenderer = tokenObject.GetComponent<MeshRenderer>();

        BaseMeshFilter = baseObject.GetComponent<MeshFilter>();
        BaseMeshRenderer = baseObject.GetComponent<MeshRenderer>();

        if (TokenMeshFilter == null)
        {
            Debug.LogWarning("No MeshFilter component found!");
            return;
        }

        creatureComponent = GetComponentInParent<CreatureComponent>();
        
        if (creatureComponent != null)
        {
            TokenMeshToFind = creatureComponent.name;

        }
        else
        {
            TokenMeshToFind = "Wizard"; // Default mesh name if no CreatureComponent found
        }
        
        // Don't proceed if we have nothing to search for
        if (string.IsNullOrEmpty(TokenMeshToFind))
        {
            return;
        }
        //Debug.Log("Looking for mesh: " + TokenMeshToFind);
        bool meshFound = false;

        foreach (TokenMeshes entry in TokenOptions)
        {
            if (entry != null && entry.Name == TokenMeshToFind)
            {
                if (entry.mesh != null)
                {
                    TokenMeshFilter.sharedMesh = entry.mesh;
                    //Debug.Log($"Selected {entry.Name} Mesh");
                    meshFound = true;
                    break;
                }
                else
                {
                    Debug.LogError($"Mesh for {entry.Name} is null!");
                    meshFound = true;
                    break;
                }
            }
        }


        BaseMeshFilter.sharedMesh = BaseOptions[0].mesh;

        if (!meshFound)
        {
            Debug.LogError($"No mesh found with name: {TokenMeshToFind}");
            TokenMeshFilter.sharedMesh = null;
        }
    }

    // Update is called once per frame
    void Update()
    {

    }


}

[System.Serializable]
public class TokenMeshes
{
    public Mesh mesh;
    public string Name = "default";
    
}

[System.Serializable]
public class BaseMeshes
{
    public Mesh mesh;
    public int level = 0;
    
}
