using UnityEngine;

public class Portrait : MonoBehaviour
{
    private RenderTexture individualRenderTexture;
    private Camera portraitCamera;
    private Texture2D texture;
  
    public RenderTexture GetRenderTexture() {
        return individualRenderTexture;
    }

    public Texture2D GetPortraitSnapshot() {
        RenderTexture rt = GetRenderTexture();
        if (rt == null) return null;
        
        // Enable camera, render, then disable
        if (portraitCamera != null) {
            portraitCamera.enabled = true;
            portraitCamera.Render();
            portraitCamera.enabled = false;
        }
        
        // Capture snapshot at correct dimensions
        Texture2D snapshot = new Texture2D(152, 114, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        snapshot.ReadPixels(new Rect(0, 0, 152, 114), 0, 0);
        snapshot.Apply();
        RenderTexture.active = null;
        return snapshot;
    }

    void Awake() {
        Debug.Log("Portrait Awake: Initializing RenderTexture and Camera");
        // Create individual RenderTexture with card dimensions
        individualRenderTexture = new RenderTexture(152, 114, 24);
        portraitCamera = GetComponentInChildren<Camera>();
        if (portraitCamera != null) {
            Debug.Log("Found Camera");
            portraitCamera.targetTexture = individualRenderTexture;
            // Adjust camera aspect ratio to match texture
            portraitCamera.aspect = 152f / 114f; // 1.6:1
        } else {
            Debug.LogError("No Camera found in children of Portrait!");
        }
    }

    void Start() {
        // Render initial frame
        if (portraitCamera != null) {
            Debug.Log("Portrait Start: Rendering initial frame");
            GetPortraitSnapshot(); // Render initial frame to populate texture
        } else {
            Debug.Log("START FAILURE");
        }
    }

    void OnDestroy() {
        if (individualRenderTexture != null) {
            individualRenderTexture.Release();
            Destroy(individualRenderTexture);
        }
    }
}
