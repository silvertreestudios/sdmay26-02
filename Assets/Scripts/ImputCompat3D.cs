using UnityEngine;

/// <summary>
/// Simple wrapper for mouse input
/// </summary>
public static class InputCompat
{
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    // Current mouse device (new Input System)
    static UnityEngine.InputSystem.Mouse M => UnityEngine.InputSystem.Mouse.current;

    /// <summary>Mouse position in screen pixels.</summary>
    public static Vector3 MousePositionScreen()
        => (Vector3)(M != null ? M.position.ReadValue() : Vector2.zero);

    /// <summary>True if left mouse button was pressed this frame.</summary>
    public static bool LeftClickDown()
        => M != null && M.leftButton.wasPressedThisFrame;

#else
    /// <summary>Mouse position in screen pixels.</summary>
    public static Vector3 MousePositionScreen()
        => Input.mousePosition;

    /// <summary>True if left mouse button was pressed this frame.</summary>
    public static bool LeftClickDown()
        => Input.GetMouseButtonDown(0);
#endif
}