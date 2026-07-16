using UnityEngine;

public class Portrait : MonoBehaviour
{
    private const int PortraitWidth = 152;
    private const int PortraitHeight = 114;

    private RenderTexture individualRenderTexture;
    private Camera portraitCamera;
    private Texture2D cachedSnapshot;

    public RenderTexture GetRenderTexture() {
        return individualRenderTexture;
    }

    public Texture2D GetPortraitSnapshot() {
        return cachedSnapshot;
    }

    public void RefreshSnapshot() {
        RenderTexture rt = individualRenderTexture;
        if (rt == null || portraitCamera == null) return;

        bool wasEnabled = portraitCamera.enabled;
        RenderTexture previousActive = RenderTexture.active;
        try {
            portraitCamera.enabled = true;
            portraitCamera.Render();
            if (cachedSnapshot == null)
                cachedSnapshot = new Texture2D(PortraitWidth, PortraitHeight, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            cachedSnapshot.ReadPixels(new Rect(0, 0, PortraitWidth, PortraitHeight), 0, 0);
            cachedSnapshot.Apply();
        } finally {
            RenderTexture.active = previousActive;
            portraitCamera.enabled = wasEnabled;
        }
    }

    void Awake() {
        //Debug.Log("Portrait Awake: Initializing RenderTexture and Camera");
        // Create individual RenderTexture with card dimensions
        individualRenderTexture = new RenderTexture(PortraitWidth, PortraitHeight, 24, RenderTextureFormat.ARGB32);
        portraitCamera = GetComponentInChildren<Camera>();
        if (portraitCamera != null) {
            portraitCamera.targetTexture = individualRenderTexture;
            portraitCamera.aspect = (float)PortraitWidth / PortraitHeight;
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = Color.clear;
        } else {
            Debug.LogError("No Camera found in children of Portrait!");
        }
    }

    void Start() {
        RefreshSnapshot();
    }

    void OnDestroy() {
        if (individualRenderTexture != null) {
            if (portraitCamera != null && portraitCamera.targetTexture == individualRenderTexture)
                portraitCamera.targetTexture = null;
            individualRenderTexture.Release();
            Destroy(individualRenderTexture);
        }
        if (cachedSnapshot != null)
            Destroy(cachedSnapshot);
    }
}
