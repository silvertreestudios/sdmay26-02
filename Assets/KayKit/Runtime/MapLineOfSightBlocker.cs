using UnityEngine;

namespace Game.KayKit
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class MapLineOfSightBlocker : MonoBehaviour
    {
        private const int RaycastBufferSize = 32;
        private static readonly RaycastHit[] RaycastBuffer = new RaycastHit[RaycastBufferSize];

        public static bool BlocksSegment(Vector3 start, Vector3 end)
        {
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            if (distance <= Mathf.Epsilon)
                return false;

            Vector3 normalizedDirection = direction / distance;
            int hitCount = Physics.RaycastNonAlloc(
                start,
                normalizedDirection,
                RaycastBuffer,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.UseGlobal);
            if (hitCount < RaycastBuffer.Length)
                return ContainsBlocker(RaycastBuffer, hitCount);

            RaycastHit[] allHits = Physics.RaycastAll(
                start,
                normalizedDirection,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.UseGlobal);
            return ContainsBlocker(allHits, allHits.Length);
        }

        private static bool ContainsBlocker(RaycastHit[] hits, int count)
        {
            for (int index = 0; index < count; index++)
            {
                Collider collider = hits[index].collider;
                if (collider != null && collider.GetComponentInParent<MapLineOfSightBlocker>() != null)
                    return true;
            }

            return false;
        }
    }
}
