using UnityEngine;

public class Portrait : MonoBehaviour
{
    private RenderTexture individualRenderTexture;
    private Camera portraitCamera;
    private Texture2D cachedSnapshot;

    public RenderTexture GetRenderTexture() {
        return individualRenderTexture;
    }

    public Texture2D GetPortraitSnapshot() {
        return cachedSnapshot;
    }

    private void CaptureSnapshot() {
        RenderTexture rt = individualRenderTexture;
        if (rt == null || portraitCamera == null) return;

        portraitCamera.enabled = true;
        portraitCamera.Render();
        portraitCamera.enabled = false;

        cachedSnapshot = new Texture2D(152, 114, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        cachedSnapshot.ReadPixels(new Rect(0, 0, 152, 114), 0, 0);
        cachedSnapshot.Apply();
        RenderTexture.active = null;
    }

    void Awake() {
        //Debug.Log("Portrait Awake: Initializing RenderTexture and Camera");
        // Create individual RenderTexture with card dimensions
        individualRenderTexture = new RenderTexture(152, 114, 24, RenderTextureFormat.ARGB32);
        portraitCamera = GetComponentInChildren<Camera>();
        if (portraitCamera != null) {
            portraitCamera.targetTexture = individualRenderTexture;
            portraitCamera.aspect = 152f / 114f;
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = Color.clear;
        } else {
            Debug.LogError("No Camera found in children of Portrait!");
        }
    }

    void Start() {
        CaptureSnapshot();
    }

    void OnDestroy() {
        if (individualRenderTexture != null) {
            individualRenderTexture.Release();
            Destroy(individualRenderTexture);
        }
    }
}
