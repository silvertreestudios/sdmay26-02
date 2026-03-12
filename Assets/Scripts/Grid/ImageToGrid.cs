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
        
        Color32 black = new Color32(0, 0, 0, 255);      // #000000 - black for ground
        Color32 yellow = new Color32(255, 255, 0, 255); // #FFFF00 - yellow for walls
        Color32 brown = new Color32(165, 42, 42, 255); // #A52A2A - brown for doors

        for (int i = 0; i < img.width; i++)
        {
            for (int j = 0; j < img.height; j++)
            {
                Color32 pixel = img.GetPixel(i, j);

                if (pixel.r == black.r && pixel.g == black.g && pixel.b == black.b)
                {
                    grid[i, j] = 1; // ground
                }
                else if (pixel.r == yellow.r && pixel.g == yellow.g && pixel.b == yellow.b)
                {
                    grid[i, j] = 2; // wall
                }
                else if (pixel.r == brown.r && pixel.g == brown.g && pixel.b == brown.b)
                {
                    grid[i, j] = 3; // door
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
        // Debug.Log(gridString);
    }
}