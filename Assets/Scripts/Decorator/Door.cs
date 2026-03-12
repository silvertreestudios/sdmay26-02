using UnityEngine;
using System.Collections.Generic;
using Game.Creature;

public class Door : MonoBehaviour
{
    public GameObject door;
    public int maxHP = 1;
    private int currentHP;

    IGridMemory gridMemory;
    int gridX;
    int gridZ;
    bool hasGridPosition;

    void Start()
    {
        currentHP = maxHP;
        gridMemory = IGridMemory.GetInstance();
        hasGridPosition = TryResolveGridPosition();
    }

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

    // Called when door takes damage from an attack
    public void TakeDamage(List<DamageValue> damageValues, D20Result attackRoll)
    {
        if (currentHP <= 0)
        {
            return; 
        }

        // Calculate total damage
        int totalDamage = 0;
        foreach (var damageValue in damageValues)
        {
            totalDamage += damageValue.DamageAmount;
        }

        currentHP -= totalDamage;
        Debug.Log($"Door at ({gridX}, {gridZ}) took {totalDamage} damage. HP: {currentHP}/{maxHP}");

        if (currentHP <= 0)
        {
            currentHP = 0;
            OpenDoor();
        }
    }

    // taking raw damage without attack roll
    public void TakeDamage(int damage)
    {
        if (currentHP <= 0)
        {
            return;
        }

        currentHP -= damage;
        Debug.Log($"Door at ({gridX}, {gridZ}) took {damage} damage. HP: {currentHP}/{maxHP}");

        if (currentHP <= 0)
        {
            currentHP = 0;
            OpenDoor();
        }
    }

    private void OpenDoor()
    {
        if (!hasGridPosition || gridMemory == null)
        {
            return;
        }

        if (!gridMemory.IsDoor(gridX, gridZ))
        {
            return;
        }

        // Only open if currently closed
        if (!gridMemory.IsDoorOpen(gridX, gridZ))
        {
            gridMemory.ToggleDoor(gridX, gridZ);
            Debug.Log($"Door at ({gridX}, {gridZ}) has been broken open!");
        }
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

    // Get the door's current HP for UI or targeting purposes
    public int GetCurrentHP()
    {
        return currentHP;
    }

    // Check if door is broken (0 HP)
    public bool IsBroken()
    {
        return currentHP <= 0;
    }
}