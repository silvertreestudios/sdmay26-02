using System.Linq;
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
            int documentVersion = RequiredInt(source, "documentVersion", path);
            RequireVersion(
                documentVersion,
                DungeonSaveSchema.RunManifestVersion,
                path + ".documentVersion"
            );
            return new DungeonRunSaveManifest(
                documentVersion,
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

        private static JObject FromParty(DungeonPartySaveState party) =>
            new()
            {
                ["leaderRosterSlotId"] = party.LeaderRosterSlotId,
                ["members"] = new JArray(
                    party.Members.Select(member => new JObject
                    {
                        ["rosterSlotId"] = member.RosterSlotId,
                        ["creature"] = FromCreature(member.Creature),
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
            ValidateProperties(source, path, "rosterSlotId", "creature");
            return new DungeonPartyMemberSaveState(
                RequiredString(source, "rosterSlotId", path),
                ReadCreature(RequiredObject(source, "creature", path), path + ".creature")
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

        internal static JObject FromFloor(DungeonFloorSaveState floor) =>
            new()
            {
                ["documentVersion"] = floor.DocumentVersion,
                ["depth"] = floor.Depth,
                ["staticFloorJson"] = floor.StaticFloorJson,
                ["doors"] = new JArray(
                    floor.Doors.Select(door => new JObject
                    {
                        ["doorId"] = door.DoorId,
                        ["isOpen"] = door.IsOpen,
                    })
                ),
                ["encounters"] = new JArray(
                    floor.Encounters.Select(encounter => new JObject
                    {
                        ["encounterId"] = encounter.EncounterId,
                        ["status"] = EncounterStatus(encounter.Status),
                    })
                ),
                ["creatures"] = new JArray(
                    floor.Creatures.Select(creature => new JObject
                    {
                        ["encounterId"] = creature.EncounterId,
                        ["creature"] = FromCreature(creature.Creature),
                    })
                ),
            };

        internal static DungeonFloorSaveState ReadFloor(JObject source) =>
            ReadFloor(source, "floor");

        private static DungeonFloorSaveState ReadFloor(JObject source, string path)
        {
            ValidateProperties(
                source,
                path,
                "documentVersion",
                "depth",
                "staticFloorJson",
                "doors",
                "encounters",
                "creatures"
            );
            int documentVersion = RequiredInt(source, "documentVersion", path);
            RequireVersion(
                documentVersion,
                DungeonSaveSchema.FloorStateVersion,
                path + ".documentVersion"
            );
            return new DungeonFloorSaveState(
                documentVersion,
                RequiredInt(source, "depth", path),
                RequiredString(source, "staticFloorJson", path),
                ReadObjects(RequiredArray(source, "doors", path), path + ".doors", ReadDoor),
                ReadObjects(
                    RequiredArray(source, "encounters", path),
                    path + ".encounters",
                    ReadEncounter
                ),
                ReadObjects(
                    RequiredArray(source, "creatures", path),
                    path + ".creatures",
                    ReadEncounterCreature
                )
            );
        }

        private static DungeonDoorSaveState ReadDoor(JObject source, string path)
        {
            ValidateProperties(source, path, "doorId", "isOpen");
            return new DungeonDoorSaveState(
                RequiredString(source, "doorId", path),
                RequiredBool(source, "isOpen", path)
            );
        }

        private static DungeonEncounterSaveState ReadEncounter(JObject source, string path)
        {
            ValidateProperties(source, path, "encounterId", "status");
            return new DungeonEncounterSaveState(
                RequiredString(source, "encounterId", path),
                ReadEncounterStatus(RequiredString(source, "status", path), path + ".status")
            );
        }

        private static DungeonEncounterCreatureSaveState ReadEncounterCreature(
            JObject source,
            string path
        )
        {
            ValidateProperties(source, path, "encounterId", "creature");
            return new DungeonEncounterCreatureSaveState(
                RequiredString(source, "encounterId", path),
                ReadCreature(RequiredObject(source, "creature", path), path + ".creature")
            );
        }
    }
}
