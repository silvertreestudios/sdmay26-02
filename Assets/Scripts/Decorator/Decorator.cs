using UnityEditor;
using UnityEngine;

public class Decorator : MonoBehaviour
{
    public GameObject Wall; // Assign this in the Inspector
    public GameObject Door; // Assign this in the Inspector
    public Material Grass; 
    public Material Dirt;
    public Material GrassBillboard;
    private DecoratorMap dm;
    private TileOld[,] grid;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    
    
    void Start()
    {

        dm = DecoratorMap.Instance;
        Debug.Log("DecoratorMap instance created");
        dm.Initialize(IGridMemory.GetInstance());
        grid = dm.GetTileMap();
        Debug.Log($"Grid dimensions: {grid.GetLength(0)} x {grid.GetLength(1)}");

        GameObject parentObject = new GameObject("DecoratedTiles");


        foreach (TileOld tile in grid)
        {
            switch (tile.TileType)
            {
                case TileOld.Type.Walkable:
                    //TODO: make this add to one mesh and refrence a material so can use tiling and save draw calls
                    //Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.green);
                    DrawQuad(tile, Dirt);
                    break;
                case TileOld.Type.Ground:
                    //Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.yellow);
                    DrawQuad(tile, Grass);
                    break;
                case TileOld.Type.Void:
                    DrawQuad(tile, Grass);
                    //Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.black);
                    break;
                case TileOld.Type.Wall:
                    //Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.red);
                    if (Wall != null)
                    {
                        DrawQuad(tile, Dirt);

                        GameObject wallPrefab = Instantiate(Wall, tile.WorldPosition, Quaternion.identity, parentObject.transform);
                        wallPrefab.name = $"Wall_{tile.GridPosition.x}_{tile.GridPosition.z}";
                        //wallPrefab.GetComponent<Wall>().setStyle(tile.GridPosition.x, tile.GridPosition.z, grid);
                    }
                    else
                    {
                        Debug.LogWarning("Wall prefab not assigned in Decorator script.");
                    }
                    break;
                case TileOld.Type.Door:
                    DrawQuad(tile, Dirt);
                    GameObject doorPrefab = Instantiate(Door, tile.WorldPosition, Quaternion.identity, parentObject.transform);
                    doorPrefab.name = $"Door_{tile.GridPosition.x}_{tile.GridPosition.z}";
                    break;
            }
        }
        // Was an attempt to add grass textures to the map using perlin noise 
        //VisualizePerlinNoise(0.55f, 0.05f, null);
    }

    private void DrawQuad(TileOld tile, Material mat)
    {
        if (mat != null)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = tile.WorldPosition;
            quad.transform.rotation = Quaternion.Euler(90, 0, 0); // Rotate to lie flat on the XZ plane
            quad.GetComponent<MeshRenderer>().material = mat;
            quad.transform.SetParent(transform); 
        }
        else
        {
            Debug.LogWarning("Material not assigned in Decorator script.");
        }
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void VisualizePerlinNoise(float threshold, float scale, Material purpleMaterial)
    {
        if (grid == null)
        {
            Debug.LogWarning("Grid not initialized.");
            return;
        }

        foreach (TileOld tile in grid)
        {   
            if (tile.TileType != TileOld.Type.Void) continue; // Only visualize on walkable tiles
            float noiseValue = Mathf.PerlinNoise(tile.WorldPosition.x * scale, tile.WorldPosition.z * scale);
            if (noiseValue > threshold)
            {
                // First quad at 0,45,0
                GameObject quad1 = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad1.transform.position = tile.WorldPosition;
                quad1.transform.rotation = Quaternion.Euler(0, 45, 0);
                if (purpleMaterial != null)
                    quad1.GetComponent<MeshRenderer>().material = purpleMaterial;
                else
                    quad1.GetComponent<MeshRenderer>().material.color = new Color(0.5f, 0, 0.5f);
                quad1.transform.SetParent(transform);

            // // Second quad at 0,-45,0
            // GameObject quad2 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            // quad2.transform.position = tile.WorldPosition;
            // quad2.transform.rotation = Quaternion.Euler(0, -45, 0);
            // if (purpleMaterial != null)
            //     quad2.GetComponent<MeshRenderer>().material = purpleMaterial;
            // else
            //     quad2.GetComponent<MeshRenderer>().material.color = new Color(0.5f, 0, 0.5f);
            // quad2.transform.SetParent(transform);
            // }

            // // Second quad at 0,-45,0
            // GameObject quad3 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            // quad3.transform.position = tile.WorldPosition;
            // quad3.transform.rotation = Quaternion.Euler(0, 0, 0);
            // if (purpleMaterial != null)
            //     quad3.GetComponent<MeshRenderer>().material = purpleMaterial;
            // else
            //     quad3.GetComponent<MeshRenderer>().material.color = new Color(0.5f, 0, 0.5f);
            // quad3.transform.SetParent(transform);

            //             // Second quad at 0,-45,0
            // GameObject quad4 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            // quad4.transform.position = tile.WorldPosition;
            // quad4.transform.rotation = Quaternion.Euler(0, 90, 0);
            // if (purpleMaterial != null)
            //     quad4.GetComponent<MeshRenderer>().material = purpleMaterial;
            // else
            //     quad4.GetComponent<MeshRenderer>().material.color = new Color(0.5f, 0, 0.5f);
            // quad4.transform.SetParent(transform);
            }
        }
    }
}
