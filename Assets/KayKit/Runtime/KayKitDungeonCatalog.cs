using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.KayKit
{
    [Serializable]
    public sealed class KayKitDungeonCatalogEntry
    {
        [SerializeField] private string id;
        [SerializeField] private GameObject model;

        public string Id => id;
        public GameObject Model => model;

        public KayKitDungeonCatalogEntry(string id, GameObject model)
        {
            this.id = id;
            this.model = model;
        }
    }

    [CreateAssetMenu(menuName = "KayKit/Dungeon Catalog", fileName = "KayKitDungeonCatalog")]
    public sealed class KayKitDungeonCatalog : ScriptableObject
    {
        [SerializeField] private List<KayKitDungeonCatalogEntry> entries = new();

        public IReadOnlyList<KayKitDungeonCatalogEntry> Entries => entries;

#if UNITY_EDITOR
        public void ReplaceEntries(IEnumerable<KayKitDungeonCatalogEntry> replacement)
        {
            entries = new List<KayKitDungeonCatalogEntry>(replacement);
        }
#endif
    }
}
