using System;
using System.Collections.Generic;
//using System.Numerics;
using Unity.VisualScripting;
using UnityEngine;


public enum CameraType
{
    Movement,
    Focus,
    Target,
    Pick,
    Party,
    Orbit
}


public class CameraManager : SingletonMonoBehaviour<CameraManager>
{

    private List<(string, GameObject)> entities = new List<(string, GameObject)>();
    Camera camera = null;
    private CameraType currentCameraMode;
    private string currentActor;
    private float currentTime = 0.0f;
    private bool isTransitionDone = true;
    private Vector3 totalTransform = Vector3.zero;
    private Vector3 cameraOffset = new Vector3(0, 5, -5);
    public float orbitRadius = 5f; // Distance from target
    public float orbitSpeed = 90f; // Degrees per second
    private float currentOrbitAngle = 0f;
    private bool NeedsToMovetoTarget = false;
    private Vector3 targetPosition;

    [Header("General Settings")]
    public Transform cameraTarget;

    [Header("Mouse Settings")]
    public float mouseScaleFactor = 5f;



    [Header("Focus Camera Settings")]
    public AnimationCurve focusLerp;
    public float focusCameraSpeed = 10f;
    private Transform startPosition;
    [Range(5f, 15f)]
    public float FocusZoom = 10f;

    [Header("Target Camera Settings")]
    public AnimationCurve targetLerp;
    public float targetCameraSpeed = 10f;
    public float offsetDistance = 5f;
    public float fromActorAngle = 140f;
    public Transform camTargetTransform;
    [Range(1f, 15f)]
    public float TargetZoom = 10f;

    [Header("Pick Camera Settings")]
    public AnimationCurve pickLerp;
    public float pickCameraSpeed = 10f;
    //public Transform camPickTransform;
    [Range(5f, 15f)]
    public float PickZoom = 10f;


    // Debug function
    public void DebugLogCameraManager()
    {
        Debug.Log("CameraManager is active.");
    }


    // Sets the main camera for the manager to control
    public void setCamera(Camera cam)
    {
        camera = cam;
    }

    public void addActor(string name, GameObject Actor)
    {
        entities.Add((name, Actor));
    }


    // Resets the internal clock for camera transitions
    private void ResetClock()
    {
        currentTime = 0f;
    }

    public void SetCameraForCharacter(string characterName, CameraType mode)
    {
        if (isTransitionDone)
        {
            setCurrentActor(characterName);
            setMode(mode);
            ResetClock();
        }
        else
        {
            Debug.LogWarning("Camera transition is still in progress. Please wait.");
        }
    }

    public bool IsTransitionDone()
    {
        return isTransitionDone;
    }

    public float Timer(float overallTime)
    {
        float jumpTime = overallTime; // Duration of the jump
        currentTime += Time.deltaTime;
        float time = Mathf.Clamp01(currentTime / jumpTime);
        //isTransitionDone flag control 
        if (time >= 1f) { isTransitionDone = true; }
        else { isTransitionDone = false; }
        return time;
    }


    // Sets the current actor for the camera to focus on
    private void setCurrentActor(string name)
    {
        currentActor = name;
    }


    // Removes an actor from the manager by name
    public void removeActor(string name)
    {
        entities.RemoveAll(e => e.Item1 == name);
    }


    // Sets the camera mode
    private void setMode(CameraType mode)
    {
        switch (mode)
        {
            case CameraType.Movement:
                //currentCameraMode = CameraType.Movement;
                Debug.Log("Camera mode set to Movement.");
                // Set camera to movement mode
                break;
            case CameraType.Focus:
                currentCameraMode = CameraType.Focus;
                startPosition = camera.transform;
                Debug.Log("Camera mode set to Focus.");
                // Set camera to focus mode
                break;
            case CameraType.Target:
                currentCameraMode = CameraType.Target;
                startPosition = camera.transform;
                Debug.Log("Camera mode set to Target.");
                // Set camera to target mode
                break;
            case CameraType.Pick:
                currentCameraMode = CameraType.Pick;
                startPosition = camera.transform;
                Debug.Log("Camera mode set to Pick.");
                // Set camera to pick mode
                break;
            case CameraType.Party:
                currentCameraMode = CameraType.Party;
                Debug.Log("Camera mode set to Party.");
                // Set camera to party mode
                break;
            case CameraType.Orbit:
                currentCameraMode = CameraType.Orbit;
                Debug.Log("Camera mode set to Orbit.");
                // Set camera to orbit mode
                break;
            default:
                Debug.LogWarning("Unknown camera mode.");
                break;
        }
    }


