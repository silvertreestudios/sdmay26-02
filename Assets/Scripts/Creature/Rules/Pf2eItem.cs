using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Wraps one PF2e data item while preserving the original JSON as the rule-element source of truth.
    /// </summary>
    public sealed class Pf2eItem
    {
        public string Name { get; private set; }
        public string Slug { get; private set; }
        public string Type { get; private set; }
        public string ResourceName { get; private set; }
        public string Source { get; private set; }
        public JObject Json { get; private set; }
        public JObject System => Json["system"] as JObject;

        public IEnumerable<JObject> Rules
        {
            get
            {
                JArray rules = System?["rules"] as JArray;
                if (rules == null)
                    yield break;

                foreach (JToken rule in rules)
                    if (rule is JObject obj)
                        yield return obj;
            }
        }

        /// <summary>
        /// Parses a Resources text asset into a catalog item without mutating the upstream-style JSON shape.
        /// </summary>
        /// <param name="resourceName">The Unity Resources asset name used as a fallback lookup alias.</param>
        /// <param name="json">The raw PF2e item JSON.</param>
        /// <param name="item">The parsed catalog item when parsing succeeds.</param>
        /// <returns>True when the item has the minimum name and type data required by the rules pipeline.</returns>
        public static bool TryParse(string resourceName, string json, out Pf2eItem item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                JObject root = JObject.Parse(json);
                string name = root.Value<string>("name");
                string type = root.Value<string>("type");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                    return false;

                string slug = root.SelectToken("system.slug")?.Value<string>();
                item = new Pf2eItem
                {
                    Name = name,
                    Slug = string.IsNullOrWhiteSpace(slug) ? Pf2eSlug.FromName(name) : slug,
                    Type = type,
                    ResourceName = resourceName,
                    Source = root.Value<string>("Source") ?? root.SelectToken("system.publication.title")?.Value<string>() ?? string.Empty,
                    Json = root
                };
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PF2e catalog skipped malformed item '{resourceName}': {ex.Message}");
                return false;
            }
        }
    }
}
