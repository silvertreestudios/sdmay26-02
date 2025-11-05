using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class CameraManager : SingletonMonoBehaviour<CameraManager>
{

    private List<(string, GameObject)> entities = new List<(string, GameObject)>();
    Camera camera = null;

    public void DebugLogCameraManager()
    {
        Debug.Log("CameraManager is active.");
    }

    public void setCamera(Camera cam)
    {
        camera = cam;
    }

    public void addEntity(string name, GameObject entity)
    {
        entities.Add((name, entity));
    }

    public void focusCamera(string name)
    {
        GameObject entity = entities.Find(e => e.Item1 == name).Item2;
        if (entity != null)
        {
            // Logic to focus camera on entity
            Debug.Log($"Focusing camera on {name}");

            Vector3 targetPosition = new Vector3(entity.transform.position.x + 5, entity.transform.position.y + 5, entity.transform.position.z - 5);
            camera.transform.position = Vector3.Lerp(camera.transform.position, targetPosition, 0.1f);
            camera.transform.LookAt(new Vector3(entity.transform.position.x, 0, entity.transform.position.z));
        }
        else
        {
            Debug.LogWarning($"Entity with name {name} not found.");
        }
    }

    public void movementCamera(string name, Vector3 destination)
    {
        GameObject entity = entities.Find(e => e.Item1 == name).Item2;

        if (entity != null)
        {
            // Logic to move camera towards destination
            Debug.Log($"Moving camera towards {name}");

            camera.transform.position = Vector3.Lerp(camera.transform.position, destination, 0.1f);
            camera.transform.LookAt(new Vector3(entity.transform.position.x, 0, entity.transform.position.z));
        }
        else
        {
            Debug.LogWarning($"Entity with name {name} not found.");
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