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
            // Construct fast access if necessary
            if (FastAccess == null)
            {
                FastAccess = new();
                for (int i = 0; i < TileDefinitions.Count; i++)
                {
                    TileDefinition def = TileDefinitions[i];

                    // Only go off RBG values
                    def.Color.a = 255;
                    FastAccess.Add(def.Color, (def.Tile, def.Prefab, def.Floor));
                }
            }

            (TileType, GameObject, Material) output;

            Color32 rgb_pixel = color;
            rgb_pixel.a = 255;
            if (FastAccess.TryGetValue(rgb_pixel, out output))
                return output;
            Debug.LogError("Undefined color used in tile image: " + color + "\nPlease define color in TileSettings on MapGenerator");
            return (TileType.Empty, null, null);
        }
    }

    public enum TileType
    {
        Empty,
        Ground,
        Wall,
        Door
    }

}
