using System;
using UnityEngine;

namespace GridPrivate
{
    public enum WallVariant
    {
        Straight,
        Endcap,
        Corner,
        Crossing,
        Pillar,
        TIntersection,
    }

    public readonly struct WallResolution
    {
        public WallVariant Variant { get; }
        public int Rotation { get; }

        public WallResolution(WallVariant variant, int rotation)
        {
            Variant = variant;
            Rotation = rotation;
        }
    }

    public static class WallStructuralResolver
    {
        public static WallResolution Resolve(Vector3Int position, in TileType[,] gridData)
        {
            int x = position.x;
            int z = position.z;
            bool north = IsStructure(gridData, x, z + 1);
            bool south = IsStructure(gridData, x, z - 1);
            bool east = IsStructure(gridData, x + 1, z);
            bool west = IsStructure(gridData, x - 1, z);
            int count =
                Convert.ToInt32(north)
                + Convert.ToInt32(south)
                + Convert.ToInt32(east)
                + Convert.ToInt32(west);

            if (count == 4)
                return new WallResolution(WallVariant.Crossing, 0);

            if (count == 3)
            {
                int rotation =
                    !north ? 270
                    : !south ? 90
                    : !east ? 0
                    : 180;
                return new WallResolution(WallVariant.TIntersection, rotation);
            }

            if (count == 2)
            {
                if (north && south)
                    return new WallResolution(WallVariant.Straight, 90);
                if (east && west)
                    return new WallResolution(WallVariant.Straight, 0);

                int rotation =
                    north && west ? 0
                    : north && east ? 90
                    : south && west ? 270
                    : 180;
                return new WallResolution(WallVariant.Corner, rotation);
            }

            if (count == 1)
            {
                int rotation =
                    east ? 0
                    : west ? 180
                    : north ? 270
                    : 90;
                return new WallResolution(WallVariant.Endcap, rotation);
            }

            return new WallResolution(WallVariant.Pillar, 0);
        }

        public static bool IsStructure(TileType[,] gridData, int x, int z)
        {
            if (
                gridData == null
                || x < 0
                || z < 0
                || x >= gridData.GetLength(0)
                || z >= gridData.GetLength(1)
            )
            {
                return false;
            }

            TileType tile = gridData[x, z];
            return tile == TileType.Wall || tile == TileType.Door;
        }
    }

    public class Wall : MonoBehaviour, IOnGridGeneration
    {
        [SerializeField]
        protected Transform wall;

        [SerializeField]
        protected Transform cap;

        [SerializeField]
        protected Transform corner;

        [SerializeField]
        protected Transform crossIntersection;

        [SerializeField]
        protected Transform pillar;

        [SerializeField]
        protected Transform tIntersection;

        public WallVariant SelectedVariant { get; private set; }

        public void OnGeneration(Vector3Int position, in TileType[,] gridData)
        {
            WallResolution resolution = WallStructuralResolver.Resolve(position, gridData);
            SelectedVariant = resolution.Variant;
            transform.rotation = Quaternion.Euler(0f, resolution.Rotation, 0f);

            SetVisible(wall, resolution.Variant == WallVariant.Straight);
            SetVisible(cap, resolution.Variant == WallVariant.Endcap);
            SetVisible(corner, resolution.Variant == WallVariant.Corner);
            SetVisible(crossIntersection, resolution.Variant == WallVariant.Crossing);
            SetVisible(pillar, resolution.Variant == WallVariant.Pillar);
            SetVisible(tIntersection, resolution.Variant == WallVariant.TIntersection);
        }

        private static void SetVisible(Transform target, bool visible)
        {
            if (target == null)
                return;

            target.gameObject.SetActive(visible);
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = visible;
        }
    }
}
