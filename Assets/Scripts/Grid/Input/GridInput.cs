using GridPrivate;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEditor.FilePathAttribute;

namespace GridPrivate
{
    [RequireComponent(typeof(Map))]
    public class GridInput : MonoBehaviour
    {
        // Hover fields
        [SerializeField]
        protected GameObject HoverPrefab;    // Prefab
        protected GameObjectPool HoverPool;  // Prefab acquisition
        protected List<GameObject> HoverList = new();// Used List

        // Range fields
        [SerializeField]
        protected GameObject RangePrefab;    // Prefab
        protected GameObjectPool RangePool;  // Prefab acquisition
        protected List<GameObject> RangeList = new();// Used List


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
            HoverPool = new(HoverPrefab);
            RangePool = new(RangePrefab);
            GridAPIPrivate grid = GetComponent<GridAPIPrivate>();
            Tiles = grid.GetTiles();

            OnHover.AddListener((List<Vector3Int> locations) =>
            {
                while(HoverList.Count > 0)
                {
                    int index = HoverList.Count - 1;
                    GameObject h = HoverList[index];
                    h.SetActive(false);
                    HoverList.RemoveAt(index);
                }
                foreach(Vector3Int location in locations)
                {
                    GameObject hover = HoverPool.GetObject();
                    HoverList.Add(hover);
                    hover.transform.position = new Vector3(location.x, location.y + 0.003f, location.z);
                }
            });
            OnHoverEnd.AddListener(() => {foreach (GameObject g in RangeList) g.SetActive(false); });
            OnHighlightRange.AddListener(ShowRange);
            OnCancelAction.AddListener(ClearRange);
        }

        void Update()
        {
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

        void ShowRange(List<Vector3Int> inRange)
        {
            foreach(Vector3Int tile in inRange)
            {
                GameObject g = RangePool.GetObject();
                RangeList.Add(g);
                g.transform.position = tile;
            }
        }

        void ClearRange()
        {
            foreach(GameObject g in RangeList)
            {
                g.SetActive(false);
            }
        }
    }
}