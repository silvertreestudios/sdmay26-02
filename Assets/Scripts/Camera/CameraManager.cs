using System;
using System.Collections.Generic;
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
    public void ResetClock()
    {
        currentTime = 0f;
    }


    // Sets the current actor for the camera to focus on
    public void setCurrentActor(string name)
    {
        currentActor = name;
    }


    // Removes an actor from the manager by name
    public void removeActor(string name)
    {
        entities.RemoveAll(e => e.Item1 == name);
    }


    // Sets the camera mode
    public void setMode(CameraType mode)
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
            float jumpTime = focusCameraSpeed; // Duration of the jump
            currentTime += Time.deltaTime;
            float time = Mathf.Clamp01(currentTime / jumpTime);
            // Debug.Log("Focus Camera time: " + time);
            // Debug.Log("Lerp Value: " + focusLerp.Evaluate(time));

            Vector3 targetPosition = new Vector3(startPosition.position.x, FocusZoom, startPosition.position.z);
            camera.transform.position = Vector3.Lerp(startPosition.position, targetPosition, focusLerp.Evaluate(time));

            Quaternion lookRotation = Quaternion.LookRotation(new Vector3(entity.transform.position.x, 0, entity.transform.position.z) - camera.transform.position);
            lookRotation = MouseOffset(lookRotation);
            camera.transform.rotation = Quaternion.Lerp(startPosition.rotation, lookRotation, focusLerp.Evaluate(time));
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
            float jumpTime = targetCameraSpeed; // Duration of the jump
            currentTime += Time.deltaTime;
            float time = Mathf.Clamp01(currentTime / jumpTime);
            // Debug.Log("Target Camera time: " + time);
            // Debug.Log("Lerp Value: " + focusLerp.Evaluate(time));

            Vector3 camTargetDirection = camTargetTransform.position - entity.transform.position;
            camTargetDirection.Normalize();
            Debug.Log("Cam Target Direction: " + camTargetDirection);
            camTargetDirection = Quaternion.Euler(0, fromActorAngle, 0) * camTargetDirection;
            Debug.Log("Cam Target Direction (Adjusted): " + camTargetDirection);
            camTargetDirection *= offsetDistance; // Distance from target
            camTargetDirection.y = TargetZoom;
            camTargetDirection += entity.transform.position;

            Vector3 targetPosition = new Vector3(entity.transform.position.x, TargetZoom, (entity.transform.position.z));
            camera.transform.position = Vector3.Lerp(camera.transform.position, camTargetDirection, targetLerp.Evaluate(time));

            Quaternion lookRotation = Quaternion.LookRotation(new Vector3(camTargetTransform.position.x, 0, camTargetTransform.position.z) - camera.transform.position);
            lookRotation = MouseOffset(lookRotation);
            camera.transform.rotation = Quaternion.Lerp(startPosition.rotation, lookRotation, targetLerp.Evaluate(time));
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
            float jumpTime = focusCameraSpeed; // Duration of the jump
            currentTime += Time.deltaTime;
            float time = Mathf.Clamp01(currentTime / jumpTime);
            // Debug.Log("Pick Camera time: " + time);
            // Debug.Log("Lerp Value: " + pickLerp.Evaluate(time));
            float xDirection = Mathf.Sign(camera.transform.position.x - entity.transform.position.x);
            float zDirection = Mathf.Sign(camera.transform.position.z - entity.transform.position.z);

            Vector3 targetPosition = new Vector3(entity.transform.position.x + (xDirection * 3f), PickZoom, entity.transform.position.z + (zDirection * 3f));

            Debug.Log($"X Direction: {xDirection}");
            Debug.Log($"Z Direction: {zDirection}");
            camera.transform.position = Vector3.Lerp(startPosition.position, targetPosition, pickLerp.Evaluate(time));

            Quaternion targetLookRotation = Quaternion.LookRotation(new Vector3(entity.transform.position.x, 0, entity.transform.position.z) - camera.transform.position);
            Debug.Log("Target Look Rotation: " + targetLookRotation.eulerAngles);
            Quaternion lookRotation = MouseOffset(targetLookRotation);
            Debug.Log("Adjusted Look Rotation: " + lookRotation.eulerAngles);
            camera.transform.rotation = Quaternion.Lerp(startPosition.rotation, lookRotation, pickLerp.Evaluate(time));
        }
        else
        {
            Debug.LogWarning($"Entity with name {name} not found.");
        }
    }


    // Adjusts the camera rotation based on mouse position
    private Quaternion MouseOffset(Quaternion baseRotation)
    {

        // Calculate rotation towards mouse position
        mouseScaleFactor = mouseScaleFactor;
        Vector3 mouseScreenPosition = Input.mousePosition;
        mouseScreenPosition.x = (mouseScreenPosition.x / Screen.width) - 0.5f; // Normalize to -0.5 to 0.5
        mouseScreenPosition.y = (mouseScreenPosition.y / Screen.height) - 0.5f; // Normalize to -0.5 to 0.5
        //Debug.Log("Mouse Normalized Position: " + mouseScreenPosition);
        Quaternion lookRotation = baseRotation;
        //Debug.Log("Base Look Rotation: " + lookRotation.eulerAngles);
        lookRotation *= Quaternion.Euler(mouseScreenPosition.y * mouseScaleFactor * -1, mouseScreenPosition.x * mouseScaleFactor, 0);
        //Debug.Log("Adjusted Look Rotation: " + lookRotation.eulerAngles);
        return lookRotation;
    }


    // Updates the camera based on the current mode
    public void update()
    {
        switch (currentCameraMode)
        {
            case CameraType.Movement:
                // Update camera in movement mode
                break;
            case CameraType.Focus:
                focusCamera(currentActor);
                break;
            case CameraType.Target:
                targetCamera(currentActor);
                break;
            case CameraType.Pick:
                PickCamera(currentActor);
                // Update camera in pick mode
                break;
            case CameraType.Party:
                // Update camera in party mode
                break;
            case CameraType.Orbit:
                // Update camera in orbit mode
                break;
            default:
                Debug.LogWarning("Unknown camera mode.");
                break;
        }
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