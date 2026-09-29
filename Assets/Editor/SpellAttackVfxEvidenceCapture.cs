#if UNITY_EDITOR
using System;
using System.IO;
using Game.Rules.Unity.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Captures inspected full Game View evidence for the production VFX gallery.</summary>
[InitializeOnLoad]
public static class SpellAttackVfxEvidenceCapture
{
    private const string ActiveKey = "SpellAttackVfxEvidenceCapture.Active";
    private static readonly (string Filename, string Entry, double Delay)[] Captures =
    {
        ("gallery", string.Empty, 0.2),
        ("melee", "strike/greataxe/critical", 1.05),
        ("ranged", "strike/shortbow/critical", 0.82),
        ("offensive-spell", "spell/divine-lance/2-action/critical", 1.68),
        ("heal-touch", "spell/heal/1-action/living", 1.05),
        ("heal-ranged-travel", "spell/heal/2-action/living", 1.1),
        ("heal-ranged-result", "spell/heal/2-action/living", 1.65),
        ("healing-area-wave", "spell/heal/3-action/living", 1.05),
        ("healing-area-mixed-results", "spell/heal/3-action/living", 1.85),
        ("heal-undead", "spell/heal/3-action/undead-critical-failure", 1.85),
        ("hymn-deafened", "spell/haunting-hymn/2-action/critical-failure", 1.05),
        ("infuse-three-target", "spell/infuse-vitality/3-action/create", 1.8),
        ("persistent", "auxiliary/rotting-aura/active", 0.45),
    };

    private static int index;
    private static double nextAction;
    private static bool waitingForFile;
    private static bool effectStarted;
    private static string pendingPath = string.Empty;

    static SpellAttackVfxEvidenceCapture()
    {
        if (SessionState.GetBool(ActiveKey, false))
            Register();
    }

    /// <summary>
    /// Opens the gallery and starts the deterministic evidence capture sequence after protecting
    /// modified scenes in interactive Editor sessions. Batchmode capture remains non-interactive.
    /// </summary>
    public static void Begin()
    {
        SpellAttackVfxEditorEntrySafety.TryRun(
            Application.isBatchMode,
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo,
            BeginCapture
        );
    }

    private static void BeginCapture()
    {
        SessionState.SetBool(ActiveKey, true);
        index = 0;
        Register();
        EditorSceneManager.OpenScene("Assets/Scenes/VfxGallery.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Register()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Screen.SetResolution(1920, 1080, false);
            index = 0;
            waitingForFile = false;
            effectStarted = false;
            nextAction = EditorApplication.timeSinceStartup + 1.5;
            EditorApplication.update -= UpdateCapture;
            EditorApplication.update += UpdateCapture;
        }
        else if (
            state == PlayModeStateChange.EnteredEditMode
            && SessionState.GetBool(ActiveKey, false)
        )
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.Exit(0);
        }
    }

    private static void UpdateCapture()
    {
        if (EditorApplication.timeSinceStartup < nextAction)
            return;
        if (waitingForFile)
        {
            if (!File.Exists(pendingPath))
            {
                nextAction = EditorApplication.timeSinceStartup + 0.25;
                return;
            }
            waitingForFile = false;
            effectStarted = false;
            index++;
            if (index >= Captures.Length)
            {
                EditorApplication.update -= UpdateCapture;
                EditorApplication.ExitPlaymode();
                return;
            }
        }

        VfxGalleryController gallery =
            UnityEngine.Object.FindFirstObjectByType<VfxGalleryController>();
        if (gallery == null)
            throw new InvalidOperationException("The VFX gallery controller is unavailable.");
        (string filename, string entry, double delay) = Captures[index];
        if (!string.IsNullOrEmpty(entry) && !effectStarted)
        {
            gallery.ResetGallery();
            gallery.Select(entry);
            gallery.BeginSelectedForTests();
            effectStarted = true;
            nextAction = EditorApplication.timeSinceStartup + delay;
            return;
        }
        if (string.IsNullOrEmpty(entry))
            gallery.ResetGallery();
        string root = Path.GetFullPath(
            ".agent-temp/delivery/feat-spell-attack-vfx-gallery/screenshots"
        );
        Directory.CreateDirectory(root);
        pendingPath = Path.Combine(root, filename + ".png");
        ScreenCapture.CaptureScreenshot(pendingPath);
        waitingForFile = true;
        nextAction = EditorApplication.timeSinceStartup + 0.75;
    }
}
#endif
