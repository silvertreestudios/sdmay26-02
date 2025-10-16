using UnityEngine;

public class ImageToGrid : MonoBehaviour
{
    [Header("Level Image")]
    [SerializeField] private Texture2D img;

    public int[,] grid;

    public void GenerateGrid()
    {
        if (img == null) return;
        grid = new int[img.width, img.height];
        for (int i = 0; i < img.width; i++)
        {
            for (int j = 0; j < img.height; j++)
            {
                Color pixel = img.GetPixel(i, j);
                grid[i, j] = (pixel == Color.black) ? 1 : 0;
            }
        }
    }
}