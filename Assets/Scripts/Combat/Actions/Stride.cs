using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Stride : MultiFrameEntityAction
{
    public Stride(uint cost) : base(cost)
    {
        
    }

    protected override IEnumerator MFInvoke(GameObject target)
    {
        // Get the grid controller
        GridCharacterController3D gridController = GridCharacterController3D.Instance;
        
        if (gridController == null)
        {
            Debug.LogError("[Stride] GridCharacterController3D instance not found!");
            yield break;
        }

        // Determine character name from target GameObject
        // "Player 1" -> "Player1" (remove space for dictionary lookup)
        string characterName = target.name.Replace("Player ", "Player");
        
        Debug.Log($"[Stride] Attempting to move {characterName}");
        
        // Get the previewed path (player must have selected a path first)
        List<Vector3Int> path = gridController.GetPreviewedPathForCharacter(characterName);
        
        if (path == null || path.Count < 2)
        {
            Debug.LogWarning("[Stride] No valid path selected! Click on grid to select destination first.");
            yield break;
        }

        Debug.Log($"[Stride] Executing movement for {characterName} along path with {path.Count} waypoints");
        
        // Execute the movement using the grid controller
        yield return gridController.ExecuteMovement(characterName, path);
        
        Debug.Log("[Stride] Movement action completed. Automatically ending turn...");
        
        // Get the ActionController component and end the turn
        ActionController actionController = target.GetComponent<ActionController>();
        if (actionController != null)
        {
            actionController.EndTurn();
        }
        else
        {
            Debug.LogError("[Stride] ActionController not found on target!");
        }
    }
}