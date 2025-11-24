// using UnityEngine;

// public class CameraTransition
// {
//     // Reference to the camera transform
//     private Transform cameraTransform;
//     // starting position
//     private Vector3 startPosition;
//     // quaternion for rotation
//     private Quaternion startRotation;
//     private float elapsedTime = 0f;


//     public bool IsComplete { get; private set; }

//     // Constructor to initialize the camera transition
//     public CameraTransition(Transform camera)
//     {
//         cameraTransform = camera;
//         startPosition = camera.position;
//         startRotation = camera.rotation;
//         IsComplete = false;
//     }

//     // Update method to be called every frame
//     public void Update(Vector3 targetPosition, Quaternion targetRotation, AnimationCurve curve, float duration)
//     {
//         elapsedTime += Time.deltaTime;
//         float t = Mathf.Clamp01(elapsedTime / duration);
//         float curveValue = curve.Evaluate(t);

//         // Interpolate position and rotation based on the animation curve
//         cameraTransform.position = Vector3.Lerp(startPosition, targetPosition, curveValue);
//         cameraTransform.rotation = Quaternion.Lerp(startRotation, targetRotation, curveValue);

//         if (t >= 1f)
//         {
//             IsComplete = true;
//         }
//     }

//     // Reset the transition to start over
//     public void Reset()
//     {
//         startPosition = cameraTransform.position;
//         startRotation = cameraTransform.rotation;
//         elapsedTime = 0f;
//         IsComplete = false;
//     }
// }