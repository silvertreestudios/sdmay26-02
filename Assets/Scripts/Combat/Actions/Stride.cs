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
        // Notify GridCharacterController3D about active player
        GridCharacterController3D gridController = GridCharacterController3D.Instance;
        if (gridController != null)
        {
            Debug.Log("Test");
            //target.GetInstanceID();
            gridController.SetActivePlayer(target);
            Debug.Log("Test2");
        }
        else
        {
            Debug.LogError("[Stride] GridCharacterController3D instance not found!");
            yield break;
        }

        // Determine character name from target GameObject
        // "Player 1" -> "Player1" (remove space for dictionary lookup)
       
        
        Debug.Log($"[Stride] Attempting to move {target}");
        
        // Get the previewed path (player must have selected a path first)
        List<Vector3Int> path = gridController.GetPreviewedPathForCharacter(target);
        
        if (path == null || path.Count < 2)
        {
            Debug.LogWarning("[Stride] No valid path selected! Click on grid to select destination first.");
            yield break;
        }

        Debug.Log($"[Stride] Executing movement for {target} along path with {path.Count} waypoints");
        
        // Execute the movement using the grid controller
        yield return gridController.ExecuteMovement(target, path);
        
        Debug.Log("[Stride] Movement action completed.");
    }
}