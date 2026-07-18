using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>Parses the project-owned, data-driven dungeon enemy catalog.</summary>
    public static class DungeonEncounterCatalogJson
    {
        /// <summary>Gets the schema identifier required in every catalog document.</summary>
        public const string Schema = "sdmay26-02/dungeon-encounter-catalog";

        /// <summary>Parses and strictly validates an enemy catalog.</summary>
        /// <param name="json">The complete JSON catalog text.</param>
        /// <returns>Unique candidates in their authored stable order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="FormatException">
        /// The JSON is malformed, has unknown fields, or contains invalid candidate metadata.
        /// </exception>
        public static IReadOnlyList<DungeonEncounterCandidate> Parse(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                throw new FormatException("The encounter catalog is not valid JSON.", exception);
            }

            EnsureOnlyProperties(root, "catalog", "schema", "enemies");
            string schema = RequireNonEmptyString(root, "schema", "catalog");
            if (!string.Equals(schema, Schema, StringComparison.Ordinal))
                throw new FormatException("The encounter catalog schema identifier is unsupported.");
            if (!(root["enemies"] is JArray enemies))
                throw new FormatException("The encounter catalog requires an enemies array.");

            List<DungeonEncounterCandidate> candidates = new();
            HashSet<string> ids = new(StringComparer.Ordinal);
            for (int index = 0; index < enemies.Count; index++)
            {
                if (!(enemies[index] is JObject enemy))
                    throw new FormatException($"enemies[{index}] must be an object.");
                EnsureOnlyProperties(
                    enemy,
                    $"enemies[{index}]",
                    "id",
                    "level",
                    "resourcePath",
                    "prefabPath");

                string path = $"enemies[{index}]";
                string id = RequireNonEmptyString(enemy, "id", path);
                string resourcePath = RequireNonEmptyString(enemy, "resourcePath", path);
                string prefabPath = RequireNonEmptyString(enemy, "prefabPath", path);
                if (!ids.Add(id))
                    throw new FormatException($"The enemy ID '{id}' is duplicated.");
                if (enemy["level"]?.Type != JTokenType.Integer)
                    throw new FormatException($"enemies[{index}].level must be an integer.");

                int level;
                try
                {
                    level = enemy.Value<int>("level");
                }
                catch (Exception exception) when (
                    exception is OverflowException ||
                    exception is InvalidCastException)
                {
                    throw new FormatException(
                        $"enemies[{index}].level must fit in a 32-bit integer.",
                        exception);
                }

                candidates.Add(new DungeonEncounterCandidate(
                    id,
                    level,
                    resourcePath,
                    prefabPath));
            }

            return Array.AsReadOnly(candidates.ToArray());
        }

        private static string RequireNonEmptyString(
            JObject source,
            string propertyName,
            string path)
        {
            JToken token = source[propertyName];
            if (token?.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                throw new FormatException($"{path}.{propertyName} must be a non-empty string.");
            return token.Value<string>();
        }

        private static void EnsureOnlyProperties(
            JObject source,
            string path,
            params string[] allowed)
        {
            HashSet<string> names = new(allowed, StringComparer.Ordinal);
            string unknown = source.Properties()
                .Select(property => property.Name)
                .FirstOrDefault(name => !names.Contains(name));
            if (unknown != null)
                throw new FormatException($"{path}.{unknown} is not a recognized field.");
        }
    }
}
