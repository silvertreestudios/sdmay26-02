using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    internal static partial class DungeonSaveJsonDocument
    {
        internal static void ValidateProperties(JObject source, string path, params string[] names)
        {
            HashSet<string> expected = new(names, StringComparer.Ordinal);
            string unexpected = source
                .Properties()
                .Select(property => property.Name)
                .FirstOrDefault(name => !expected.Contains(name));
            if (unexpected != null)
                throw new ArgumentException($"Unknown property '{path}.{unexpected}'.");
            string missing = names.FirstOrDefault(name => source.Property(name) == null);
            if (missing != null)
                throw new ArgumentException($"Required property '{path}.{missing}' is missing.");
        }

        internal static JObject RequiredObject(JObject source, string name, string path)
        {
            if (source[name] is not JObject result)
                throw new ArgumentException($"'{path}.{name}' must be an object.");
            return result;
        }

        internal static JArray RequiredArray(JObject source, string name, string path)
        {
            if (source[name] is not JArray result)
                throw new ArgumentException($"'{path}.{name}' must be an array.");
            return result;
        }

        internal static string RequiredString(JObject source, string name, string path)
        {
            JToken token = source[name];
            if (token == null || token.Type != JTokenType.String)
                throw new ArgumentException($"'{path}.{name}' must be a string.");
            return token.Value<string>();
        }

        internal static int RequiredInt(JObject source, string name, string path)
        {
            JToken token = source[name];
            if (token == null || token.Type != JTokenType.Integer)
                throw new ArgumentException($"'{path}.{name}' must be an integer.");
            return token.Value<int>();
        }

        internal static long RequiredLong(JObject source, string name, string path)
        {
            JToken token = source[name];
            if (token == null || token.Type != JTokenType.Integer)
                throw new ArgumentException($"'{path}.{name}' must be an integer.");
            return token.Value<long>();
        }

        internal static bool RequiredBool(JObject source, string name, string path)
        {
            JToken token = source[name];
            if (token == null || token.Type != JTokenType.Boolean)
                throw new ArgumentException($"'{path}.{name}' must be a boolean.");
            return token.Value<bool>();
        }

        internal static IReadOnlyList<T> ReadObjects<T>(
            JArray source,
            string path,
            Func<JObject, string, T> read
        )
        {
            List<T> result = new(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                if (source[index] is not JObject item)
                    throw new ArgumentException($"'{path}[{index}]' must be an object.");
                result.Add(read(item, $"{path}[{index}]"));
            }
            return result;
        }

        internal static IReadOnlyList<string> ReadStrings(JArray source, string path)
        {
            List<string> result = new(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                if (source[index].Type != JTokenType.String)
                    throw new ArgumentException($"'{path}[{index}]' must be a string.");
                result.Add(source[index].Value<string>());
            }
            return result;
        }

        internal static void RequireVersion(int actual, int expected, string path)
        {
            if (actual != expected)
            {
                throw new DungeonSaveJsonIncompatibleException(
                    path,
                    $"Document version {actual} is incompatible with required version {expected}."
                );
            }
        }

        internal static JObject FromCell(DungeonSaveCell cell) =>
            new() { ["x"] = cell.X, ["z"] = cell.Z };

        internal static DungeonSaveCell ReadCell(JObject source, string path)
        {
            ValidateProperties(source, path, "x", "z");
            return new DungeonSaveCell(
                RequiredInt(source, "x", path),
                RequiredInt(source, "z", path)
            );
        }

        internal static string Slot(DungeonEquipmentSlot slot) =>
            slot switch
            {
                DungeonEquipmentSlot.Carried => "carried",
                DungeonEquipmentSlot.Armor => "armor",
                DungeonEquipmentSlot.LeftHand => "leftHand",
                DungeonEquipmentSlot.RightHand => "rightHand",
                _ => throw new ArgumentOutOfRangeException(nameof(slot)),
            };

        internal static DungeonEquipmentSlot ReadSlot(string value, string path) =>
            value switch
            {
                "carried" => DungeonEquipmentSlot.Carried,
                "armor" => DungeonEquipmentSlot.Armor,
                "leftHand" => DungeonEquipmentSlot.LeftHand,
                "rightHand" => DungeonEquipmentSlot.RightHand,
                _ => throw new ArgumentException(
                    $"'{path}' has undefined equipment slot '{value}'."
                ),
            };

        internal static string EncounterStatus(DungeonEncounterSaveStatus status) =>
            status switch
            {
                DungeonEncounterSaveStatus.Dormant => "dormant",
                DungeonEncounterSaveStatus.Active => "active",
                DungeonEncounterSaveStatus.Suspended => "suspended",
                DungeonEncounterSaveStatus.Cleared => "cleared",
                _ => throw new ArgumentOutOfRangeException(nameof(status)),
            };

        internal static DungeonEncounterSaveStatus ReadEncounterStatus(string value, string path) =>
            value switch
            {
                "dormant" => DungeonEncounterSaveStatus.Dormant,
                "active" => DungeonEncounterSaveStatus.Active,
                "suspended" => DungeonEncounterSaveStatus.Suspended,
                "cleared" => DungeonEncounterSaveStatus.Cleared,
                _ => throw new ArgumentException(
                    $"'{path}' has undefined encounter status '{value}'."
                ),
            };
    }
}
