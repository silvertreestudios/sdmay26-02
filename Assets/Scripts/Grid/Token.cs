using GridPrivate;
using GridPublic;
using UnityEngine;

namespace GridPublic
{
    public class Token : MonoBehaviour
    {
        private bool registered;

        private void Awake()
        {
            if (isActiveAndEnabled)
                TryRegister();
        }

        private void OnEnable()
        {
            if (!registered)
                TryRegister();
        }

        private bool TryRegister()
        {
            return GridAPI.TryGetInstance(out GridAPI grid) && TryRegister(grid);
        }

        private bool TryRegister(GridAPI grid)
        {
            if (registered || !isActiveAndEnabled || grid is not GridAPIPrivate privateGrid)
                return registered;

            Vector3Int position = Vector3Int.RoundToInt(transform.position);
            if (!GridTargeting.IsInBounds(privateGrid.GetTiles(), position))
            {
                Debug.LogWarning(
                    $"Failed to register token '{name}' at grid cell ({position.x}, {position.z}). " +
                    "The cell is outside the grid bounds.",
                    this);
                return false;
            }

            registered = privateGrid.AddToken(gameObject);
            if (!registered)
            {
                Debug.LogWarning(
                    $"Failed to register token '{name}' at grid cell ({position.x}, {position.z}). " +
                    "The cell may be blocked or already occupied.",
                    this);
            }
            return registered;
        }

        public void TryRegisterWithGrid(GridAPI grid)
        {
            TryRegister(grid);
        }
    }
}
