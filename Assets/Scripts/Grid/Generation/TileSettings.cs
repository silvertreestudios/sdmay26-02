using System;
using System.Collections.Generic;
using UnityEngine;

namespace GridPrivate
{
    /// <summary>
    /// A static container of Tile functions
    /// </summary>
    [System.Serializable]
    public class TileSettings
    {
        [System.Serializable]
        protected struct TileDefinition
        {
            public Color32 Color;
            public TileType Tile;
            public GameObject Prefab;
            public Material Floor;
        }

        /// <summary>
        /// List of colors in image and corresponding
        /// TileType and Prefab
        /// </summary>
        [SerializeField]
        protected List<TileDefinition> TileDefinitions;

        // Constructed once for fast access, Dictionary is non Serializeable
        protected Dictionary<Color32, (TileType, GameObject, Material)> FastAccess = null;

        /// <summary>
        /// Returns the TileType of a given pixel color
        /// </summary>
        /// <param name="pixel"></param>
        /// <returns>(Empty, null) if undefined</returns>
        public (TileType, GameObject, Material) GetTileInfo(Color32 pixel)
        {
            return GetInfo(pixel);
        }

        public bool TryGetTileInfo(
            Color32 pixel,
            out (TileType Tile, GameObject Prefab, Material Floor) tileInfo)
        {
            BuildCache();
            pixel.a = 255;
            if (FastAccess.TryGetValue(pixel, out var value))
            {
                tileInfo = (value.Item1, value.Item2, value.Item3);
                return true;
            }

            tileInfo = (TileType.Empty, null, null);
            return false;
        }

        public TileType GetTileType(Color32 pixel)
        {
            return GetInfo(pixel).Item1;
        }

        public void ResetCache()
        {
            FastAccess = null;
        }

        protected (TileType, GameObject, Material) GetInfo(Color32 color)
        {
            if (TryGetTileInfo(color, out var output))
                return output;
            Debug.LogError("Undefined color used in tile image: " + color + "\nPlease define color in TileSettings on MapGenerator");
            return (TileType.Empty, null, null);
        }

        private void BuildCache()
        {
            if (FastAccess != null)
                return;

            if (TileDefinitions == null)
                return;

            FastAccess = new();

            for (int i = 0; i < TileDefinitions.Count; i++)
            {
                TileDefinition definition = TileDefinitions[i];
                definition.Color.a = 255;
                FastAccess.Add(
                    definition.Color,
                    (definition.Tile, definition.Prefab, definition.Floor));
            }
        }
    }

    public enum TileType
    {
        Empty,
        Ground,
        Wall,
        Door,
        Obstacle
    }

}
