using GridPrivate;
using System;
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
public class Map : MonoBehaviour
{
    [SerializeField]
    protected Texture2D ImageMap;
    [SerializeField]
    protected float spacing = 1f;
    [SerializeField]
    TileSettings Settings;

    /// <summary>
    /// Stored Data of the Grid
    /// </summary>
    protected TileType[,] GridData { get; set; }

    void OnValidate()
    {
        #if UNITY_EDITOR
        if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this))
            return;
        // Defer the rebuild to avoid "DestroyImmediate is not permitted during OnValidate"
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && !Application.isPlaying)
                Generate();
        };
        #endif
    }

    /// <summary>
    /// (Re)Generates the map given an image and
    /// defined tiles for the image
    /// </summary>
    [ContextMenu("Generate")]
    public void Generate()
    {
        if (ImageMap == null) return;

        Settings.ResetCache();
        ClearChildren();
        GetMapData();


        for (int x = 0; x < ImageMap.width; x++)
        {
            for (int z = 0; z < ImageMap.height; z++)
            {
                // Get tile decorations
                Color pixel = ImageMap.GetPixel(x, z);
                var (_, prefab, floor) = Settings.GetTileInfo(pixel);

                // Place in scene
                Vector3 pos = new Vector3(x * spacing, 0, z * spacing);
                if (prefab != null)
                {
                    GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    obj.transform.position = pos;
                    obj.transform.rotation = prefab.transform.rotation;
                    obj.transform.parent = transform;
                    obj.GetComponent<IOnGridGeneration>()?.OnGeneration(new Vector3Int(x,0,z), GridData);
                }

                if(floor)
                {
                    GameObject quad = Quad(pos);
                    quad.GetComponent<MeshRenderer>().material = floor;
                }
            }
        }
    }

    public TileType[,] GetMapData()
    {
        if(GridData != null)
            return GridData;
        if (ImageMap == null) 
            return null;

        GridData = new TileType[ImageMap.width, ImageMap.height];

        for (int x = 0; x < ImageMap.width; x++)
        {
            for (int z = 0; z < ImageMap.height; z++)
            {
                // Get Tile Type
                Color pixel = ImageMap.GetPixel(x, z);
                GridData[x, z] = Settings.GetTileType(pixel);
            }
        }

        return GridData;
    }

    /// <summary>
    /// Clears the generated map
    /// </summary>
    [ContextMenu("Clear")]
    protected void ClearChildren()
    {
        while (transform.childCount > 0)
        {
            #if UNITY_EDITOR
            DestroyImmediate(transform.GetChild(0).gameObject);
            #else
            Destroy(transform.GetChild(0).gameObject);
            #endif
        }
    }

    /// <summary>
    /// Spawns a quad for a tile at a given position
    /// </summary>
    /// <param name="position"></param>
    /// <returns>The spawned quad</returns>
    protected GameObject Quad(Vector3 position)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.position = position;
        quad.transform.rotation = Quaternion.Euler(90, 0, 0); // Rotate to lie flat on the XZ plane
        quad.transform.SetParent(transform);
        return quad;
    }
}
