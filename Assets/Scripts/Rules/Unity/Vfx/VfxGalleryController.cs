using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Rules.Unity.Vfx
{
    /// <summary>
    /// Interactive in-Unity review gallery that plays the exact production catalog at gameplay scale.
    /// </summary>
    public sealed class VfxGalleryController : MonoBehaviour
    {
        private readonly List<VfxCoverageEntry> filtered = new();
        private VfxCoverageManifest manifest;
        private UnityVfxPlayback playback;
        private VfxGalleryPresentationFixture fixture;
        private Transform source;
        private readonly List<Transform> targets = new();
        private Vector2 scroll;
        private string filter = string.Empty;
        private int selected;
        private Coroutine playAll;
        private Coroutine selectedPlayback;
        private IEnumerator playAllRoutine;
        private IEnumerator selectedPlaybackRoutine;
        private VfxCoverageEntry lastPlayed;
        private float persistentPreviewSeconds = 0.8f;
        private string feedback = string.Empty;
        private bool closeView;
        private Vector3 wideCameraPosition;
        private Quaternion wideCameraRotation;

        /// <summary>Gets the stable ID currently selected by the gallery.</summary>
        public string SelectedId => filtered.Count == 0 ? string.Empty : filtered[selected].id;

        /// <summary>Gets the number of manifest entries currently available to Play All.</summary>
        public int EntryCount => filtered.Count;

        /// <summary>Gets the number of live production objects owned by the gallery session.</summary>
        public int LiveVfxObjectCount => playback?.LiveObjectCount ?? 0;

        /// <summary>Gets the production cues emitted by the most recently completed fixture.</summary>
        public IReadOnlyList<string> LastTrace => fixture.Trace;

        internal IReadOnlyList<(
            string Cue,
            Vector3 Origin,
            Vector3 Destination
        )> LastTransientStarts => fixture.TransientStarts;

        /// <summary>Gets the exact target count staged for the current fixture.</summary>
        public int CurrentTargetCount => fixture.TargetCount;

        /// <summary>Gets the health projected for the primary target by the latest fixture.</summary>
        public int PrimaryTargetHitPoints => fixture.PrimaryTargetHitPoints;

        /// <summary>Gets whether the primary target remains active after the latest fixture.</summary>
        public bool IsPrimaryTargetActive => fixture.IsPrimaryTargetActive;

        /// <summary>Gets whether a selected or play-all timeline is currently owned by the gallery.</summary>
        public bool IsPlaybackActive => selectedPlayback != null || playAll != null;

        /// <summary>Gets whether the tracked Play All control owns the active timeline.</summary>
        public bool IsPlayAllActive => playAll != null;

        /// <summary>Gets whether the tracked selected-entry control owns the active timeline.</summary>
        public bool IsSelectedPlaybackActive => selectedPlayback != null;

        /// <summary>Selects an exact stable entry for deterministic tests and evidence capture.</summary>
        public void Select(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A gallery entry ID is required.", nameof(id));
            filter = string.Empty;
            ApplyFilter();
            int index = filtered.FindIndex(entry =>
                string.Equals(entry.id, id, StringComparison.Ordinal)
            );
            if (index < 0)
                throw new KeyNotFoundException($"Unknown VFX gallery entry '{id}'.");
            selected = index;
        }

        /// <summary>Plays the selected entry through the same catalog used by gallery controls.</summary>
        public IEnumerator PlaySelectedForTests() => PlayRoutine(Current());

        /// <summary>Starts the selected timeline through the same tracked path as the UI.</summary>
        public void BeginSelectedForTests() => PlaySelected();

        /// <summary>Starts Play All through the same tracked path as the UI.</summary>
        public void BeginPlayAllForTests()
        {
            if (playAll == null)
                TogglePlayAll();
        }

        internal void StopPlayAllForTests()
        {
            if (playAll != null)
                TogglePlayAll();
        }

        internal void ConfigureTestTiming(float persistentHoldSeconds)
        {
            if (persistentHoldSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(persistentHoldSeconds));
            persistentPreviewSeconds = persistentHoldSeconds;
            playback.ConfigureTestTiming(0.01f);
            fixture.ConfigureTestTiming(persistentHoldSeconds);
        }

        private void Awake()
        {
            Time.timeScale = 1f;
            manifest = VfxCoverageManifest.Load();
            playback = new UnityVfxPlayback(new ResourcesVfxPrefabCatalog(), "Gallery VFX");
            source = GameObject.Find("Gallery Source")?.transform;
            Transform target = GameObject.Find("Gallery Target")?.transform;
            if (source == null || target == null)
                throw new InvalidOperationException(
                    "The VFX gallery requires source and target models."
                );
            targets.Add(target);
            for (int index = 1; index < 3; index++)
            {
                Transform clone = Instantiate(target.gameObject).transform;
                clone.name = "Gallery Target " + (index + 1);
                targets.Add(clone);
            }
            fixture = new VfxGalleryPresentationFixture(playback, source, targets);
            Camera camera = Camera.main;
            if (camera != null)
            {
                wideCameraPosition = camera.transform.position;
                wideCameraRotation = camera.transform.rotation;
            }
            ApplyFilter();
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            fixture?.Dispose();
            playback?.Dispose();
        }

        private void OnDisable()
        {
            CancelPlayback();
            Time.timeScale = 1f;
        }

        private void OnGUI()
        {
            const float width = 430f;
            GUILayout.BeginArea(new Rect(18f, 18f, width, Screen.height - 36f), GUI.skin.box);
            GUILayout.Label("SPELL & STRIKE VFX GALLERY");
            GUILayout.Label("Production prefabs • deterministic fixtures • full timelines");
            GUILayout.Label(
                "Select or filter an entry. IDs copy into bug reports; Close View follows contact."
            );
            string nextFilter = GUILayout.TextField(filter ?? string.Empty);
            if (!string.Equals(nextFilter, filter, StringComparison.Ordinal))
            {
                filter = nextFilter;
                ApplyFilter();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous"))
                Move(-1);
            if (GUILayout.Button("Play Selected"))
                PlaySelected();
            if (GUILayout.Button("Replay"))
                Play(lastPlayed ?? Current());
            if (GUILayout.Button("Next"))
                Move(1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(playAll == null ? "Play All" : "Stop All"))
                TogglePlayAll();
            if (GUILayout.Button(Time.timeScale == 0f ? "Resume" : "Pause"))
                Time.timeScale = Time.timeScale == 0f ? 1f : 0f;
            if (GUILayout.Button("Slow Motion"))
                Time.timeScale = Mathf.Approximately(Time.timeScale, 0.35f) ? 1f : 0.35f;
            if (GUILayout.Button("Reset"))
                ResetGallery();
            if (GUILayout.Button(closeView ? "Wide View" : "Close View"))
                ToggleView();
            if (GUILayout.Button("Main Menu"))
                SceneManager.LoadScene("MainMenuScene");
            GUILayout.EndHorizontal();

            VfxCoverageEntry current = Current();
            if (current != null)
            {
                GUILayout.Label("ID: " + current.id);
                if (GUILayout.Button("Copy ID"))
                {
                    GUIUtility.systemCopyBuffer = current.id;
                    feedback = "Copied " + current.id;
                }
                GUILayout.Label(
                    current.displayName
                        + " • "
                        + current.variant
                        + " • "
                        + current.outcome
                        + " • "
                        + (current.productionReachable ? "production-reachable" : "fixture-only")
                );
                GUILayout.Label("Trigger: " + current.gameplayTrigger);
                GUILayout.Label("Cue: " + current.cue);
                GUILayout.Label($"Fixture targets: {fixture.TargetCount} • {feedback}");
                if (fixture.Trace.Count > 0)
                    GUILayout.Label("Production trace: " + string.Join(" → ", fixture.Trace));
            }

            scroll = GUILayout.BeginScrollView(scroll);
            for (int index = 0; index < filtered.Count; index++)
            {
                VfxCoverageEntry entry = filtered[index];
                bool active = index == selected;
                if (GUILayout.Toggle(active, entry.id, "Button") && !active)
                    selected = index;
            }
            GUILayout.EndScrollView();
            GUILayout.Label(
                $"{filtered.Count} entries • {playback.LiveObjectCount} live VFX objects"
            );
            GUILayout.EndArea();
        }

        /// <summary>Plays every filtered entry once through the production prefab catalog.</summary>
        public IEnumerator PlayAllEntries()
        {
            for (int index = 0; index < filtered.Count; index++)
            {
                selected = index;
                IEnumerator routine = PlayRoutine(filtered[index]);
                using (routine as IDisposable)
                {
                    while (routine.MoveNext())
                        yield return routine.Current;
                }
            }
            playAll = null;
            playAllRoutine = null;
        }

        /// <summary>Resets playback and restores normal simulation speed.</summary>
        public void ResetGallery()
        {
            CancelPlayback();
            Time.timeScale = 1f;
            feedback = "Reset complete; no gallery-owned visuals remain.";
        }

        private void ApplyFilter()
        {
            filtered.Clear();
            string query = (filter ?? string.Empty).Trim();
            filtered.AddRange(
                manifest.entries.Where(entry =>
                    query.Length == 0
                    || entry.id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                )
            );
            selected = Mathf.Clamp(selected, 0, Math.Max(0, filtered.Count - 1));
        }

        private void Move(int delta)
        {
            if (filtered.Count == 0)
                return;
            selected = (selected + delta + filtered.Count) % filtered.Count;
        }

        private void PlaySelected() => Play(Current());

        private void Play(VfxCoverageEntry entry)
        {
            if (entry != null)
            {
                CancelPlayback();
                selectedPlaybackRoutine = PlayTracked(entry);
                selectedPlayback = StartCoroutine(selectedPlaybackRoutine);
            }
        }

        private IEnumerator PlayRoutine(VfxCoverageEntry entry)
        {
            lastPlayed = entry;
            fixture.Reset();
            playback.Reset();
            feedback = "Playing committed fixture through " + entry.gameplayTrigger;
            IEnumerator routine = fixture.Play(entry);
            using (routine as IDisposable)
            {
                while (routine.MoveNext())
                    yield return routine.Current;
            }
            feedback = "Complete: " + entry.id;
        }

        private IEnumerator PlayTracked(VfxCoverageEntry entry)
        {
            IEnumerator routine = PlayRoutine(entry);
            using (routine as IDisposable)
            {
                while (routine.MoveNext())
                    yield return routine.Current;
            }
            selectedPlayback = null;
            selectedPlaybackRoutine = null;
        }

        private void TogglePlayAll()
        {
            if (playAll != null)
            {
                CancelPlayback();
                feedback = "Play All stopped; no gallery-owned visuals remain.";
                return;
            }
            CancelPlayback();
            playAllRoutine = PlayAllEntries();
            playAll = StartCoroutine(playAllRoutine);
        }

        private void CancelPlayback()
        {
            if (playAll != null)
            {
                IEnumerator routine = playAllRoutine;
                StopCoroutine(playAll);
                playAll = null;
                playAllRoutine = null;
                (routine as IDisposable)?.Dispose();
            }
            if (selectedPlayback != null)
            {
                IEnumerator routine = selectedPlaybackRoutine;
                StopCoroutine(selectedPlayback);
                selectedPlayback = null;
                selectedPlaybackRoutine = null;
                (routine as IDisposable)?.Dispose();
            }
            fixture?.Reset();
            playback?.Reset();
        }

        private VfxCoverageEntry Current() =>
            filtered.Count == 0 ? null : filtered[Mathf.Clamp(selected, 0, filtered.Count - 1)];

        private void ToggleView()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;
            closeView = !closeView;
            if (!closeView)
            {
                camera.transform.SetPositionAndRotation(wideCameraPosition, wideCameraRotation);
                return;
            }
            Vector3 midpoint =
                targets.Count == 0
                    ? source.position
                    : Vector3.Lerp(source.position, targets[0].position, 0.65f);
            camera.transform.position = midpoint + new Vector3(0f, 3.2f, -5.2f);
            camera.transform.LookAt(midpoint + Vector3.up);
        }
    }
}
