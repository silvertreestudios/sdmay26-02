using UnityEngine;
using UnityEngine.U2D;
//change to be non monoBehaviour, its not needed
[System.Serializable]
public class ImageToGrid
{
    [Header("Level Image")]
    [SerializeField] private Texture2D img;
    [SerializeField] private Texture2D img2;

    public int[,] grid;
    public int[,] wall;

    // black pixel indicates ground
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

    // green pixel indicates wall, 3D cube prefab
    public int[,] GenerateWalls()
    {
        if (img2 == null) return null;
        wall = new int[img2.width, img2.height];
        for (int i = 0; i<img2.width; i++)
        {
            for (int j = 0; j < img2.height; j++)
            {
                Color pixel = img2.GetPixel(i, j);
                wall[i, j] = (pixel == Color.blue) ? 1 : 0;

            }
        }
        return wall;
    }

    // getters
    public int[,] GetGrid()
    {
        return grid;
    }

    public int[,] GetWall()
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

    // print grid to console for debugging
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

    // print wall to console for debugging
    public void PrintWalls()
    {
        if (wall == null) return;
        string wallString = "";
        for (int j = wall.GetLength(1) - 1; j >= 0; j--)
        {
            for (int i = 0; i < wall.GetLength(0); i++)
            {
                wallString += wall[i, j] + " ";
            }
            wallString += "\n";
        }
        Debug.Log(wallString);
    }
}