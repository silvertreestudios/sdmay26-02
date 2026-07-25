using System.Collections;
using Game.DungeonPersistence;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Owns the persistent fade overlay and the one-shot dungeon launch request consumed by the
/// procedural gameplay scene.
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    private static SceneTransitionManager _instance;

    [SerializeField]
    private PanelSettings panelSettings;

    private UIDocument _doc;
    private VisualElement _overlay;
    private DungeonRunLaunchRequest pendingDungeonRun = DungeonRunLaunchRequest.None;
    private bool isTransitioning;

    internal static bool IsTransitioning => _instance != null && _instance.isTransitioning;

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
            if (existing != null)
                panelSettings = existing.panelSettings;
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

    /// <summary>Fades to and loads the named scene unless another transition is in progress.</summary>
    /// <param name="sceneName">The build-settings scene name to load.</param>
    /// <param name="duration">The duration of each fade, in unscaled seconds.</param>
    /// <returns><see langword="true"/> when accepted; otherwise, <see langword="false"/>.</returns>
    public static bool FadeAndLoad(string sceneName, float duration = 1f)
    {
        EnsureInstance();
        return _instance.TryBeginTransition(
            sceneName,
            null,
            duration,
            DungeonRunLaunchRequest.None
        );
    }

    /// <summary>Fades to and loads the indexed scene unless another transition is in progress.</summary>
    /// <param name="buildIndex">The build-settings index to load.</param>
    /// <param name="duration">The duration of each fade, in unscaled seconds.</param>
    /// <returns><see langword="true"/> when accepted; otherwise, <see langword="false"/>.</returns>
    public static bool FadeAndLoad(int buildIndex, float duration = 1f)
    {
        EnsureInstance();
        return _instance.TryBeginTransition(
            null,
            buildIndex,
            duration,
            DungeonRunLaunchRequest.None
        );
    }

    internal static bool FadeAndLoadDungeon(DungeonRunLaunchRequest request, float duration = 1f)
    {
        if (request == null || !request.IsPending)
            throw new System.ArgumentException(
                "A pending dungeon launch request is required.",
                nameof(request)
            );

        EnsureInstance();
        return _instance.TryBeginTransition("ProceduralDungeon", null, duration, request);
    }

    internal static bool TryConsumeDungeonRunLaunch(out DungeonRunLaunchRequest request)
    {
        if (_instance == null)
        {
            request = DungeonRunLaunchRequest.None;
            return false;
        }

        request = _instance.pendingDungeonRun;
        _instance.pendingDungeonRun = DungeonRunLaunchRequest.None;
        return request.IsPending;
    }

    private static void EnsureInstance()
    {
        if (_instance != null)
            return;
        var go = new GameObject("SceneTransitionManager");
        go.AddComponent<SceneTransitionManager>();
    }

    private bool TryBeginTransition(
        string sceneName,
        int? buildIndex,
        float duration,
        DungeonRunLaunchRequest request
    )
    {
        if (isTransitioning)
        {
            string target = sceneName ?? $"build index {buildIndex.Value}";
            Debug.LogWarning(
                $"Scene transition to '{target}' was rejected because another scene transition is already in progress."
            );
            return false;
        }

        isTransitioning = true;
        pendingDungeonRun = request;
        _overlay.pickingMode = PickingMode.Position;
        StartCoroutine(FadeRoutine(sceneName, buildIndex, duration));
        return true;
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
        string target = sceneName ?? $"build index {buildIndex.Value}";
        AsyncOperation op;
        try
        {
            op =
                sceneName != null
                    ? SceneManager.LoadSceneAsync(sceneName)
                    : SceneManager.LoadSceneAsync(buildIndex.Value);
        }
        catch (System.ArgumentException exception)
        {
            RecoverFromLoadFailure(target, exception.Message);
            yield break;
        }

        // Unity's API contract returns an operation or throws, but keep the framework boundary
        // defensive so an unavailable scene can never leave the persistent overlay blocking input.
        if (op == null)
        {
            RecoverFromLoadFailure(target, "Unity did not return a scene load operation.");
            yield break;
        }

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
        _overlay.pickingMode = PickingMode.Ignore;
        isTransitioning = false;
    }

    private void RecoverFromLoadFailure(string target, string reason)
    {
        Debug.LogError($"Scene transition to '{target}' failed to start: {reason}");
        pendingDungeonRun = DungeonRunLaunchRequest.None;
        _overlay.style.opacity = 0f;
        _overlay.pickingMode = PickingMode.Ignore;
        isTransitioning = false;
    }
}
