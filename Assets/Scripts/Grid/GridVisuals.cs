using System.Collections.Generic;
using UnityEngine;
using static UnityEditor.FilePathAttribute;

namespace GridPrivate
{
    [RequireComponent(typeof(GridAPIPrivate))]
    public class GridVisuals : MonoBehaviour
    {
        // Hover fields
        [SerializeField]
        protected GameObject HoverPrefab;    // Prefab
        protected GameObjectPool HoverPool;  // Prefab acquisition/storage

        // Range fields
        [SerializeField]
        protected GameObject RangePrefab;    // Prefab
        protected GameObjectPool RangePool;  // Prefab acquisition/storage

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
            OnCancelAction.AddListener(() => RangePool.Clear());
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
    }
}