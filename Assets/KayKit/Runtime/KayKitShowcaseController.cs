using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.KayKit
{
    public sealed class KayKitShowcaseController : MonoBehaviour
    {
        private const string DefaultClipId = "animation/general/idle_a";
        private const float MinimumPlaybackSpeed = 0.25f;
        private const float MaximumPlaybackSpeed = 2.0f;
        private const float MinimumCameraDistance = 7.0f;
        private const float MaximumCameraDistance = 16.0f;

        [Header("Animation")]
        [SerializeField]
        private KayKitAnimationLibrary animationLibrary;

        [SerializeField]
        private GameObject adventurerRoot;

        [SerializeField]
        private Animator adventurerAnimator;

        [SerializeField]
        private GameObject skeletonRoot;

        [SerializeField]
        private Animator skeletonAnimator;

        [Header("Presentation")]
        [SerializeField]
        private GameObject environmentRoot;

        [SerializeField]
        private GameObject accessoryRoot;

        [SerializeField]
        private Camera showcaseCamera;

        [SerializeField]
        private Transform cameraFocus;

        [SerializeField]
        private float cameraHeight = 3.2f;

        [SerializeField]
        private float cameraDistance = 11.0f;

        private readonly List<KayKitAnimationEntry> playableEntries = new();
        private readonly List<KayKitAnimationEntry> visibleEntries = new();
        private readonly List<AnimationClipPlayable> clipPlayables = new();
        private PlayableGraph playableGraph;
        private string[] categories = Array.Empty<string>();
        private int selectedEntryIndex = -1;
        private int selectedCategoryIndex;
        private Vector2 clipScroll;
        private string searchText = string.Empty;
        private float playbackSpeed = 1.0f;
        private float cameraYaw;
        private bool isPlaying;
        private bool showAdventurer = true;
        private bool showSkeleton = true;
        private bool showEnvironment = true;
        private bool spinAccessory = true;
        private GUIStyle headerStyle;
        private GUIStyle selectedClipStyle;
        private GUIStyle metadataStyle;

        public KayKitAnimationLibrary AnimationLibrary => animationLibrary;
        public GameObject AdventurerRoot => adventurerRoot;
        public Animator AdventurerAnimator => adventurerAnimator;
        public GameObject SkeletonRoot => skeletonRoot;
        public Animator SkeletonAnimator => skeletonAnimator;
        public GameObject EnvironmentRoot => environmentRoot;
        public GameObject AccessoryRoot => accessoryRoot;
        public Camera ShowcaseCamera => showcaseCamera;
        public Transform CameraFocus => cameraFocus;
        public int AvailableClipCount =>
            animationLibrary == null
                ? 0
                : animationLibrary.Entries.Count(entry => entry.Clip != null);
        public bool HasRequiredReferences =>
            animationLibrary != null
            && adventurerRoot != null
            && adventurerAnimator != null
            && skeletonRoot != null
            && skeletonAnimator != null
            && environmentRoot != null
            && accessoryRoot != null
            && showcaseCamera != null
            && cameraFocus != null;
        public bool IsPlaying => isPlaying;
        public KayKitAnimationEntry SelectedEntry =>
            selectedEntryIndex >= 0 && selectedEntryIndex < playableEntries.Count
                ? playableEntries[selectedEntryIndex]
                : null;
        public float NormalizedTime => GetNormalizedTime();

        public void Configure(
            KayKitAnimationLibrary library,
            GameObject adventurer,
            GameObject skeleton,
            GameObject environment,
            GameObject accessory,
            Camera camera,
            Transform focus
        )
        {
            animationLibrary = library;
            adventurerRoot = adventurer;
            adventurerAnimator =
                adventurer == null ? null : adventurer.GetComponentInChildren<Animator>(true);
            skeletonRoot = skeleton;
            skeletonAnimator =
                skeleton == null ? null : skeleton.GetComponentInChildren<Animator>(true);
            environmentRoot = environment;
            accessoryRoot = accessory;
            showcaseCamera = camera;
            cameraFocus = focus;

            if (Application.isPlaying)
                InitializeShowcase();
        }

        public bool TrySelectClip(string clipId)
        {
            if (playableEntries.Count == 0)
                BuildCatalog();
            if (Application.isPlaying && !HasRequiredReferences)
                return false;

            int index = playableEntries.FindIndex(entry =>
                string.Equals(entry.Id, clipId, StringComparison.OrdinalIgnoreCase)
            );
            if (index < 0)
                return false;

            SelectEntry(index);
            return true;
        }

        private void Start()
        {
            InitializeShowcase();
        }

        private void Update()
        {
            if (spinAccessory && accessoryRoot != null && accessoryRoot.activeInHierarchy)
                accessoryRoot.transform.Rotate(
                    0.0f,
                    25.0f * Time.unscaledDeltaTime,
                    0.0f,
                    Space.World
                );

            KayKitAnimationEntry entry = SelectedEntry;
            if (!isPlaying || entry == null || entry.Loop || clipPlayables.Count == 0)
                return;

            if (clipPlayables[0].GetTime() < entry.Clip.length)
                return;

            SetPlayableTime(entry.Clip.length);
            SetPlaybackState(false);
        }

        private void OnDisable()
        {
            DestroyPlayableGraph();
        }

        private void OnGUI()
        {
            EnsureStyles();

            Rect safeArea = Screen.safeArea;
            float width = Mathf.Clamp(safeArea.width * 0.29f, 330.0f, 430.0f);
            Rect panel = new(
                safeArea.x + 12.0f,
                safeArea.y + 12.0f,
                width,
                safeArea.height - 24.0f
            );

            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label("KayKit Showcase", headerStyle);
            GUILayout.Label(
                "Press Play, then browse a category or search for any imported clip. "
                    + "Both Humanoid rigs preview the same animation.",
                metadataStyle
            );
            GUILayout.Space(6.0f);

            if (!HasRequiredReferences)
            {
                GUILayout.Label(
                    "The scene is missing one or more required showcase references.",
                    metadataStyle
                );
                GUILayout.EndArea();
                return;
            }

            DrawDisplayControls();
            GUILayout.Space(6.0f);
            DrawAnimationControls();
            GUILayout.Space(6.0f);
            DrawClipBrowser();
            GUILayout.FlexibleSpace();
            DrawCameraControls();
            GUILayout.EndArea();
        }

        private void InitializeShowcase()
        {
            DestroyPlayableGraph();
            BuildCatalog();
            ApplyDisplayVisibility();
            ApplyCamera();

            if (!HasRequiredReferences)
            {
                Debug.LogError("KayKit showcase is missing required scene references.", this);
                return;
            }

            ConfigureAnimator(adventurerAnimator);
            ConfigureAnimator(skeletonAnimator);

            int defaultIndex = playableEntries.FindIndex(entry =>
                string.Equals(entry.Id, DefaultClipId, StringComparison.OrdinalIgnoreCase)
            );
            SelectEntry(defaultIndex >= 0 ? defaultIndex : 0);
            int categoryIndex = Array.FindIndex(
                categories,
                category =>
                    string.Equals(
                        category,
                        SelectedEntry?.SourceCategory,
                        StringComparison.OrdinalIgnoreCase
                    )
            );
            selectedCategoryIndex = Mathf.Max(0, categoryIndex);
            RebuildVisibleEntries();
        }

        private void BuildCatalog()
        {
            playableEntries.Clear();
            if (animationLibrary != null)
            {
                playableEntries.AddRange(
                    animationLibrary
                        .Entries.Where(entry => entry.Clip != null)
                        .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                );
            }

            categories = playableEntries
                .Select(entry => entry.SourceCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.Ordinal)
                .ToArray();
            selectedCategoryIndex = Mathf.Clamp(
                selectedCategoryIndex,
                0,
                Mathf.Max(0, categories.Length - 1)
            );
            RebuildVisibleEntries();
        }

        private static void ConfigureAnimator(Animator animator)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void SelectEntry(int index)
        {
            if (index < 0 || index >= playableEntries.Count)
                return;

            selectedEntryIndex = index;
            KayKitAnimationEntry entry = playableEntries[index];
            if (!Application.isPlaying)
                return;

            DestroyPlayableGraph();
            playableGraph = PlayableGraph.Create("KayKit Showcase Preview");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            AddAnimationOutput(adventurerAnimator, entry.Clip, "Adventurer");
            AddAnimationOutput(skeletonAnimator, entry.Clip, "Skeleton");
            SetPlaybackSpeed(playbackSpeed);
            playableGraph.Play();
            isPlaying = true;
        }

        private void AddAnimationOutput(Animator target, AnimationClip clip, string outputName)
        {
            AnimationClipPlayable clipPlayable = AnimationClipPlayable.Create(playableGraph, clip);
            clipPlayable.SetApplyFootIK(true);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                playableGraph,
                outputName,
                target
            );
            output.SetSourcePlayable(clipPlayable);
            clipPlayables.Add(clipPlayable);
        }

        private void DestroyPlayableGraph()
        {
            if (playableGraph.IsValid())
                playableGraph.Destroy();

            clipPlayables.Clear();
            isPlaying = false;
        }

        private void DrawDisplayControls()
        {
            GUILayout.Label("Models", headerStyle);
            GUILayout.BeginHorizontal();
            bool nextAdventurer = GUILayout.Toggle(showAdventurer, "Adventurer");
            bool nextSkeleton = GUILayout.Toggle(showSkeleton, "Skeleton");
            bool nextEnvironment = GUILayout.Toggle(showEnvironment, "Dungeon");
            GUILayout.EndHorizontal();
            bool nextSpinAccessory = GUILayout.Toggle(spinAccessory, "Rotate ranger bow display");

            if (
                nextAdventurer == showAdventurer
                && nextSkeleton == showSkeleton
                && nextEnvironment == showEnvironment
                && nextSpinAccessory == spinAccessory
            )
            {
                return;
            }

            showAdventurer = nextAdventurer;
            showSkeleton = nextSkeleton;
            showEnvironment = nextEnvironment;
            spinAccessory = nextSpinAccessory;
            ApplyDisplayVisibility();
        }

        private void DrawAnimationControls()
        {
            KayKitAnimationEntry entry = SelectedEntry;
            GUILayout.Label("Animation", headerStyle);
            if (entry == null)
            {
                GUILayout.Label("No playable clips are available.", metadataStyle);
                return;
            }

            GUILayout.Label(Humanize(entry.Clip.name), headerStyle);
            GUILayout.Label(
                $"{entry.SourceCategory}  |  {entry.Duration:0.00}s  |  "
                    + (entry.Loop ? "Looping" : "One-shot"),
                metadataStyle
            );

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous"))
                SelectRelative(-1);
            if (GUILayout.Button(isPlaying ? "Pause" : "Play"))
                TogglePlayback();
            if (GUILayout.Button("Restart"))
                RestartPlayback();
            if (GUILayout.Button("Next"))
                SelectRelative(1);
            GUILayout.EndHorizontal();

            float normalizedTime = GetNormalizedTime();
            float nextNormalizedTime = GUILayout.HorizontalSlider(normalizedTime, 0.0f, 1.0f);
            if (!Mathf.Approximately(normalizedTime, nextNormalizedTime))
                SeekNormalized(nextNormalizedTime);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Speed", GUILayout.Width(45.0f));
            float nextSpeed = GUILayout.HorizontalSlider(
                playbackSpeed,
                MinimumPlaybackSpeed,
                MaximumPlaybackSpeed
            );
            GUILayout.Label($"{nextSpeed:0.00}x", GUILayout.Width(42.0f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(playbackSpeed, nextSpeed))
                SetPlaybackSpeed(nextSpeed);
        }

        private void DrawClipBrowser()
        {
            GUILayout.Label($"Clips ({visibleEntries.Count}/{playableEntries.Count})", headerStyle);
            if (categories.Length > 0)
            {
                int nextCategory = GUILayout.SelectionGrid(selectedCategoryIndex, categories, 2);
                if (nextCategory != selectedCategoryIndex)
                {
                    selectedCategoryIndex = nextCategory;
                    searchText = string.Empty;
                    RebuildVisibleEntries();
                    if (visibleEntries.Count > 0)
                        SelectEntry(playableEntries.IndexOf(visibleEntries[0]));
                }
            }

            string nextSearch = GUILayout.TextField(searchText);
            if (!string.Equals(nextSearch, searchText, StringComparison.Ordinal))
            {
                searchText = nextSearch;
                RebuildVisibleEntries();
            }

            float listHeight = Mathf.Clamp(Screen.safeArea.height * 0.27f, 150.0f, 300.0f);
            clipScroll = GUILayout.BeginScrollView(clipScroll, GUILayout.Height(listHeight));
            foreach (KayKitAnimationEntry entry in visibleEntries)
            {
                int index = playableEntries.IndexOf(entry);
                GUIStyle style = index == selectedEntryIndex ? selectedClipStyle : GUI.skin.button;
                string label = Humanize(entry.Clip.name) + (entry.Loop ? "  [Loop]" : string.Empty);
                if (GUILayout.Button(label, style))
                    SelectEntry(index);
            }
            GUILayout.EndScrollView();
        }

        private void DrawCameraControls()
        {
            GUILayout.Label("Camera", headerStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Orbit Left"))
            {
                cameraYaw -= 15.0f;
                ApplyCamera();
            }
            if (GUILayout.Button("Reset"))
            {
                cameraYaw = 0.0f;
                cameraDistance = 11.0f;
                ApplyCamera();
            }
            if (GUILayout.Button("Orbit Right"))
            {
                cameraYaw += 15.0f;
                ApplyCamera();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Zoom", GUILayout.Width(45.0f));
            float nextDistance = GUILayout.HorizontalSlider(
                cameraDistance,
                MinimumCameraDistance,
                MaximumCameraDistance
            );
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(cameraDistance, nextDistance))
            {
                cameraDistance = nextDistance;
                ApplyCamera();
            }
        }

        private void SelectRelative(int offset)
        {
            if (visibleEntries.Count == 0)
                return;

            KayKitAnimationEntry selected = SelectedEntry;
            int visibleIndex = visibleEntries.IndexOf(selected);
            if (visibleIndex < 0)
                visibleIndex = 0;
            else
                visibleIndex =
                    (visibleIndex + offset + visibleEntries.Count) % visibleEntries.Count;

            SelectEntry(playableEntries.IndexOf(visibleEntries[visibleIndex]));
        }

        private void TogglePlayback()
        {
            if (!playableGraph.IsValid())
                return;

            if (isPlaying)
            {
                SetPlaybackState(false);
                return;
            }

            KayKitAnimationEntry entry = SelectedEntry;
            if (entry != null && !entry.Loop && GetNormalizedTime() >= 0.999f)
                SetPlayableTime(0.0);
            SetPlaybackState(true);
        }

        private void RestartPlayback()
        {
            SetPlayableTime(0.0);
            SetPlaybackState(true);
        }

        private void SetPlaybackState(bool shouldPlay)
        {
            if (!playableGraph.IsValid())
                return;

            if (shouldPlay)
                playableGraph.Play();
            else
                playableGraph.Stop();
            isPlaying = shouldPlay;
        }

        private void SetPlaybackSpeed(float speed)
        {
            playbackSpeed = Mathf.Clamp(speed, MinimumPlaybackSpeed, MaximumPlaybackSpeed);
            foreach (AnimationClipPlayable clipPlayable in clipPlayables)
                clipPlayable.SetSpeed(playbackSpeed);
        }

        private float GetNormalizedTime()
        {
            KayKitAnimationEntry entry = SelectedEntry;
            if (entry == null || entry.Clip.length <= 0.0f || clipPlayables.Count == 0)
                return 0.0f;

            double time = clipPlayables[0].GetTime();
            if (entry.Loop)
                time %= entry.Clip.length;
            return Mathf.Clamp01((float)(time / entry.Clip.length));
        }

        private void SeekNormalized(float normalizedTime)
        {
            KayKitAnimationEntry entry = SelectedEntry;
            if (entry == null)
                return;

            SetPlayableTime(Mathf.Clamp01(normalizedTime) * entry.Clip.length);
        }

        private void SetPlayableTime(double time)
        {
            if (!playableGraph.IsValid())
                return;

            foreach (AnimationClipPlayable clipPlayable in clipPlayables)
                clipPlayable.SetTime(time);
            playableGraph.Evaluate(0.0f);
        }

        private void RebuildVisibleEntries()
        {
            visibleEntries.Clear();
            if (categories.Length == 0)
                return;

            string category = categories[selectedCategoryIndex];
            bool isSearching = !string.IsNullOrWhiteSpace(searchText);
            foreach (KayKitAnimationEntry entry in playableEntries)
            {
                if (
                    !isSearching
                    && !string.Equals(
                        entry.SourceCategory,
                        category,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                if (
                    isSearching
                    && entry.Clip.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Id.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0
                )
                {
                    continue;
                }

                visibleEntries.Add(entry);
            }
        }

        private void ApplyDisplayVisibility()
        {
            if (adventurerRoot != null)
                adventurerRoot.SetActive(showAdventurer);
            if (skeletonRoot != null)
                skeletonRoot.SetActive(showSkeleton);
            if (environmentRoot != null)
                environmentRoot.SetActive(showEnvironment);
        }

        private void ApplyCamera()
        {
            if (showcaseCamera == null || cameraFocus == null)
                return;

            Vector3 localOffset = new(0.0f, cameraHeight, -cameraDistance);
            Vector3 worldOffset = Quaternion.Euler(0.0f, cameraYaw, 0.0f) * localOffset;
            showcaseCamera.transform.position = cameraFocus.position + worldOffset;
            showcaseCamera.transform.LookAt(cameraFocus.position);
        }

        private void EnsureStyles()
        {
            if (headerStyle != null)
                return;

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            metadataStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            selectedClipStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            selectedClipStyle.normal.textColor = new Color(1.0f, 0.85f, 0.35f);
        }

        private static string Humanize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unnamed" : value.Replace('_', ' ');
        }
    }
}
