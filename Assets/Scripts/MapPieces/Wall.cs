using UnityEngine;

public class Wall : MonoBehaviour
{
    Transform wall;
    Transform cap;
    Transform corner;
    Transform crossIntersection;
    Transform pillar;
    Transform tIntersection;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        Transform transform = GetComponent<Transform>();
        wall = FindDeepChild(transform, "Wall");
        cap = FindDeepChild(transform, "Cap");
        corner = FindDeepChild(transform, "Corner");
        crossIntersection = FindDeepChild(transform, "Cross_Intersection");
        pillar = FindDeepChild(transform, "Pillar");
        tIntersection = FindDeepChild(transform, "T_Intersection");
        // Recursively searches for a descendant Transform by name
        Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                var result = FindDeepChild(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }
    }

    public void setStyle (int x, int z, Tile[,] grid)
    {
        // Check the four cardinal directions for neighboring walls
        bool up = (z < grid.GetLength(1) - 1) && (grid[x, z + 1].TileType == Tile.Type.Wall);
        bool down = (z > 0) && (grid[x, z - 1].TileType == Tile.Type.Wall);
        bool left = (x < grid.GetLength(0) - 1) && (grid[x + 1, z].TileType == Tile.Type.Wall);
        bool right = (x > 0) && (grid[x - 1, z].TileType == Tile.Type.Wall);

        bool isCrossIntersection = up && down && left && right;
        bool isWall = (up && down) || (left && right);
        bool isCorner = (up && right) || (up && left) || (down && right) || (down && left);
        bool isTIntersection = (up && left && right) || (down && left && right) || (left && up && down) || (right && up && down);
        bool isCap = (up || down || left || right);
        bool isPillar = !up && !down && !left && !right;


        if (isCrossIntersection)
        {
            crossIntersection.GetComponent<MeshRenderer>().enabled = true;
            // Debug.Log($"Setting wall style at ({x}, {z}) to Cross Intersection");
            return;
        }
        else if (isTIntersection)
        {
            tIntersection.GetComponent<MeshRenderer>().enabled = true;
            // Debug.Log($"Setting wall style at ({x}, {z}) to T Intersection");
            if (!up) transform.rotation = Quaternion.Euler(0, 270, 0);
            else if (!down) transform.rotation = Quaternion.Euler(0, 90, 0);
            else if (!left) transform.rotation = Quaternion.Euler(0, 0, 0);
            else if (!right) transform.rotation = Quaternion.Euler(0, 180, 0);
            return;
        }
        else if (isCorner)
        {
            corner.GetComponent<MeshRenderer>().enabled = true;
            // Debug.Log($"Setting wall style at ({x}, {z}) to Corner");
            if (up && right) transform.rotation = Quaternion.Euler(0, 0, 0);
            else if (up && left) transform.rotation = Quaternion.Euler(0, 90, 0);
            else if (down && right) transform.rotation = Quaternion.Euler(0, 270, 0);
            else if (down && left) transform.rotation = Quaternion.Euler(0, 180, 0);
            return;
        }
        else if(isWall) 
        {
            wall.GetComponent<MeshRenderer>().enabled = true;
            // Debug.Log($"Setting wall style at ({x}, {z}) to Wall");
            if (up && down)
                transform.rotation = Quaternion.Euler(0, 90, 0);
            else
                transform.rotation = Quaternion.Euler(0, 0, 0);
            return;
        }
        else if (isCap)
        {
            cap.GetComponent<MeshRenderer>().enabled = true;
            if (left)
            {
                //Debug.Log($"Setting wall style at ({x}, {z}) to Cap");
                transform.rotation = Quaternion.Euler(0, 0, 0);
            }
            else if (right)
            {
                //Debug.Log($"Setting wall style at ({x}, {z}) to Cap");
                transform.rotation = Quaternion.Euler(0, 180, 0);
            }
            else if (up)
            {
                //Debug.Log($"Setting wall style at ({x}, {z}) to Cap");
                transform.rotation = Quaternion.Euler(0, 270, 0);
            }
            else if (down)
            {
                //Debug.Log($"Setting wall style at ({x}, {z}) to Cap");
                transform.rotation = Quaternion.Euler(0, 90, 0);
            }
            return;
        }
        else if (isPillar)
        {
            pillar.GetComponent<MeshRenderer>().enabled = true;
            // Debug.Log($"Setting wall style at ({x}, {z}) to Pillar");
            return;
        }
    }
}
