using System;
using System.Collections;
using System.Collections.Generic;
//using System.Numerics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

// TODO: add a way to get all tokens poistions in the scene for camera to focus on them CombatManager->Action Contoller -> prefab token
// TODO:
public class CameraManager : SingletonMonoBehaviour<CameraManager>
{
    public float cameraMoveSpeed = 5f;
    public float cameraZoomSpeed = 5f;
    public float cameraRotationSpeed = 20f;
    public float maxCameraYLimit = 7f;
    public float minCamearYLimit = 3f;
    public float cameraMovementAcceleration = 10f;
    public float cameraMovementDeceleration = 15f;
    public bool invertZoom = false;
    Camera mainCamera;
    private InputSystem_Actions inputActions;
    private InputAction moveAction;
    private InputAction zoomAction;
    private InputAction rotateAction;
    private InputAction interactAction;
    private Vector2 moveInput = Vector2.zero;
    private Vector2 zoomInput = Vector2.zero;
    private Vector2 rotateInput = Vector2.zero;
    private Vector2 currentVelocity = Vector2.zero;
    private float currentRotationVelocity = 0f; // Add this
    
    private CombatManager combatManager;
    private GameObject followTarget;
    public bool IsFollowing => followTarget != null;

    void Awake()
    {
        base.Awake();
        moveAction = InputSystem.actions.FindAction("MoveCamera");
        zoomAction = InputSystem.actions.FindAction("ZoomCamera");
        rotateAction = InputSystem.actions.FindAction("RotateCamera");
        //This is purely placeholder 
        interactAction = InputSystem.actions.FindAction("Sprint");
    }

    void Start()
    {
        getCamera();
        combatManager = CombatManagerInterface.GetInstance() as CombatManager;
    }

    void Update()
    {
        HandleCameraMovement();
        HandleCameraZoom();
        HandleCameraRotation();
        if (interactAction.IsPressed())
        {
            FocusOnCombatants();
        }
    }


    private void getCamera()
    {
        if(Camera.main != null)
        {
            mainCamera = Camera.main;
        }
        else
        {
            Debug.Log("No camera found in the scene, creating one.");
            GameObject cameraObject = new GameObject("Main Camera");
            mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
        }
    }

    private void HandleCameraMovement()
    {
        moveInput = moveAction.ReadValue<Vector2>();
        if (moveInput.magnitude > 0.01f && IsFollowing)
            StopFollowing();
        Vector3 forward = mainCamera.transform.forward;
        Vector3 right = mainCamera.transform.right;
        forward.y = 0;
        right.y = 0;
        forward.Normalize();
        right.Normalize();
        
        float accelerationRate = moveInput.magnitude > 0.01f ? cameraMovementAcceleration : cameraMovementDeceleration;
        
        currentVelocity = Vector2.Lerp(currentVelocity, moveInput, accelerationRate * Time.deltaTime);

        // Scale speed based on camera height
        float zoomScale = Mathf.InverseLerp(minCamearYLimit, maxCameraYLimit, mainCamera.transform.position.y);
        
        Vector3 moveDirection = right * currentVelocity.x + forward * currentVelocity.y;
        mainCamera.transform.Translate(moveDirection * cameraMoveSpeed * Time.deltaTime * (0.5f + zoomScale), Space.World);
    }

    private void HandleCameraZoom()
    {
        zoomInput = zoomAction.ReadValue<Vector2>();
        if (invertZoom)
        {
            zoomInput.y = -zoomInput.y;
        }
        float zoomAmount = zoomInput.y * cameraZoomSpeed * Time.deltaTime;
        Vector3 newPosition = mainCamera.transform.position + mainCamera.transform.forward * zoomAmount;

        if (newPosition.y < minCamearYLimit || newPosition.y > maxCameraYLimit)
        {
            return;
        }
        mainCamera.transform.position = newPosition;
    }

    private void HandleCameraRotation()
    {
        rotateInput = rotateAction.ReadValue<Vector2>();

        float accelerationRate = rotateInput.magnitude > 0.01f ? cameraMovementAcceleration : cameraMovementDeceleration;
        
        currentRotationVelocity = Mathf.Lerp(currentRotationVelocity, rotateInput.x, accelerationRate * Time.deltaTime);

        // Scale rotation based on camera height
        float zoomScale = Mathf.InverseLerp(minCamearYLimit, maxCameraYLimit, mainCamera.transform.position.y);
        
        float rotationAmount = currentRotationVelocity * cameraRotationSpeed * Time.deltaTime * (0.5f + zoomScale);
        Vector3 orbitCenter = followTarget != null ? followTarget.transform.position : GetCameraLookAtPosition();
        mainCamera.transform.RotateAround(orbitCenter, Vector3.up, rotationAmount);
    }


