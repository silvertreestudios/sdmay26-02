using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.KayKit
{
    [Serializable]
    public sealed class CreatureVisualCatalogEntry
    {
        [SerializeField]
        private string key;

        [SerializeField]
        private string visualId;

        [SerializeField]
        private string species;

        [SerializeField]
        private GameObject visualPrefab;

        public string Key => key;
        public string VisualId => visualId;
        public string Species => species;
        public GameObject VisualPrefab => visualPrefab;

        public CreatureVisualCatalogEntry(
            string key,
            string visualId,
            string species,
            GameObject visualPrefab
        )
        {
            this.key = key;
            this.visualId = visualId;
            this.species = species;
            this.visualPrefab = visualPrefab;
        }
    }

    [CreateAssetMenu(
        menuName = "KayKit/Creature Visual Catalog",
        fileName = "CreatureVisualCatalog"
    )]
    public sealed class CreatureVisualCatalog : ScriptableObject
    {
        [SerializeField]
        private List<CreatureVisualCatalogEntry> entries = new();

        public IReadOnlyList<CreatureVisualCatalogEntry> Entries => entries;

        public bool TryResolve(string key, out CreatureVisualCatalogEntry entry)
        {
            string normalized = NormalizeKey(key);
            foreach (CreatureVisualCatalogEntry candidate in entries)
            {
                if (
                    candidate != null
                    && NormalizeKey(candidate.Key) == normalized
                    && candidate.VisualPrefab != null
                )
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public static string NormalizeKey(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

#if UNITY_EDITOR
        public void ReplaceEntries(IEnumerable<CreatureVisualCatalogEntry> replacement)
        {
            entries =
                replacement == null
                    ? new List<CreatureVisualCatalogEntry>()
                    : new List<CreatureVisualCatalogEntry>(replacement);
        }
#endif
    }
}
