using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    internal static class DungeonSaveContractGuard
    {
        internal static string RequiredId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "A stable non-blank identifier is required.",
                    parameterName
                );
            return value;
        }

        internal static string Normalized(string value) => value ?? string.Empty;

        internal static string CanonicalJson(string value, string parameterName)
        {
            string normalized = Normalized(value);
            if (normalized.Length == 0)
                return normalized;
            try
            {
                return CanonicalToken(
                        JToken.Parse(
                            normalized,
                            new JsonLoadSettings
                            {
                                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                                LineInfoHandling = LineInfoHandling.Ignore,
                            }
                        )
                    )
                    .ToString(Formatting.None);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException(
                    "Effect state must be valid JSON.",
                    parameterName,
                    exception
                );
            }
        }

        internal static string CanonicalStaticFloorJson(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Static floor JSON is required.", parameterName);
            JObject root;
            try
            {
                root = JObject.Parse(
                    value,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    }
                );
            }
            catch (JsonException exception)
            {
                throw new ArgumentException(
                    "Static floor JSON is invalid.",
                    parameterName,
                    exception
                );
            }
            if (root.Property("runtimeState") != null)
            {
                throw new ArgumentException(
                    "Static floor JSON cannot contain mutable runtimeState.",
                    parameterName
                );
            }
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(value);
            if (!parsed.IsSuccess)
            {
                string message =
                    parsed.Diagnostics.Count > 0
                        ? parsed.Diagnostics[0].Message
                        : "The dungeon generator document is invalid.";
                throw new ArgumentException(
                    "Static floor JSON is invalid: " + message,
                    parameterName
                );
            }
            return DungeonLevelJsonSerializer.Serialize(parsed.Document);
        }

        private static JToken CanonicalToken(JToken source)
        {
            if (source is JObject sourceObject)
            {
                JObject result = new();
                foreach (
                    JProperty property in sourceObject
                        .Properties()
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                )
                {
                    result.Add(property.Name, CanonicalToken(property.Value));
                }
                return result;
            }
            if (source is JArray sourceArray)
                return new JArray(sourceArray.Select(CanonicalToken));
            return source.DeepClone();
        }

        internal static IReadOnlyList<T> UniqueSorted<T>(
            IEnumerable<T> source,
            Func<T, string> key,
            string parameterName
        ) => Sorted(source, key, parameterName, permitDuplicateKeys: false);

        internal static IReadOnlyList<T> Sorted<T>(
            IEnumerable<T> source,
            Func<T, string> key,
            string parameterName,
            bool permitDuplicateKeys
        )
        {
            if (source == null)
                throw new ArgumentNullException(parameterName);
            T[] copied = source.ToArray();
            RequiredElements(copied, parameterName);
            copied = copied.OrderBy(key, StringComparer.Ordinal).ToArray();
            if (!permitDuplicateKeys)
                RequireUnique(copied.Select(key), parameterName);
            return Array.AsReadOnly(copied);
        }

        internal static IReadOnlyList<string> UniqueStrings(
            IEnumerable<string> source,
            string parameterName
        )
        {
            if (source == null)
                throw new ArgumentNullException(parameterName);
            string[] copied = source.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (copied.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Stable identifiers cannot be blank.", parameterName);
            RequireUnique(copied, parameterName);
            return Array.AsReadOnly(copied);
        }

        internal static void RequiredElements<T>(IEnumerable<T> source, string parameterName)
        {
            if (source.Any(item => item == null))
                throw new ArgumentException(
                    "Collections cannot contain null entries.",
                    parameterName
                );
        }

        internal static void RequireUnique(IEnumerable<string> keys, string parameterName)
        {
            string[] copied = keys.ToArray();
            if (copied.Distinct(StringComparer.Ordinal).Count() != copied.Length)
                throw new ArgumentException("Stable identifiers must be unique.", parameterName);
        }
    }
}
