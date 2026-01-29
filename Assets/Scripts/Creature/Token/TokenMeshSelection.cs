using System;
using System.Collections.Generic;
using Game.Creature;
using UnityEngine;

public class TokenMeshSelection : MonoBehaviour
{
    public MeshPathEntry[] meshOptions = new MeshPathEntry[1];

    CreatureComponent creatureComponent;
    
    public string selectedMeshFromInspector; // Set by Inspector
    MeshRenderer MeshRenderer;
    MeshFilter MeshFilter;

    public string meshToFind;

    #if UNITY_EDITOR
    void OnValidate()
    {
        // Only update in editor when values change and object is in a scene
        if (gameObject.scene.IsValid())
        {
            UpdateMesh();
        }
    }
    #endif

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        UpdateMesh();
    }

    void UpdateMesh()
    {
        // Safety check for destroyed objects
        if (this == null || gameObject == null) return;

        MeshFilter = GetComponent<MeshFilter>();
        MeshRenderer = GetComponent<MeshRenderer>();

        if (MeshFilter == null)
        {
            Debug.LogWarning("No MeshFilter component found!");
            return;
        }

        creatureComponent = GetComponentInParent<CreatureComponent>();
        // FIX: Check if creatureComponent is null before accessing it
        if (creatureComponent != null)
        {
            meshToFind = creatureComponent.name;
        }
        else
        {
            meshToFind = selectedMeshFromInspector;
        }
        
        // Don't proceed if we have nothing to search for
        if (string.IsNullOrEmpty(meshToFind))
        {
            return;
        }
        Debug.Log("Looking for mesh: " + meshToFind);
        bool meshFound = false;

        foreach (MeshPathEntry entry in meshOptions)
        {
            if (entry != null && entry.Name == meshToFind)
            {
                if (entry.mesh != null)
                {
                    MeshFilter.sharedMesh = entry.mesh;
                    Debug.Log($"Selected {entry.Name} Mesh");
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

        if (!meshFound)
        {
            Debug.LogError($"No mesh found with name: {meshToFind}");
            MeshFilter.sharedMesh = null;
        }
    }

    // Update is called once per frame
    void Update()
    {

    }


}

[System.Serializable]
public class MeshPathEntry
{
    public Mesh mesh;
    public string Name = "default";
    
}
