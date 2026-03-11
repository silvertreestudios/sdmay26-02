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
        
        // Capture snapshot
        Texture2D snapshot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        snapshot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        snapshot.Apply();
        RenderTexture.active = null;
        return snapshot;
    }

    // void Awake() {
    //     // Create individual RenderTexture for this portrait
    //     individualRenderTexture = new RenderTexture(512, 512, 24);
    //     portraitCamera = GetComponent<Camera>();
    //     if (portraitCamera != null) {
    //         portraitCamera.targetTexture = individualRenderTexture;
    //     }
    // }

    void Start() {
        // Render initial frame
        if (portraitCamera != null) {
            portraitCamera.Render();
        }
    }

    void OnDestroy() {
        if (individualRenderTexture != null) {
            individualRenderTexture.Release();
            Destroy(individualRenderTexture);
        }
    }

    public void SetPortraitCameraEnabled(bool enabled) {
        Camera portraitCamera = GetComponent<Camera>();
        if (portraitCamera != null) {
            portraitCamera.enabled = enabled;
        }
    }
}
