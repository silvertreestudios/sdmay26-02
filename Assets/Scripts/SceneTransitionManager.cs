using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class SceneTransitionManager : MonoBehaviour
{
    private static SceneTransitionManager _instance;

    [SerializeField] private PanelSettings panelSettings;

    private UIDocument _doc;
    private VisualElement _overlay;

    private void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        BuildOverlay();
    }

    private void BuildOverlay()
    {
        if (panelSettings == null)
        {
            // Fallback: borrow PanelSettings from any UIDocument in the scene
            var existing = FindFirstObjectByType<UIDocument>();
            if (existing != null) panelSettings = existing.panelSettings;
        }

        _doc = gameObject.AddComponent<UIDocument>();
        _doc.panelSettings = panelSettings;
        _doc.sortingOrder = 100;

        _overlay = new VisualElement();
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = 0;
        _overlay.style.top = 0;
        _overlay.style.right = 0;
        _overlay.style.bottom = 0;
        _overlay.style.backgroundColor = new StyleColor(Color.black);
        _overlay.style.opacity = 0f;
        _overlay.pickingMode = PickingMode.Ignore;

        _doc.rootVisualElement.Add(_overlay);
    }

    public static void FadeAndLoad(string sceneName, float duration = 1f)
    {
        EnsureInstance();
        _instance.StartCoroutine(_instance.FadeRoutine(sceneName, null, duration));
    }

    public static void FadeAndLoad(int buildIndex, float duration = 1f)
    {
        EnsureInstance();
        _instance.StartCoroutine(_instance.FadeRoutine(null, buildIndex, duration));
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        var go = new GameObject("SceneTransitionManager");
        go.AddComponent<SceneTransitionManager>();
    }

    private IEnumerator FadeRoutine(string sceneName, int? buildIndex, float duration)
    {
        // Fade to black
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _overlay.style.opacity = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        _overlay.style.opacity = 1f;

        // Start async load, hold activation until fade completes
        AsyncOperation op = sceneName != null
            ? SceneManager.LoadSceneAsync(sceneName)
            : SceneManager.LoadSceneAsync(buildIndex.Value);

        op.allowSceneActivation = false;
        while (op.progress < 0.9f)
            yield return null;
        op.allowSceneActivation = true;

        yield return null; // let new scene settle

        // Fade from black
        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _overlay.style.opacity = 1f - Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        _overlay.style.opacity = 0f;
    }
}
