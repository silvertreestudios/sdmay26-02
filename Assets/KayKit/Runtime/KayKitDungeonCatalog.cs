using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.KayKit
{
    [Serializable]
    public sealed class KayKitDungeonCatalogEntry
    {
        [SerializeField] private string id;
        [SerializeField] private GameObject model;
        [SerializeField] private GameObject wrapperPrefab;
        [SerializeField] private Vector2Int footprint = Vector2Int.one;
        [SerializeField] private int defaultRotation;
        [SerializeField] private float defaultYOffset;
        [SerializeField] private bool blocksMovement;
        [SerializeField] private bool blocksLineOfSight;

        public string Id => id;
        public GameObject Model => model;
        public GameObject WrapperPrefab => wrapperPrefab;
        public GameObject PlacementPrefab => wrapperPrefab != null ? wrapperPrefab : model;
        public Vector2Int Footprint => footprint;
        public int DefaultRotation => defaultRotation;
        public float DefaultYOffset => defaultYOffset;
        public bool BlocksMovement => blocksMovement;
        public bool BlocksLineOfSight => blocksLineOfSight;

        public KayKitDungeonCatalogEntry(string id, GameObject model)
            : this(id, model, null, Vector2Int.one, 0, 0f, false, false)
        {
        }

        public KayKitDungeonCatalogEntry(
            string id,
            GameObject model,
            GameObject wrapperPrefab,
            Vector2Int footprint,
            int defaultRotation,
            float defaultYOffset,
            bool blocksMovement,
            bool blocksLineOfSight)
        {
            this.id = id;
            this.model = model;
            this.wrapperPrefab = wrapperPrefab;
            this.footprint = footprint;
            this.defaultRotation = defaultRotation;
            this.defaultYOffset = defaultYOffset;
            this.blocksMovement = blocksMovement;
            this.blocksLineOfSight = blocksLineOfSight;
        }
    }

    [CreateAssetMenu(menuName = "KayKit/Dungeon Catalog", fileName = "KayKitDungeonCatalog")]
    public sealed class KayKitDungeonCatalog : ScriptableObject
    {
        [SerializeField] private List<KayKitDungeonCatalogEntry> entries = new();
        [SerializeField] private Material defaultMaterial;
        [SerializeField] private GameObject floorPrefab;
        [SerializeField] private GameObject wallPrefab;
        [SerializeField] private GameObject doorwayPrefab;

        private Dictionary<string, KayKitDungeonCatalogEntry> entriesById;

        public IReadOnlyList<KayKitDungeonCatalogEntry> Entries => entries;
        public Material DefaultMaterial => defaultMaterial;
        public GameObject FloorPrefab => floorPrefab;
        public GameObject WallPrefab => wallPrefab;
        public GameObject DoorwayPrefab => doorwayPrefab;

        public bool TryGet(string id, out KayKitDungeonCatalogEntry entry)
        {
            if (entriesById == null)
            {
                entriesById = entries
                    .Where(candidate => candidate != null && !string.IsNullOrWhiteSpace(candidate.Id))
                    .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            }

            return entriesById.TryGetValue(id ?? string.Empty, out entry);
        }

#if UNITY_EDITOR
        public void ReplaceEntries(IEnumerable<KayKitDungeonCatalogEntry> replacement)
        {
            entries = new List<KayKitDungeonCatalogEntry>(replacement);
            entriesById = null;
        }

        public void ConfigureStructure(
            Material material,
            GameObject floor,
            GameObject wall,
            GameObject doorway)
        {
            defaultMaterial = material;
            floorPrefab = floor;
            wallPrefab = wall;
            doorwayPrefab = doorway;
        }
#endif
    }
}
