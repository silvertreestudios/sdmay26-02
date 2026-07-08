using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Indexes PF2e data items loaded from Resources so rules can resolve names, slugs, and Foundry UUID-style references.
    /// </summary>
    public sealed class Pf2eItemCatalog
    {
        private readonly Dictionary<string, Pf2eItem> bySlug = new();
        private readonly Dictionary<string, Pf2eItem> byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Pf2eItem> byUuid = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Pf2eItem> items = new();
        private static Pf2eItemCatalog instance;

        public IReadOnlyList<Pf2eItem> Items => items;

        public static Pf2eItemCatalog Instance => instance ??= LoadFromResources();

        /// <summary>
        /// Clears the Resources-backed singleton so tests can rebuild the catalog from controlled data.
        /// </summary>
        public static void ResetForTests()
        {
            instance = null;
        }

        /// <summary>
        /// Loads all parseable PF2e JSON files under Resources/DataFiles into a lookup catalog.
        /// </summary>
        /// <returns>A catalog containing every valid PF2e item found in Resources.</returns>
        public static Pf2eItemCatalog LoadFromResources()
        {
            Pf2eItemCatalog catalog = new();
            TextAsset[] assets = Resources.LoadAll<TextAsset>("DataFiles");
            foreach (TextAsset asset in assets)
            {
                if (Pf2eItem.TryParse(asset.name, asset.text, out Pf2eItem item))
                    catalog.Add(item);
            }
            return catalog;
        }

        /// <summary>
        /// Adds an item to every supported lookup index while preserving the first item for duplicate aliases.
        /// </summary>
        /// <param name="item">The parsed PF2e item to index.</param>
        public void Add(Pf2eItem item)
        {
            if (item == null)
                return;

            items.Add(item);
            Index(bySlug, item.Slug, item);
            Index(byName, item.Name, item);
            Index(byName, item.ResourceName, item);

            foreach (string uuid in GenerateUuidAliases(item))
                Index(byUuid, uuid, item);
        }

        /// <summary>
        /// Resolves a Foundry UUID, item name, resource name, or slug into a catalog item.
        /// </summary>
        /// <param name="reference">The reference value found in data or supplied by gameplay code.</param>
        /// <returns>The matching item, or null when the catalog cannot resolve it.</returns>
        public Pf2eItem Resolve(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            string trimmed = reference.Trim();
            if (byUuid.TryGetValue(trimmed, out Pf2eItem item))
                return item;

            string nameFromUuid = ExtractUuidItemName(trimmed);
            if (!string.IsNullOrWhiteSpace(nameFromUuid) && TryResolveByNameOrSlug(nameFromUuid, out item))
                return item;

            return TryResolveByNameOrSlug(trimmed, out item) ? item : null;
        }

        /// <summary>
        /// Resolves a human-readable item name or slug without requiring callers to know which form they have.
        /// </summary>
        /// <param name="value">The item name, resource name, or slug.</param>
        /// <param name="item">The resolved item when one exists.</param>
        /// <returns>True when the value resolves to a catalog item.</returns>
        public bool TryResolveByNameOrSlug(string value, out Pf2eItem item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (byName.TryGetValue(value, out item))
                return true;

            string slug = Pf2eSlug.FromName(value);
            return bySlug.TryGetValue(slug, out item);
        }

        private static void Index(Dictionary<string, Pf2eItem> dictionary, string key, Pf2eItem item)
        {
            if (!string.IsNullOrWhiteSpace(key) && !dictionary.ContainsKey(key))
                dictionary.Add(key, item);
        }

        private static IEnumerable<string> GenerateUuidAliases(Pf2eItem item)
        {
            string sourceId = item.Json.SelectToken("flags.core.sourceId")?.Value<string>()
                ?? item.Json.SelectToken("flags.pf2e.sourceId")?.Value<string>()
                ?? item.Json.SelectToken("system.source.value")?.Value<string>();
            if (!string.IsNullOrWhiteSpace(sourceId))
                yield return sourceId;

            string[] commonPacks =
            {
                "classes",
                "classfeatures",
                "actionspf2e",
                "feat-effects",
                "feats-srd",
                "conditionitems"
            };

            foreach (string pack in commonPacks)
                yield return $"Compendium.pf2e.{pack}.Item.{item.Name}";
        }

        private static string ExtractUuidItemName(string reference)
        {
            const string marker = ".Item.";
            int index = reference.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index < 0 ? null : reference.Substring(index + marker.Length);
        }
    }
}
