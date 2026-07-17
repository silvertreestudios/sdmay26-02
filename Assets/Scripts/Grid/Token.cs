using GridPrivate;
using GridPublic;
using UnityEngine;

namespace GridPublic
{
    public class Token : MonoBehaviour
    {
        private bool registered;
        private bool waitingForGrid;

        private void Awake()
        {
            if (isActiveAndEnabled)
                TryRegister();
        }

        private void OnEnable()
        {
            if (registered || TryRegister())
                return;

            if (!GridAPI.TryGetInstance(out _))
                Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private bool TryRegister()
        {
            return GridAPI.TryGetInstance(out GridAPI grid) && TryRegister(grid);
        }

        private bool TryRegister(GridAPI grid)
        {
            if (registered || !isActiveAndEnabled || grid is not GridAPIPrivate privateGrid)
                return registered;

            registered = privateGrid.AddToken(gameObject);
            return registered;
        }

        private void Subscribe()
        {
            if (waitingForGrid)
                return;

            GridAPI.Ready += OnGridReady;
            waitingForGrid = true;
        }

        private void Unsubscribe()
        {
            if (!waitingForGrid)
                return;

            GridAPI.Ready -= OnGridReady;
            waitingForGrid = false;
        }

        private void OnGridReady(GridAPI grid)
        {
            Unsubscribe();
            TryRegister(grid);
        }
    }
}
