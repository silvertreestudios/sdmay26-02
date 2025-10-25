using UnityEngine;
//change to be non monoBehaviour, its not needed
[System.Serializable]
public class ImageToGrid
{
    [Header("Level Image")]
    [SerializeField] private Texture2D img;

    public int[,] grid;

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

    public int[,] GetGrid()
    {
        return grid;
    }

    public int GetWidth()
    {
        return img.width;
    }
    public int GetHeight()
    {
        return img.height;
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