    // Focuses the camera on a specific actor by name
    public void focusCamera(string name)
    {
        GameObject entity = entities.Find(e => e.Item1 == name).Item2;

        if (entity != null)
        {
            Vector3 targetPosition = new Vector3(startPosition.position.x, FocusZoom, startPosition.position.z);
            camera.transform.position = Vector3.Lerp(startPosition.position, targetPosition, focusLerp.Evaluate(Timer(focusCameraSpeed)));

            Quaternion lookRotation = Quaternion.LookRotation(new Vector3(entity.transform.position.x, 0, entity.transform.position.z) - camera.transform.position);
            lookRotation = MouseOffset(lookRotation);
            camera.transform.rotation = Quaternion.Lerp(startPosition.rotation, lookRotation, focusLerp.Evaluate(Timer(focusCameraSpeed)));
        }
        else
        {
            Debug.LogWarning($"Entity with name {name} not found.");
        }
    }


    // Targets the camera on a specific actor by name
    public void targetCamera(string name)
    {
        GameObject entity = entities.Find(e => e.Item1 == name).Item2;
        if (entity != null)
        {
            Vector3 camTargetDirection = camTargetTransform.position - entity.transform.position;
            camTargetDirection.Normalize();
            camTargetDirection = Quaternion.Euler(0, fromActorAngle, 0) * camTargetDirection;
            camTargetDirection *= offsetDistance; // Distance from target
            camTargetDirection.y = TargetZoom;
            camTargetDirection += entity.transform.position;

            Vector3 targetPosition = new Vector3(entity.transform.position.x, TargetZoom, (entity.transform.position.z));
            camera.transform.position = Vector3.Lerp(camera.transform.position, camTargetDirection, targetLerp.Evaluate(Timer(targetCameraSpeed)));

            Quaternion lookRotation = Quaternion.LookRotation(new Vector3(camTargetTransform.position.x, 0, camTargetTransform.position.z) - camera.transform.position);
            lookRotation = MouseOffset(lookRotation);
            camera.transform.rotation = Quaternion.Lerp(startPosition.rotation, lookRotation, targetLerp.Evaluate(Timer(targetCameraSpeed)));
        }
        else
        {
            Debug.LogWarning($"Entity with name {name} not found.");
        }
    }


    //Hovers the camera over a specific actor by name at offset specified by how it moved in relation to camera
    public void PickCamera(string name)
    {
        GameObject entity = entities.Find(e => e.Item1 == name).Item2;

        if (entity != null)
        {
            float xDirection = Mathf.Sign(camera.transform.position.x - entity.transform.position.x);
            float zDirection = Mathf.Sign(camera.transform.position.z - entity.transform.position.z);

            Vector3 targetPosition = new Vector3(entity.transform.position.x + (xDirection * 3f), PickZoom, entity.transform.position.z + (zDirection * 3f));

            camera.transform.position = Vector3.Lerp(startPosition.position, targetPosition, pickLerp.Evaluate(Timer(pickCameraSpeed)));

            Quaternion targetLookRotation = Quaternion.LookRotation(new Vector3(entity.transform.position.x, 0, entity.transform.position.z) - camera.transform.position);
            Quaternion lookRotation = MouseOffset(targetLookRotation);
            camera.transform.rotation = Quaternion.Lerp(startPosition.rotation, lookRotation, pickLerp.Evaluate(Timer(pickCameraSpeed)));
        }
        else
        {
            Debug.LogWarning($"Entity with name {name} not found.");
        }
    }


    // Adjusts the camera rotation based on mouse position
    private Quaternion MouseOffset(Quaternion baseRotation)
    {
        //mouseScaleFactor = mouseScaleFactor;
        Vector3 mouseScreenPosition = Input.mousePosition;
        mouseScreenPosition.x = (mouseScreenPosition.x / Screen.width) - 0.5f; // Normalize to -0.5 to 0.5
        mouseScreenPosition.y = (mouseScreenPosition.y / Screen.height) - 0.5f; // Normalize to -0.5 to 0.5
        Quaternion lookRotation = baseRotation;
        lookRotation *= Quaternion.Euler(mouseScreenPosition.y * mouseScaleFactor * -1, mouseScreenPosition.x * mouseScaleFactor, 0);
        return lookRotation;
    }


