using UnityEngine;

public class Door : MonoBehaviour
{
    public GameObject door;
    IGridMemory gridMemory;
    int gridX;
    int gridZ;
    bool hasGridPosition;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // door = gameObject;
        gridMemory = IGridMemory.GetInstance();
        hasGridPosition = TryResolveGridPosition();
    }

    // Update is called once per frame
    void Update()
    {
        if (door == null)
        {
            return;
        }

        if (gridMemory == null)
        {
            gridMemory = IGridMemory.GetInstance();
            if (gridMemory == null)
            {
                return;
            }
        }

        if (!hasGridPosition)
        {
            hasGridPosition = TryResolveGridPosition();
            if (!hasGridPosition)
            {
                return;
            }
        }

        if (!gridMemory.IsDoor(gridX, gridZ))
        {
            return;
        }

        bool isOpen = gridMemory.IsDoorOpen(gridX, gridZ);
        SetDoorEnabled(!isOpen);
    }

    bool TryResolveGridPosition()
    {
        string[] nameParts = gameObject.name.Split('_');
        if (nameParts.Length >= 3 && int.TryParse(nameParts[1], out int parsedX) && int.TryParse(nameParts[2], out int parsedZ))
        {
            gridX = parsedX;
            gridZ = parsedZ;
            return true;
        }

        if (gridMemory == null)
        {
            return false;
        }

        gridX = Mathf.FloorToInt((transform.position.x - gridMemory.Origin.x) / gridMemory.CellSize);
        gridZ = Mathf.FloorToInt((transform.position.z - gridMemory.Origin.y) / gridMemory.CellSize);
        return true;
    }

    void SetDoorEnabled(bool enabled)
    {
        door.GetComponent<MeshRenderer>().enabled = enabled;
    }
}