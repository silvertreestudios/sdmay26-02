using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple wrapper for mouse input using the new Input System.
/// </summary>
public static class InputCompat
{
    // Reference to the current mouse device
    private static Mouse MouseDevice => Mouse.current;

    /// <summary>Gets the mouse position in screen pixels.</summary>
    public static Vector3 MousePositionScreen()
        => MouseDevice != null ? (Vector3)MouseDevice.position.ReadValue() : Vector3.zero;

    /// <summary>Returns true if the left mouse button was pressed this frame.</summary>
    public static bool LeftClickDown()
        => MouseDevice != null && MouseDevice.leftButton.wasPressedThisFrame;

    /// <summary>Returns true if the right mouse button was pressed this frame.</summary>
    public static bool RightClickDown()
        => MouseDevice != null && MouseDevice.rightButton.wasPressedThisFrame;

}