    //This is used for the center point of what the camera rotates on
    private Vector3 GetCameraLookAtPosition()
    {
        Vector3 returnPosition = new Vector3(mainCamera.transform.position.x, 0, mainCamera.transform.position.z);
        Plane plane = new Plane(Vector3.up, Vector3.zero);      
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            returnPosition = hitPoint;
        }
        return returnPosition;
    }

    public Vector3 GetAveragePosition()
    {
        Vector3[] positions = combatManager.getPoistions();

        if (positions.Length == 0)
            return Vector3.zero;
        
        Vector3 sum = Vector3.zero;
        foreach (Vector3 pos in positions)
        {
            sum += pos;
        }
        return sum / positions.Length;
    }

    private Coroutine currentPanRoutine;

    public void PanToTarget(GameObject target, bool followIndefinitely = false)
    {
        if (target == null) return;
        if (currentPanRoutine != null) StopCoroutine(currentPanRoutine);
        currentPanRoutine = StartCoroutine(PanToTargetRoutine(target, followIndefinitely));
    }

    public void StopFollowing()
    {
        if (currentPanRoutine != null)
        {
            StopCoroutine(currentPanRoutine);
            currentPanRoutine = null;
        }
        followTarget = null;
    }

    private IEnumerator PanToTargetRoutine(GameObject target, bool followIndefinitely)
    {
        float panDuration = 0.5f;
        Vector3 offset = mainCamera.transform.position - GetCameraLookAtPosition();

        // Pan to target over 0.5 seconds, preserving camera Y
        float elapsed = 0f;
        while (elapsed < panDuration)
        {
            if (target == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / panDuration);
            Vector3 desiredPosition = target.transform.position + offset;
            desiredPosition.y = mainCamera.transform.position.y;
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, desiredPosition, t);
            yield return null;
        }

        if (followIndefinitely)
        {
            // Follow indefinitely - track target delta (X/Z only) so orbiting still works
            followTarget = target;
            Vector3 lastTargetPos = target.transform.position;
            while (true)
            {
                if (target == null) { followTarget = null; yield break; }
                Vector3 delta = target.transform.position - lastTargetPos;
                delta.y = 0;
                mainCamera.transform.position += delta;
                lastTargetPos = target.transform.position;
                yield return null;
            }
        }
        else
        {
            // Track target for 1 second then stop (X/Z only)
            elapsed = 0f;
            while (elapsed < 1f)
            {
                if (target == null) yield break;
                elapsed += Time.deltaTime;
                Vector3 desiredPosition = target.transform.position + offset;
                desiredPosition.y = mainCamera.transform.position.y;
                mainCamera.transform.position = desiredPosition;
                yield return null;
            }
        }
    }

    public void FocusOnCombatants()
    {
        Vector3 averagePosition = GetAveragePosition();
        Vector3 currentLookAt = GetCameraLookAtPosition();
        
        // Calculate the offset from current look-at to camera
        Vector3 offset = mainCamera.transform.position - currentLookAt;
        
        // Calculate desired zoom based on combatant spread
        float maxDistance = GetMaxDistanceFromAveragePoint(averagePosition);
        
        // Map distance to Y height (closer combatants = lower camera, spread out = higher camera)
        float targetY = Mathf.Lerp(minCamearYLimit, maxCameraYLimit, maxDistance/5f); // Adjust divisor to tune sensitivity
        targetY = Mathf.Clamp(targetY, minCamearYLimit, maxCameraYLimit);
        
        // Update Y component of offset
        offset.y = targetY - averagePosition.y;
        
        // Move camera to maintain offset from new target
        mainCamera.transform.position = averagePosition + offset;
    }

    private float GetMaxDistanceFromAveragePoint(Vector3 average)
    {
        Vector3[] positions = combatManager.getPoistions();
        float maxDist = 0f;
        
        foreach (Vector3 pos in positions)
        {
            float dist = Vector3.Distance(new Vector3(average.x, 0, average.z), new Vector3(pos.x, 0, pos.z));
            if (dist > maxDist)
                maxDist = dist;
        }
        
        return maxDist;
    }
}