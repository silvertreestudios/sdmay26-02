using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Rules.Unity.Vfx
{
    internal sealed class UnityVfxCoroutineRunner : MonoBehaviour { }

    /// <summary>Runs fire-and-forget committed Fact presentation on a live component owner.</summary>
    public static class VfxCoroutineHost
    {
        /// <summary>Starts a bounded VFX routine when its owner is active.</summary>
        public static void Run(MonoBehaviour owner, IEnumerator routine)
        {
            if (owner == null || !owner.isActiveAndEnabled || routine == null)
                return;
            if (!Application.isPlaying)
            {
                try
                {
                    while (routine.MoveNext()) { }
                }
                finally
                {
                    (routine as IDisposable)?.Dispose();
                }
                return;
            }
            owner.StartCoroutine(routine);
        }
    }

    /// <summary>Identifies one production visual-effect prefab without exposing asset paths to features.</summary>
    public readonly struct VfxCueId : IEquatable<VfxCueId>
    {
        /// <summary>Creates a stable cue identifier.</summary>
        public VfxCueId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A VFX cue identifier is required.", nameof(value));
            Value = value.Trim();
        }

        /// <summary>Gets the stable Resources-relative cue identifier.</summary>
        public string Value { get; }

        /// <inheritdoc/>
        public bool Equals(VfxCueId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is VfxCueId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Loads the exact production prefab for a stable cue.</summary>
    public interface IVfxPrefabCatalog
    {
        /// <summary>Returns the required prefab or throws when production content is incomplete.</summary>
        GameObject Require(VfxCueId cue);
    }

    /// <summary>Loads production VFX prefabs from the committed Resources catalog.</summary>
    public sealed class ResourcesVfxPrefabCatalog : IVfxPrefabCatalog
    {
        /// <inheritdoc/>
        public GameObject Require(VfxCueId cue)
        {
            GameObject prefab = Resources.Load<GameObject>("Vfx/" + cue.Value);
            return prefab != null
                ? prefab
                : throw new InvalidOperationException($"VFX cue '{cue}' has no production prefab.");
        }
    }

    /// <summary>
    /// Owns spawned transient and persistent VFX for one encounter or gallery session.
    /// </summary>
    public sealed class UnityVfxPlayback : IDisposable
    {
        private readonly IVfxPrefabCatalog catalog;
        private readonly GameObject host;
        private readonly UnityVfxCoroutineRunner coroutineRunner;
        private readonly Dictionary<string, GameObject> persistent = new(StringComparer.Ordinal);
        private bool disposed;
        private float timelineDurationScale = 1f;

        internal event Action<VfxCueId> CueStarted = delegate { };
        internal event Action<VfxCueId, Vector3, Vector3> TransientStarted = delegate { };

        /// <summary>Creates an isolated playback owner.</summary>
        public UnityVfxPlayback(IVfxPrefabCatalog catalog, string ownerName)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (string.IsNullOrWhiteSpace(ownerName))
                throw new ArgumentException(
                    "A playback owner name is required.",
                    nameof(ownerName)
                );
            host = new GameObject(ownerName.Trim());
            coroutineRunner = host.AddComponent<UnityVfxCoroutineRunner>();
        }

        /// <summary>Gets the number of live objects owned by this playback session.</summary>
        public int LiveObjectCount =>
            host == null
                ? 0
                : host.GetComponentsInChildren<UnityVfxInstance>(includeInactive: false).Length;

        internal void ConfigureTestTiming(float durationScale)
        {
            if (durationScale <= 0f || durationScale > 1f)
                throw new ArgumentOutOfRangeException(nameof(durationScale));
            timelineDurationScale = durationScale;
        }

        /// <summary>
        /// Plays one complete transient timeline and yields until its terminal frame or cancellation.
        /// </summary>
        /// <param name="cue">The exact production cue to instantiate.</param>
        /// <param name="origin">The authored motion's starting world position.</param>
        /// <param name="destination">The authored motion's ending world position.</param>
        /// <param name="intensity">A relative scale multiplier for the committed outcome.</param>
        /// <param name="lifetimeOwner">
        /// Optional scene owner whose disable or destruction immediately ends the transient.
        /// </param>
        public IEnumerator PlayTransient(
            VfxCueId cue,
            Vector3 origin,
            Vector3 destination,
            float intensity = 1f,
            Transform lifetimeOwner = null
        )
        {
            ThrowIfDisposed();
            CueStarted(cue);
            TransientStarted(cue, origin, destination);
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(
                    catalog.Require(cue),
                    origin,
                    Quaternion.identity,
                    host.transform
                );
                instance.name = "VFX " + cue.Value;
                UnityVfxInstance behavior = instance.GetComponent<UnityVfxInstance>();
                if (behavior == null)
                    throw new InvalidOperationException(
                        $"VFX prefab '{cue}' is missing {nameof(UnityVfxInstance)}."
                    );
                behavior.BindLifetime(lifetimeOwner);
                behavior.Begin(
                    origin,
                    destination,
                    Mathf.Max(0.25f, intensity),
                    persistent: false,
                    timelineDurationScale
                );
                if (!Application.isPlaying)
                    yield break;
                while (instance != null && behavior != null && !behavior.IsComplete)
                    yield return null;
            }
            finally
            {
                Destroy(instance);
            }
        }

        /// <summary>
        /// Starts a bounded transient on the playback lifetime rather than a possibly defeated
        /// creature, while retaining optional scene-owner cancellation.
        /// </summary>
        internal void RunTransient(
            VfxCueId cue,
            Vector3 origin,
            Vector3 destination,
            float intensity = 1f,
            Transform lifetimeOwner = null
        )
        {
            ThrowIfDisposed();
            VfxCoroutineHost.Run(
                coroutineRunner,
                PlayTransient(cue, origin, destination, intensity, lifetimeOwner)
            );
        }

        /// <summary>Creates or replaces one persistent object under an authoritative stable key.</summary>
        public void SetPersistent(string key, VfxCueId cue, Transform owner, float intensity = 1f)
        {
            SetPersistent(key, cue, owner, Vector3.up * 0.55f, false, intensity);
        }

        /// <summary>Creates or replaces a persistent object at an explicit owner-relative anchor.</summary>
        public void SetPersistent(
            string key,
            VfxCueId cue,
            Transform owner,
            Vector3 localOffset,
            bool followRotation,
            float intensity = 1f
        )
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A persistent VFX key is required.", nameof(key));
            RemovePersistent(key);
            if (owner == null)
                return;
            CueStarted(cue);
            GameObject instance = UnityEngine.Object.Instantiate(
                catalog.Require(cue),
                owner.position,
                Quaternion.identity,
                host.transform
            );
            instance.name = "Persistent VFX " + key;
            UnityVfxInstance behavior = instance.GetComponent<UnityVfxInstance>();
            if (behavior == null)
            {
                Destroy(instance);
                throw new InvalidOperationException(
                    $"VFX prefab '{cue}' is missing {nameof(UnityVfxInstance)}."
                );
            }
            behavior.Follow(owner, localOffset, followRotation);
            behavior.Begin(
                owner.position,
                owner.position,
                Mathf.Max(0.25f, intensity),
                persistent: true,
                timelineDurationScale
            );
            persistent.Add(key, instance);
        }

        /// <summary>Removes one persistent object idempotently.</summary>
        public void RemovePersistent(string key)
        {
            if (
                string.IsNullOrWhiteSpace(key)
                || !persistent.TryGetValue(key, out GameObject instance)
            )
                return;
            persistent.Remove(key);
            Destroy(instance);
        }

        /// <summary>Destroys every owned object, light, particle system, and pending timeline.</summary>
        public void Reset()
        {
            persistent.Clear();
            if (host == null)
                return;
            for (int index = host.transform.childCount - 1; index >= 0; index--)
                Destroy(host.transform.GetChild(index).gameObject);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            persistent.Clear();
            Destroy(host);
        }

        private void ThrowIfDisposed()
        {
            if (disposed || host == null)
                throw new ObjectDisposedException(nameof(UnityVfxPlayback));
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (value is GameObject gameObject)
                gameObject.SetActive(false);
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }

    /// <summary>Provides deterministic feature-neutral endpoints for authored travel cues.</summary>
    internal static class VfxTrajectory
    {
        private const float MissOffset = 0.85f;

        /// <summary>
        /// Returns an endpoint beside the intended target so a missed projectile or beam cannot
        /// visually contact its model. Coincident or vertical endpoints use a stable world axis.
        /// </summary>
        internal static Vector3 ResolveMissDestination(Vector3 origin, Vector3 intendedDestination)
        {
            Vector3 horizontalDirection = intendedDestination - origin;
            horizontalDirection.y = 0f;
            Vector3 lateral =
                horizontalDirection.sqrMagnitude > 0.0001f
                    ? Vector3.Cross(Vector3.up, horizontalDirection.normalized)
                    : Vector3.right;
            return intendedDestination + lateral * MissOffset;
        }
    }

    /// <summary>Describes the motion authored into one generated production prefab.</summary>
    public enum VfxMotionKind
    {
        /// <summary>Expands and fades at its origin.</summary>
        Burst,

        /// <summary>Travels from origin to destination.</summary>
        Projectile,

        /// <summary>Expands horizontally around its origin.</summary>
        Emanation,

        /// <summary>Remains attached until authoritative removal.</summary>
        Persistent,

        /// <summary>Draws a short beam between origin and destination.</summary>
        Beam,
    }
}
