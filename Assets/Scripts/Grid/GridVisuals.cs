using System.Collections.Generic;
using UnityEngine;

namespace GridPrivate
{
    [RequireComponent(typeof(GridAPIPrivate))]
    public class GridVisuals : MonoBehaviour
    {
        [Header("Hover")]
        [SerializeField]
        protected GameObject HoverPrefab;    // Prefab
        protected GameObjectPool HoverPool;  // Prefab acquisition/storage

        [Header("Range")]
        [SerializeField]
        protected GameObject RangePrefab;    // Prefab
        protected GameObjectPool RangePool;  // Prefab acquisition/storage

        [Header("Lines")]
        [SerializeField]
        protected Material LineMaterial;
        [SerializeField]
        protected float LineWidth;
        [SerializeField]
        protected Color LineColor;
        protected LineRenderer LineRenderer;

        // Tiles
        protected Tile[,] Tiles;
        protected delegate bool TileFilter(Tile tile);
        TileFilter Filter = delegate (Tile tile) {return true;};

        protected Vector3Int? Hover;

        protected float HoverOffset = 0.03f;
        protected float RangeOffset = 0.02f;

        private void Awake()
        {
            HoverPool = new(HoverPrefab);
            RangePool = new(RangePrefab);
            GridAPIPrivate grid = GetComponent<GridAPIPrivate>();
            Tiles = grid.GetTiles();

            OnHover.AddListener((List<Vector3Int> locations) => ClearAndShowFiltered(locations, HoverPool, HoverOffset));
            OnHoverEnd.AddListener(() => HoverPool.Clear());
            OnHighlightRange.AddListener((List<Vector3Int> locations) => ClearAndShow(locations, RangePool, RangeOffset));
            OnHighlightRangeEnd.AddListener(() => RangePool.Clear());
            OnActionCancel.AddListener(() => RangePool.Clear());

            LineRenderer = this.gameObject.AddComponent<LineRenderer>();
            LineRenderer.material = (LineMaterial != null)?
                LineMaterial:
                new Material(Shader.Find("Unlit/Color"));
            LineRenderer.startWidth = LineRenderer.endWidth = LineWidth;
            LineRenderer.material.color = LineColor;
            LineRenderer.positionCount = 0;
            LineRenderer.useWorldSpace = true;

            OnPreviewPath.AddListener(ShowPath);
        }

        protected void Show(List<Vector3Int> locations, GameObjectPool pool, float offset)
        {
            foreach (Vector3Int location in locations)
            {
                GameObject go = pool.GetObject();
                go.transform.position = new Vector3(location.x, location.y + offset, location.z);
            }
        }

        protected void ClearAndShow(List<Vector3Int> locations, GameObjectPool pool, float offset)
        {
            pool.Clear();
            foreach (Vector3Int location in locations)
            {
                GameObject go = pool.GetObject();
                go.transform.position = new Vector3(location.x, location.y + offset, location.z);
            }
        }

        protected void ClearAndShowFiltered(List<Vector3Int> locations, GameObjectPool pool, float offset)
        {
            pool.Clear();
            foreach (Vector3Int location in locations)
            {
                if (Filter(Tiles[location.x, location.z]))
                {
                    GameObject go = pool.GetObject();
                    go.transform.position = new Vector3(location.x, location.y + offset, location.z);
                }
            }
        }

        /// <summary>
        /// Shows a path preview
        /// </summary>
        /// <param name="path">Path to preview (must have at least 2 points)</param>
        public void ShowPath(List<Vector3Int> path)
        {
            if (path == null)
            {
                LineRenderer.positionCount = 0;
                return;
            }
            LineRenderer.positionCount = path.Count;

            // Convert grid cells to world positions
            for (int i = 0; i < path.Count; i++)
            {
                LineRenderer.SetPosition(i, new Vector3(path[i].x, 0.1f, path[i].z));
            }
        }
    }
}