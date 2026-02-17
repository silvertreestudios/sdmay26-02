using UnityEditor;
using UnityEngine;

public class Decorator : MonoBehaviour
{
    private DecoratorMap dm;
    private Tile[,] grid;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        dm = DecoratorMap.Instance;
        Debug.Log("DecoratorMap instance created");
        dm.Initialize(IGridMemory.GetInstance());
        grid = dm.GetTileMap();
        Debug.Log($"Grid dimensions: {grid.GetLength(0)} x {grid.GetLength(1)}");


    }

    // Update is called once per frame
    void Update()
    {
        foreach (Tile tile in grid)
        {
            switch (tile.TileType)
            {
                case Tile.Type.Walkable:
                    Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.green);
                    break;
                case Tile.Type.Ground:
                    Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.yellow);

                    break;
                case Tile.Type.Void:
                    Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.black);
                    break;
                case Tile.Type.Wall:
                    Debug.DrawLine(tile.WorldPosition, tile.WorldPosition + Vector3.up * 0.5f, Color.red);
                    break;
            }
        }
    }


}
