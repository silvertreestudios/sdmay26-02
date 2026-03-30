using UnityEngine;

namespace GridPrivate 
{
    public class Wall : MonoBehaviour, IOnGridGeneration
    {
        [SerializeField]
        protected Transform wall;
        [SerializeField]
        protected Transform cap;
        [SerializeField]
        protected Transform corner;
        [SerializeField]
        protected Transform crossIntersection;
        [SerializeField]
        protected Transform pillar;
        [SerializeField]
        protected Transform tIntersection;

        public void OnGeneration(Vector3Int position, in TileType[,] gridData)
        {
            int x = position.x;
            int z = position.z;
            // Check the four cardinal directions for neighboring walls
            bool up = (z < gridData.GetLength(1) - 1) && (gridData[x, z + 1] == TileType.Wall);
            bool down = (z > 0) && (gridData[x, z - 1] == TileType.Wall);
            bool left = (x < gridData.GetLength(0) - 1) && (gridData[x + 1, z] == TileType.Wall);
            bool right = (x > 0) && (gridData[x - 1, z] == TileType.Wall);


            bool upDoor = (z < gridData.GetLength(1) - 1) && (gridData[x, z + 1] == TileType.Door);
            bool downDoor = (z > 0) && (gridData[x, z - 1] == TileType.Door);
            bool leftDoor = (x < gridData.GetLength(0) - 1) && (gridData[x + 1, z] == TileType.Door);
            bool rightDoor = (x > 0) && (gridData[x - 1, z] == TileType.Door);

            bool isCrossIntersection = up && down && left && right;
            bool isWall = (up && down) || (left && right);
            bool isDoorAdjacent = upDoor || downDoor || leftDoor || rightDoor;
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
            else if (isDoorAdjacent)
            {
                wall.GetComponent<MeshRenderer>().enabled = true;
                // Debug.Log($"Setting wall style at ({x}, {z}) to Door Adjacent");
                if (upDoor || downDoor) transform.rotation = Quaternion.Euler(0, 90, 0);
                else transform.rotation = Quaternion.Euler(0, 0, 0);
                return;
            }
            else if (isWall)
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
}