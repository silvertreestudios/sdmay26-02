using UnityEngine;

namespace Game.Rules.Unity.Vfx
{
    /// <summary>Runs the bounded timeline embedded in each production VFX prefab.</summary>
    public sealed class UnityVfxInstance : MonoBehaviour
    {
        [SerializeField]
        private VfxMotionKind motion = VfxMotionKind.Burst;

        [SerializeField]
        private float duration = 0.75f;

        [SerializeField]
        private float baseScale = 1f;

        private Vector3 origin;
        private Vector3 destination;
        private Transform follow;
        private Vector3 followLocalOffset = Vector3.up * 0.55f;
        private bool followRotation;
        private bool hadFollow;
        private float elapsed;
        private float intensity = 1f;
        private bool persistent;
        private bool begun;
        private float runtimeDuration;

        /// <summary>Gets whether a transient has reached its terminal frame.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>Configures the authored motion for a newly spawned instance.</summary>
        public void Begin(
            Vector3 start,
            Vector3 end,
            float playbackIntensity,
            bool persistent,
            float durationScale
        )
        {
            origin = start;
            destination = end;
            intensity = Mathf.Max(0.25f, playbackIntensity);
            this.persistent = persistent;
            elapsed = 0f;
            begun = true;
            runtimeDuration = duration * Mathf.Clamp(durationScale, 0.001f, 1f);
            IsComplete = false;
            transform.position = start;
            Vector3 direction = end - start;
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.localScale = Vector3.one * baseScale * intensity;
            foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>())
                particles.Play(true);
        }

        /// <summary>Attaches a persistent visual to a live scene owner without reparenting it.</summary>
        public void Follow(Transform owner)
        {
            Follow(owner, Vector3.up * 0.55f, false);
        }

        /// <summary>Attaches a persistent visual with an explicit local offset and rotation policy.</summary>
        public void Follow(Transform owner, Vector3 localOffset, bool rotateWithOwner)
        {
            follow = owner;
            hadFollow = owner != null;
            followLocalOffset = localOffset;
            followRotation = rotateWithOwner;
        }

        private void Update()
        {
            if (!begun)
                return;
            if (persistent && hadFollow && (follow == null || !follow.gameObject.activeInHierarchy))
            {
                Destroy(gameObject);
                return;
            }
            if (follow != null)
            {
                origin = follow.TransformPoint(followLocalOffset);
                destination = origin;
                if (followRotation)
                    transform.rotation = follow.rotation;
            }
            if (persistent)
            {
                transform.position = origin;
                float pulse = 0.94f + Mathf.Sin(Time.time * 3f) * 0.06f;
                transform.localScale = Vector3.one * baseScale * intensity * pulse;
                return;
            }

            elapsed += Time.deltaTime;
            float progress = runtimeDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / runtimeDuration);
            switch (motion)
            {
                case VfxMotionKind.Projectile:
                    transform.position = Vector3.Lerp(
                        origin,
                        destination,
                        Mathf.SmoothStep(0f, 1f, progress)
                    );
                    break;
                case VfxMotionKind.Beam:
                    Vector3 direction = destination - origin;
                    float distance = Mathf.Max(0.1f, direction.magnitude);
                    transform.position = Vector3.Lerp(origin, destination, 0.5f);
                    if (direction.sqrMagnitude > 0.001f)
                        transform.rotation = Quaternion.LookRotation(
                            direction.normalized,
                            Vector3.up
                        );
                    transform.localScale = new Vector3(
                        baseScale * intensity,
                        baseScale * intensity,
                        distance * Mathf.SmoothStep(0.05f, 1f, progress)
                    );
                    break;
                case VfxMotionKind.Emanation:
                    transform.position = origin;
                    transform.localScale =
                        new Vector3(1f, 0.2f, 1f)
                        * baseScale
                        * intensity
                        * Mathf.Lerp(0.2f, 4f, progress);
                    break;
                default:
                    transform.position = origin;
                    transform.localScale =
                        Vector3.one * baseScale * intensity * Mathf.Lerp(0.35f, 1.45f, progress);
                    break;
            }
            IsComplete = progress >= 1f;
        }
    }
}
