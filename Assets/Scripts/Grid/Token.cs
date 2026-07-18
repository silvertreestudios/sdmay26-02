using GridPrivate;
using GridPublic;
using UnityEngine;

namespace GridPublic
{
    /// <summary>
    /// Tracks a scene object that occupies one grid cell and keeps its registration coherent
    /// when runtime map data is replaced.
    /// </summary>
    public class Token : MonoBehaviour
    {
        private bool registered;
        private GridAPI registeredGrid;
        private bool detachedFromGrid;

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
            if (registered || detachedFromGrid || !isActiveAndEnabled ||
                grid is not GridAPIPrivate privateGrid)
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
            registeredGrid = registered ? grid : null;
            if (!registered)
            {
                Debug.LogWarning(
                    $"Failed to register token '{name}' at grid cell ({position.x}, {position.z}). " +
                    "The cell may be blocked or already occupied.",
                    this);
            }
            return registered;
        }

        /// <summary>
        /// Explicitly makes a previously removed token eligible for the supplied grid and
        /// attempts to place it at its current world-space cell.
        /// </summary>
        /// <param name="grid">The grid that should own the token.</param>
        public void TryRegisterWithGrid(GridAPI grid)
        {
            detachedFromGrid = false;
            TryRegister(grid);
        }

        internal bool TryGetRebindCell(GridAPI grid, out Vector3Int cell)
        {
            cell = Vector3Int.RoundToInt(transform.position);
            if (detachedFromGrid)
                return false;
            if (registered)
                return registeredGrid == grid;
            return isActiveAndEnabled;
        }

        internal bool RebindToGrid(GridAPI grid)
        {
            if (registered && registeredGrid != grid)
                return true;
            if (detachedFromGrid)
                return true;

            registered = false;
            registeredGrid = null;
            return !isActiveAndEnabled || TryRegister(grid);
        }

        /// <summary>
        /// Records a complete removal from the owning grid. Disabled tokens that merely await
        /// reactivation remain registered; defeated or otherwise removed tokens do not take part
        /// in later runtime map replacement.
        /// </summary>
        internal void DetachFromGrid(GridAPI grid)
        {
            if (!registered || registeredGrid != grid)
                return;

            registered = false;
            registeredGrid = null;
            detachedFromGrid = true;
        }
    }
}
