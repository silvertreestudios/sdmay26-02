using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TODO
/// - Allow to take in Vector3Int list as path points
/// - Allow to have linear interpolation between points with new data type
/// - Implement a lookAt()
///
/// </summary>
public class Movement : MonoBehaviour
{
    // Jump points for the piece to move between
    [Header("Path Points")]
    public GameObject path_point_1;
    public GameObject path_point_2;
    public GameObject path_point_3;
    public GameObject path_point_4;
    public GameObject path_point_5;

    // List to hold the path points
    List<Vector3Int> path_points = new List<Vector3Int>();
    List<Vector3Int> path_buffer = new List<Vector3Int>();

    // Public variables
    [Header("General")]
    public GameObject targetCamera;
    public Vector3Int enemy;

    [Header("Jump Parameters")]
    public float stepHeight;
    public float maxRotation;
    public AnimationCurve ptLerp;
    public AnimationCurve yLerp;

    // Private variables
    private Vector3 current_jump_point;
    private bool isJumping = false;
    private Vector3 targetJump;
    private float currentTime;
    private Vector3 direction;
    private int currentPathIndex = 0;

    void Start()
    {
        current_jump_point = transform.position;
        currentTime = 0.0f;

        // Initialize the path points based on the assigned GameObjects
        path_buffer.Add(Vector3Int.RoundToInt(path_point_1.transform.position));
        path_buffer.Add(Vector3Int.RoundToInt(path_point_2.transform.position));
        path_buffer.Add(Vector3Int.RoundToInt(path_point_3.transform.position));
        path_buffer.Add(Vector3Int.RoundToInt(path_point_4.transform.position));
        path_buffer.Add(Vector3Int.RoundToInt(path_point_5.transform.position));

        setPathPoints(path_buffer);
    }

    void Update()
    {
        // Keep the camera focused on the piece, ignoring y-axis
        targetCamera.transform.LookAt(new Vector3(transform.position.x, 0, transform.position.z));

        // On space key press, set the path points for the piece to follow
        //if (Input.GetKeyDown(KeyCode.Space)) { }

        // Move the piece along the path buffer, with a interval of 0.5 seconds per jump
        moveAlongPath(0.5f);
        lookAt(enemy);
    }

    // Sets the path points for the piece to follow, returns 0 on success, -1 on failure
    int setPathPoints(List<Vector3Int> points)
    {
        if (path_points.Count > 0)
        {
            Debug.Log("failed to set path points, path_points length greater than 0");
            return -1;
        }
        else
        {
            path_points = new List<Vector3Int>(points); // Create a new copy instead of reference
            Debug.Log("successfully set path points");
            return 0;
        }
    }

    // Moves the piece along the given list of target positions, with each jump taking the specified time
    void moveAlongPath(float time)
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
            Debug.Log("Reached end of path points, clearing path points");
            path_points.Clear();
            currentPathIndex = 0;
        }
    }

    // Initiates a jump to the specified target position
    private void StartJump(Vector3 target)
    {
        targetJump = target;
        current_jump_point = transform.position;
        direction = targetJump - transform.position;
        direction = direction.normalized;
        currentTime = 0.0f;
        isJumping = true;
    }

    // Moves the piece along a the animation curve to the target position
    private void movePieceSin(Vector3 target, float jumpTime)
    {
        Vector3 start = current_jump_point;
        Vector3 end = target;

        // Update the current time
        currentTime += Time.deltaTime;
        float time = Mathf.Clamp01(currentTime / jumpTime);

        // Calculate the new position using the animation curves
        Vector3 position = Vector3.Lerp(start, end, ptLerp.Evaluate(time));
        position.y += stepHeight * yLerp.Evaluate(time);

        // Apply the new position and rotation
        transform.position = position;
        transform.rotation = Quaternion.Euler(
            maxRotation * yLerp.Evaluate(time) * direction.z,
            0.0f,
            maxRotation * yLerp.Evaluate(time) * -direction.x
        );

        // If the jump is complete
        if (time >= 1.0f)
        {
            //snap to final position
            transform.position = end;
            current_jump_point = end;
            isJumping = false;
            // Move to next point in path
            currentPathIndex++;
        }
    }

    public void lookAt(Vector3Int target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0; // Keep only horizontal direction
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * 5f
            ); // Smooth rotation
        }
    }
}
