using UnityEngine;
using UnityEngine.U2D;
//change to be non monoBehaviour, its not needed
[System.Serializable]
public class ImageToGrid
{
    [Header("Level Image")]
    [SerializeField] private Texture2D img;
  

    public int[,] grid;

    // black pixel indicates ground, yellow tiles placed on these coordinates
    public int[,] GenerateGrid()
    {
        if (img == null) return null;
        grid = new int[img.width, img.height];
        for (int i = 0; i < img.width; i++)
        {
            for (int j = 0; j < img.height; j++)
            {
                Color pixel = img.GetPixel(i, j);

                if (pixel == Color.black)
                {
                    grid[i, j] = 1; //ground
                }
                else if (pixel == Color.yellow)
                {
                    grid[i, j] = 2; //wall
                    // reference library for hex program for correct color
                }
                else
                {
                    grid[i, j] = 0; //empty
                }
                
            }
        }
        return grid;
    }

    // getters
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
}