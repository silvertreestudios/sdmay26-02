using System.Collections.Generic;
using UnityEngine;

namespace GridPrivate
{
    [RequireComponent(typeof(GridAPIPrivate))]
    public class GridInput : MonoBehaviour
    {
        protected Tile[,] Tiles;

        protected Vector3Int? Hover;

        /// <summary>
        /// The Camera to cast rays from
        /// </summary>
        protected Camera Camera;

        /// <summary>
        /// Returns the Camera to shoot rays from
        /// </summary>
        /// <returns></returns>
        protected Camera Cam() => Camera ? Camera : (Camera = Camera.main);

        private void Awake()
        {
            GridAPIPrivate grid = GetComponent<GridAPIPrivate>();
            Tiles = grid.GetTiles();
        }

        void Update()
        {
            if (HUDController.IsPointerOverHUD)
            {
                if (Hover.HasValue)
                {
                    OnHoverEnd.Invoke();
                    Hover = null;
                }
                return;
            }

            // Get camera
            var cam = Cam();
            if (!cam) return;

            // Build a ray from the mouse to find the hit on the grid plane.
            var ray = cam.ScreenPointToRay(InputCompat.MousePositionScreen());
            var plane = new UnityEngine.Plane(Vector3.up, transform.position);
            if (plane.Raycast(ray, out float t))
            {
                // World hit.
                Vector3 hit = ray.GetPoint(t);
                // Convert world to integer cell indices.
                Vector3Int cell = Vector3Int.RoundToInt(hit);
                if (
                    cell.x < Tiles.GetLength(0) && cell.x >= 0 &&
                    cell.z < Tiles.GetLength(1) && cell.z >= 0
                ) {
                    Hover = cell;
                    OnHover.Invoke(new List<Vector3Int> { cell });
                    return;
                }
            }
            OnHoverEnd.Invoke();
            Hover = null;
        }
    }
}