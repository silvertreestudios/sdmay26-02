using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonPersistence.Repository
{
    internal static partial class DungeonSaveJsonDocument
    {
        internal static JObject FromRun(DungeonRunSave save) =>
            new()
            {
                ["manifest"] = FromManifest(save.Manifest),
                ["floors"] = new JArray(save.Floors.Select(FromFloor)),
            };

        internal static DungeonRunSave ReadRun(JObject source)
        {
            const string path = "run";
            ValidateProperties(source, path, "manifest", "floors");
            DungeonRunSave save = new(
                ReadManifest(RequiredObject(source, "manifest", path)),
                ReadObjects(RequiredArray(source, "floors", path), path + ".floors", ReadFloor)
            );
            DungeonRunSaveValidator.RequireValid(save);
            return save;
        }

        internal static JObject FromManifest(DungeonRunSaveManifest manifest) =>
            new()
            {
                ["documentVersion"] = manifest.DocumentVersion,
                ["startingSeed"] = manifest.StartingSeed,
                ["generatorVersion"] = manifest.GeneratorVersion,
                ["currentDepth"] = manifest.CurrentDepth,
                ["party"] = FromParty(manifest.Party),
                ["generatedFloors"] = new JArray(
                    manifest.GeneratedFloors.Select(reference => new JObject
                    {
                        ["depth"] = reference.Depth,
                        ["documentVersion"] = reference.DocumentVersion,
                        ["relativePath"] = reference.RelativePath,
                    })
                ),
            };

        internal static DungeonRunSaveManifest ReadManifest(JObject source)
        {
            const string path = "manifest";
            ValidateProperties(
                source,
                path,
                "documentVersion",
                "startingSeed",
                "generatorVersion",
                "currentDepth",
                "party",
                "generatedFloors"
            );
            int version = RequiredInt(source, "documentVersion", path);
            RequireVersion(
                version,
                DungeonSaveSchema.RunManifestVersion,
                path + ".documentVersion"
            );
            return new DungeonRunSaveManifest(
                version,
                RequiredInt(source, "startingSeed", path),
                RequiredString(source, "generatorVersion", path),
                RequiredInt(source, "currentDepth", path),
                ReadParty(RequiredObject(source, "party", path), path + ".party"),
                ReadObjects(
                    RequiredArray(source, "generatedFloors", path),
                    path + ".generatedFloors",
                    ReadFloorReference
                )
            );
        }

        internal static JObject FromFloor(DungeonFloorSaveState floor) =>
            new()
            {
                ["documentVersion"] = floor.DocumentVersion,
                ["depth"] = floor.Depth,
                ["document"] = JObject.Parse(floor.DocumentJson),
            };

        internal static DungeonFloorSaveState ReadFloor(JObject source) =>
            ReadFloor(source, "floor");

        private static DungeonFloorSaveState ReadFloor(JObject source, string path)
        {
            ValidateProperties(source, path, "documentVersion", "depth", "document");
            int version = RequiredInt(source, "documentVersion", path);
            RequireVersion(version, DungeonSaveSchema.FloorStateVersion, path + ".documentVersion");
            return new DungeonFloorSaveState(
                version,
                RequiredInt(source, "depth", path),
                RequiredObject(source, "document", path).ToString(Formatting.None)
            );
        }

        private static JObject FromParty(DungeonPartySaveState party) =>
            new()
            {
                ["leaderRosterSlotId"] = party.LeaderRosterSlotId,
                ["members"] = new JArray(
                    party.Members.Select(member => new JObject
                    {
                        ["rosterSlotId"] = member.RosterSlotId,
                        ["actorState"] = JObject.Parse(member.ActorStateJson),
                    })
                ),
            };

        private static DungeonPartySaveState ReadParty(JObject source, string path)
        {
            ValidateProperties(source, path, "leaderRosterSlotId", "members");
            return new DungeonPartySaveState(
                RequiredString(source, "leaderRosterSlotId", path),
                ReadObjects(
                    RequiredArray(source, "members", path),
                    path + ".members",
                    ReadPartyMember
                )
            );
        }

        private static DungeonPartyMemberSaveState ReadPartyMember(JObject source, string path)
        {
            ValidateProperties(source, path, "rosterSlotId", "actorState");
            return new DungeonPartyMemberSaveState(
                RequiredString(source, "rosterSlotId", path),
                RequiredObject(source, "actorState", path).ToString(Formatting.None)
            );
        }

        private static DungeonFloorSaveReference ReadFloorReference(JObject source, string path)
        {
            ValidateProperties(source, path, "depth", "documentVersion", "relativePath");
            return new DungeonFloorSaveReference(
                RequiredInt(source, "depth", path),
                RequiredInt(source, "documentVersion", path),
                RequiredString(source, "relativePath", path)
            );
        }

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
    }
}