    // Updates the camera based on the current mode
    public void update()
    {
        // switch (currentCameraMode)
        // {
        //     case CameraType.Movement:
        //         // Update camera in movement mode
        //         break;
        //     case CameraType.Focus:
        //         focusCamera(currentActor);
        //         break;
        //     case CameraType.Target:
        //         targetCamera(currentActor);
        //         break;
        //     case CameraType.Pick:
        //         PickCamera(currentActor);
        //         // Update camera in pick mode
        //         break;
        //     case CameraType.Party:
        //         // Update camera in party mode
        //         break;
        //     case CameraType.Orbit:
        //         // Update camera in orbit mode
        //         break;
        //     default:
        //         Debug.LogWarning("Unknown camera mode.");
        //         break;
        // }


        if (NeedsToMovetoTarget)
        {
            totalTransform = Vector3.Lerp(totalTransform, targetPosition, Timer(1.0f));
            Debug.Log("Moving camera towards target position: " + targetPosition);
            if (IsTransitionDone())
            {
                NeedsToMovetoTarget = false;
                ResetClock();
                Debug.Log("Camera has reached the target position.");
            }
        }
        else
        {

            // Get camera's forward direction on horizontal plane (ignore Y angle)
            Vector3 cameraForward = camera.transform.forward;
            cameraForward.y = 0;
            cameraForward.Normalize();

            // Get camera's right direction on horizontal plane
            Vector3 cameraRight = camera.transform.right;
            cameraRight.y = 0;
            cameraRight.Normalize();

            if (Input.GetKey(KeyCode.W) && !Input.GetKey(KeyCode.LeftShift))
            {
                Debug.Log("W key pressed");
                totalTransform += cameraForward * 5.0f * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.A) && !Input.GetKey(KeyCode.LeftShift))
            {
                Debug.Log("A key pressed");
                totalTransform += -cameraRight * 5.0f * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.S) && !Input.GetKey(KeyCode.LeftShift))
            {
                Debug.Log("S key pressed");
                totalTransform += -cameraForward * 5.0f * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.D) && !Input.GetKey(KeyCode.LeftShift))
            {
                Debug.Log("D key pressed");
                totalTransform += cameraRight * 5.0f * Time.deltaTime;
            }


            if (Input.mouseScrollDelta.y > 0)
            {
                //camera.transform.position += camera.transform.forward * 5.0f * Time.deltaTime;
                // cameraOffset += camera.transform.forward * 5.0f * Time.deltaTime;
                cameraOffset += new Vector3(0, 5.0f * Time.deltaTime, 0);
                Debug.Log("Mouse Scroll Up");
            }
            if (Input.mouseScrollDelta.y < 0)
            {
                Debug.Log("Mouse Scroll Down");
                //camera.transform.position -= camera.transform.forward * 5.0f * Time.deltaTime;
                // cameraOffset -= camera.transform.forward * 5.0f * Time.deltaTime;
                cameraOffset += new Vector3(0, -5.0f * Time.deltaTime, 0);
            }

            if (Input.GetKey(KeyCode.UpArrow))
            {
                //camera.transform.position += camera.transform.forward * 5.0f * Time.deltaTime;
                // cameraOffset += camera.transform.forward * 5.0f * Time.deltaTime;
                cameraOffset += new Vector3(0, 5.0f * Time.deltaTime, 0);
                Debug.Log("Up Arrow key pressed");
            }
            if (Input.GetKey(KeyCode.DownArrow))
            {
                Debug.Log("Down Arrow key pressed");
                //camera.transform.position -= camera.transform.forward * 5.0f * Time.deltaTime;
                // cameraOffset -= camera.transform.forward * 5.0f * Time.deltaTime;
                cameraOffset += new Vector3(0, -5.0f * Time.deltaTime, 0);
            }
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.Q))
            {
                // Counter-clockwise rotation
                currentOrbitAngle += orbitSpeed * Time.deltaTime;
                UpdateCameraOrbit();
                Debug.Log("Left Arrow key pressed");
            }
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.E))
            {
                // Clockwise rotation
                currentOrbitAngle -= orbitSpeed * Time.deltaTime;
                UpdateCameraOrbit();
                Debug.Log("Right Arrow key pressed");
            }

            if (Input.GetKey(KeyCode.LeftShift))
            {
                Debug.Log("Left Shift key pressed");

                // Normalize mouse position to -0.5 to 0.5 range (center = 0,0)
                Vector2 normalizedMousePos;
                normalizedMousePos.x = (Input.mousePosition.x / Screen.width) - 0.5f;
                normalizedMousePos.y = (Input.mousePosition.y / Screen.height) - 0.5f;

                // Define deadzone threshold (0.2 = 20% from center)
                float deadzone = 0.2f;

                if (Mathf.Abs(normalizedMousePos.x) > deadzone)
                {
                    totalTransform += cameraRight * Mathf.Sign(normalizedMousePos.x) * 5.0f * Time.deltaTime;
                }
                if (Mathf.Abs(normalizedMousePos.y) > deadzone)
                {
                    totalTransform += cameraForward * Mathf.Sign(normalizedMousePos.y) * 5.0f * Time.deltaTime;
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("Space key pressed");
            }

            cameraTarget.transform.position = totalTransform;
            camera.transform.position = new Vector3(cameraTarget.transform.position.x + cameraOffset.x, cameraTarget.transform.position.y + cameraOffset.y, cameraTarget.transform.position.z + cameraOffset.z);
            camera.transform.LookAt(cameraTarget);
        }
    }

    private void UpdateCameraOrbit()
    {
        // Calculate current distance between camera and target
        Vector3 cameraPosition = new Vector3(
            cameraTarget.transform.position.x + cameraOffset.x,
            cameraTarget.transform.position.y + cameraOffset.y,
            cameraTarget.transform.position.z + cameraOffset.z
        );

        // Get horizontal distance (ignoring Y for circular orbit)
        Vector3 horizontalOffset = cameraPosition - cameraTarget.transform.position;
        float currentRadius = new Vector2(horizontalOffset.x, horizontalOffset.z).magnitude;

        // Calculate new offset based on angle and current radius
        float radians = currentOrbitAngle * Mathf.Deg2Rad;
        float x = Mathf.Sin(radians) * currentRadius;
        float z = -Mathf.Cos(radians) * currentRadius;

        // Keep the Y component from the current offset
        cameraOffset = new Vector3(x, cameraOffset.y, z);
    }

    public void setTarget(GameObject target)
    {
        Debug.Log("Setting camera target to: " + target.name);
        // NeedsToMovetoTarget = true;
        // targetPosition = target.transform.position;
    }

}



