using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// TODO
/// turn it to move to one location at a time instead of all at once
/// </summary>




public class tokenMovement : ITokenMovement
{
    // Jump points for the piece to move between
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;

    // Private variables
    private Transform objectTransform;
    List<Vector3Int> path_points = new List<Vector3Int>();
    private Vector3 current_jump_point;
    private bool isJumping = false;
    private Vector3 targetJump;
    private float currentTime;
    private Vector3 direction;
    private int currentPathIndex = 0;
    private Vector3Int targetJumpPoint;

    public tokenMovement(Transform objectTransform, float stepHeight, float maxRotation, AnimationCurve ptLerp, AnimationCurve yLerp)
    {
        this.objectTransform = objectTransform;
        this.stepHeight = stepHeight;
        this.maxRotation = maxRotation;
        this.ptLerp = ptLerp;
        this.yLerp = yLerp;
        current_jump_point = objectTransform.position;
        currentTime = 0.0f;
    }


    // Sets the SINGLE target point for the piece to move to
    public int setMoveToPoint(Vector3Int target)
    {
        if (target == null)
        {
            Debug.Log("failed to set move to point, target is null");
            return -1;
        }
        else
        {
            targetJumpPoint = target;
            Debug.Log("successfully set move to point");
            return 0;
        } 
    }
    public IEnumerator moveToPoint(float time)
    {   
        // If we're not currently jumping
        if (!isJumping)
        {
            StartJump(targetJumpPoint);
        }
        // If we are jumping, continue the current jump
        if (isJumping)
        {
            movePieceSin(targetJumpPoint, time);
        }
        yield return null;
    }

    // Sets the list of path points for the piece to move along
    public int setPathPoints(List<Vector3Int> points)
    {
        if (path_points.Count > 0)
        {
            Debug.Log("failed to set path points, path_points length greater than 0");
            return -1;
        }
        else
        {
            path_points.Clear();
            // Skip the first point since it's the current position
            for (int i = 1; i < points.Count; i++)
            {
                path_points.Add(points[i]);
            }
            Debug.Log("successfully set path points");
            return 0;
        }
    }

    // Moves the piece along the given list of target positions, with each jump taking the specified time
    public void moveAlongPath(float time)
    {
        // If we're not currently jumping and there are more points to visit
        if (!isJumping && currentPathIndex < path_points.Count)
        {
            StartJump(path_points[currentPathIndex]);
        }
        // If we are jumping, continue the current jump
        if (isJumping)
        {
            movePieceSin(targetJump, time);
        }
        // If we've reached the end of the path, clear the path points to reset
        if (currentPathIndex >= path_points.Count)
        {
            //Debug.Log("Reached end of path points, clearing path points");
            path_points.Clear();
            currentPathIndex = 0;
        }
    }

    // Initiates a jump to the specified target position
    private void StartJump(Vector3 target)
    {
        targetJump = DisgustingFix(target);
        current_jump_point = objectTransform.position;
        direction = targetJump - objectTransform.position;
        direction = direction.normalized;
        currentTime = 0.0f;
        isJumping = true;
        Debug.Log("Starting jump from " + current_jump_point.ToString() + " to " + targetJump.ToString());
    }

    // Moves the piece along a the animation curve to the target position
    private void movePieceSin(Vector3 target, float jumpTime)
    {
        Vector3 start = current_jump_point;
        Vector3 end = target;
        //Vector3 end = DisgustingFix(target);

        // Debug.Log("Moving piece from " + start.ToString() + " to " + end.ToString());

        // Update the current time
        currentTime += Time.deltaTime;
        float time = Mathf.Clamp01(currentTime / jumpTime);

        // Calculate the new position using the animation curves
        Vector3 position = Vector3.Lerp(start, end, ptLerp.Evaluate(time));
        position.y += stepHeight * yLerp.Evaluate(time);
        // Debug.Log("At time " + time.ToString("F2") + ", position is " + position.ToString());
        // Apply the new position and rotation
        objectTransform.position = position;
                // Replace the rotation code in movePieceSin with:
        // Calculate tilt rotation (for jump animation)
        Vector3 tiltEuler = new Vector3(
            maxRotation * yLerp.Evaluate(time) * direction.z,
            0.0f,
            maxRotation * yLerp.Evaluate(time) * -direction.x
        );
        Quaternion tiltRotation = Quaternion.Euler(tiltEuler);

        // Calculate look rotation (face movement direction)
        Quaternion targetLookRotation = Quaternion.LookRotation(direction);

        Quaternion totalRotation = tiltRotation * targetLookRotation;
        // Combine both rotations: apply look rotation first, then tilt
        objectTransform.rotation = Quaternion.Slerp(objectTransform.rotation, totalRotation, Time.deltaTime * 40f);


        // If the jump is complete
        if (time >= 1.0f)
        {
            //snap to final position
            objectTransform.position = end;
            current_jump_point = end;
            isJumping = false;
            // Move to next point in path
            currentPathIndex++;
            Debug.Log("Completed jump to " + end.ToString());
        }
    }

    public void lookAt(Vector3Int target)
    {
        Vector3 direction = target - objectTransform.position;
        direction.y = 0; // Keep only horizontal direction
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            objectTransform.rotation = Quaternion.Slerp(objectTransform.rotation, targetRotation, Time.deltaTime * 5f); // Smooth rotation
        }
    }


    //This belongs in the dumpster, fix and delete ASAP
    private Vector3 DisgustingFix(Vector3 targetJumpPoint)
    {
        return new Vector3(targetJumpPoint.x + 0.5f, objectTransform.position.y, targetJumpPoint.z + 0.5f);
    }
}
