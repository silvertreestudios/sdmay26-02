using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Animations;
using System;

public class tokenMovement : ITokenMovement
{
    // Jump points for the piece to move between
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;

    // Private variables
    private Transform objectTransform;
    List<Vector3> path_points = new List<Vector3>();
    private Vector3 current_jump_point;
    private bool isMoving = false;
    private bool isDoneMovingToPoint = true;
    private Vector3 lookAtTarget;
    private float currentTimeRotation;

    //used just for interface
    private Vector3 targetJump;
    private float currentTime;
    private Vector3 direction;
    private int currentPathIndex = 0;
    private bool running = true;
    private bool isDone = true;


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
    public int setPoint(Vector3 target)
    {
        if (path_points.Count > 0)
        {
            //Debug.Log("failed to set move to point, target is null");
            return -1;
        }
        else
        {
            path_points.Clear();
            //targetJumpPoint = DisgustingFix(target);
            path_points.Add(target);
            isDone = false;
            //Debug.Log("successfully set move to point");
            return 0;
        }
    }

    // Sets the list of path points for the piece to move along
    public int setPath(List<Vector3Int> points)
    {
        if (path_points.Count > 0)
        {
            //Debug.Log("failed to set path points, path_points length greater than 0");
            return -1;
        }
        else
        {
            path_points.Clear();
            // Skip the first point since it's the current position
            for (int i = 1; i < points.Count; i++)
            {
                path_points.Add(points[i]);
                isDone = false;
            }
            //Debug.Log("successfully set path points");
            return 0;
        }
    }


    // Moves the piece along the given list of target positions, with each jump taking the specified time
    public void move(float time)
    {
        // If we're not currently jumping and there are more points to visit
        if (!isMoving && currentPathIndex < path_points.Count && running)
        {
            StartJump(path_points[currentPathIndex]);
        }
        // If we are jumping, continue the current jump. Once done, move to the next point
        if (isMoving)
        {
            movePiece(targetJump, time);
            if (isDoneMovingToPoint) { currentPathIndex++; }
        }
        // If we've reached the end of the path, clear the path points to reset
        if (currentPathIndex >= path_points.Count)
        {
            //Debug.Log("Reached end of path points, clearing path points");
            path_points.Clear();
            isDone = true;
            currentPathIndex = 0;
        }
    }

    public bool IsMoving()
    {
        return !isDone;
    }


    // Initiates a jump to the specified target position
    private void StartJump(Vector3 target)
    {
        isDoneMovingToPoint = false;
        isMoving = true;
        targetJump = DisgustingFix(target);
        current_jump_point = objectTransform.position;
        direction = (targetJump - objectTransform.position).normalized;
        currentTime = 0.0f;
    }


    // Moves the piece along a the animation curve to the target position, and handles rotation during the jump
    private void movePiece(Vector3 target, float jumpTime)
    {
        Vector3 start = current_jump_point;
        Vector3 end = target;
        // Update the current time
        currentTime += Time.deltaTime;
        float time = Mathf.Clamp01(currentTime / jumpTime);
        //-------------MOVEMENT CALCULATIONS----------------//
        // Calculate the new position using the animation curves
        Vector3 position = Vector3.Lerp(start, end, ptLerp.Evaluate(time));
        position.y += stepHeight * yLerp.Evaluate(time);
        // Apply the new position and rotation
        objectTransform.position = position;
        //-------------ROTATION CALCULATIONS----------------//
        // Tilt forward during jump
        Vector3 tiltEuler = new Vector3(
            maxRotation * yLerp.Evaluate(time),
            0,
            0
        );
        //Look towards movement direction
        Quaternion lookRotation = Quaternion.LookRotation(direction);
        //convert tilt euler to quaternion (please never ask me how this works, i dont really know quaternions)
        Quaternion tiltRotation = Quaternion.Euler(tiltEuler);
        //combine the two rotations
        Quaternion finalRotation = lookRotation * tiltRotation;
        //apply the rotation smoothly
        objectTransform.rotation = Quaternion.Slerp(objectTransform.rotation, finalRotation, Time.deltaTime * 20f);
        //--------------------------------------------------//
        // If the jump is complete
        if (time >= 1.0f)
        {
            //snap to final position
            objectTransform.position = end;
            current_jump_point = end;
            isMoving = false;
            isDoneMovingToPoint = true;
        }
    }

    public int setLookAt(Vector3 target)
    {
        if (target == null)
        {
            //Debug.Log("failed to set look at point, target is null");
            return -1;
        }
        else
        {
            lookAtTarget = target;
            //Debug.Log("successfully set look at point");
            return 0;
        }
    }

    // Rotates the piece to face the specified target position
    public void lookAt()
    {
        Vector3 lookDirection = lookAtTarget - objectTransform.position;
        lookDirection.y = 0; // Keep only horizontal direction
        float turnTime = 3.0f; // Duration of the turn
        currentTimeRotation += Time.deltaTime;
        float time = Mathf.Clamp01(currentTimeRotation / turnTime);
        // Only rotate if not moving
        if (lookDirection != Vector3.zero && isDone)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            objectTransform.rotation = Quaternion.Slerp(objectTransform.rotation, targetRotation, time); // Smooth rotation
            //objectTransform.LookAt(DisgustingFix(lookAtTarget));
        } else
        {
            currentTimeRotation = 0.0f; // Reset rotation time if moving
        }
    }

    public IEnumerator update()
    {
        move(0.5f);
        lookAt();
        yield return null;
    }

    public void stop()
    {
        running = false;
    }
    
    public void start()
    {
        running = true;
    }


    //This belongs in the dumpster, fix and delete ASAP
    private Vector3 DisgustingFix(Vector3 targetJumpPoint)
    {
        return new Vector3(targetJumpPoint.x + 0.5f, objectTransform.position.y, targetJumpPoint.z + 0.5f);
    }


}