/// Notes
/// Camera Perspective Modes:
/// - Movement Mode: Tracks the player token from the position of the desired destination
/// - Pick Mode: Overhead view of the board for selecting tokens or tiles
/// - Focus Mode: subtle overhead downangle on a specific token or tile
/// - Party Mode: Overhead view of the party's area
/// - Orbit Mode: revolves around the party after a input delay 
/// 
/// 
/// Controls
/// - WASD: Move camera in the direction of the key pressed relative to the camera's current orientation
/// - Mouse Scroll: Zoom in and out
/// - Left and right arrow Keys: Rotate camera around the target
/// - Up and down arrow Keys: Adjust camera height
/// - Q and E: Rotate camera around the target
/// - Shift + Mouse Movement: Pan camera in the direction of mouse movement
/// 
/// 
/// Dictionary
/// name: string name of entity
/// actor: something you may want to focus the camera on
/// Clock: some sort of internal timer to manage lerping between positions
/// 
/// Method Descriptions
/// setCamera: assigns the main camera to be controlled
/// addActor: adds an entity to the camera manager's list
/// removeActor: removes an entity from the camera manager's list
/// setCurrentActor: sets the current actor for the set camera to focus on
/// setMode: sets the camera mode
/// focusCamera: focuses the camera on a specific actor
/// targetCamera: targets the camera on a specific target actor from an over-the-shoulder perspective of current actor
/// PickCamera: hovers the camera over a specific actor at an offset
/// MouseOffset: adjusts the camera rotation based on mouse position
/// update: updates the camera based on the current mode    