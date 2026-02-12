using UnityEngine;
using UnityEngine.U2D;
//change to be non monoBehaviour, its not needed
[System.Serializable]
public class ImageToGrid
{
    [Header("Level Image")]
    [SerializeField] private Texture2D img;
    [SerializeField] private Texture3D img2;

    public int[,] grid;
    public int[,,] wall;

    public int[,] GenerateGrid()
    {
        if (img == null) return null;
        grid = new int[img.width, img.height];
        for (int i = 0; i < img.width; i++)
        {
            for (int j = 0; j < img.height; j++)
            {
                Color pixel = img.GetPixel(i, j);
                grid[i, j] = (pixel == Color.black) ? 1 : 0;
            }
        }
        return grid;
    }

    public int[,,] GenerateWall()
    {
        if (img2 == null) return null;
        wall = new int[img2.width, img2.height, img2.depth];
        for (int i = 0; i<img2.width; i++)
        {
            for (int j = 0; j < img2.height; j++)
            {
                for (int k = 0; k < img2.depth; k++)
                {
                    Color pixel = img2.GetPixel(i, j, k);
                    wall[i, j, k] = (pixel == Color.white) ? 1 : 0;

                }
            }
        }
        return wall;
    }



    // seperate color to indicate tile_type.wall
    // 3d cube prefab


    public int[,] GetGrid()
    {
        return grid;
    }

    public int[,,] GetWall()
    {
        return wall;
    }

    public int GetWidth()
    {
        return img.width;
    }
    public int GetHeight()
    {
        return img.height;
    }

    public int GetDepth()
    {
        return img2.depth;
    }

    public void PrintGrid()
    {
        if (grid == null) return;
        string gridString = "";
        for (int j = grid.GetLength(1) - 1; j >= 0; j--)
        {
            for (int i = 0; i < grid.GetLength(0); i++)
            {
                gridString += grid[i, j] + " ";
            }
            gridString += "\n";
        }
        Debug.Log(gridString);
    }
}