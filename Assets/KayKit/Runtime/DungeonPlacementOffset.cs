using UnityEngine;

namespace Game.KayKit
{
    /// <summary>
    /// Describes how a catalog prefab's root moves from its logical grid-cell anchor to its
    /// visual placement point.
    /// </summary>
    /// <remarks>
    /// The offset uses the prefab's local axes. Map population rotates it with the instance,
    /// allowing a single wall-mount correction to work for every cardinal orientation while
    /// preserving the relative transforms of the prefab's model, lights, and other children.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DungeonPlacementOffset : MonoBehaviour
    {
        [SerializeField]
        private Vector3 localOffset;

        /// <summary>Gets the root-position correction expressed in the prefab's local axes.</summary>
        public Vector3 LocalOffset => localOffset;

#if UNITY_EDITOR
        /// <summary>Configures the offset stored by generated project-owned wrapper prefabs.</summary>
        /// <param name="offset">The root-position correction in prefab-local world units.</param>
        public void Configure(Vector3 offset)
        {
            localOffset = offset;
        }
#endif
    }
}
