using System;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Repository;

namespace Tests.EditMode.DungeonPersistence.Repository
{
    internal static class DungeonSaveTestFactory
    {
        internal const int Seed = 155;
        internal const string GeneratorVersion = "test-generator";

        internal static DungeonRunSave CreateRun(
            int leaderHitPoints = 18,
            bool firstDoorOpen = true
        )
        {
            DungeonPartySaveState party = new(
                "roster-leader",
                new[]
                {
                    new DungeonPartyMemberSaveState(
                        "roster-leader",
                        CreateCreature(
                            "party-0000",
                            "cleric",
                            new DungeonSaveCell(1, 1),
                            leaderHitPoints,
                            24,
                            richState: true
                        )
                    ),
                    new DungeonPartyMemberSaveState(
                        "roster-scout",
                        CreateCreature(
                            "party-0001",
                            "rogue",
                            new DungeonSaveCell(2, 1),
                            16,
                            20,
                            richState: false
                        )
                    ),
                }
            );
            DungeonRunSaveManifest manifest = new(
                DungeonSaveSchema.RunManifestVersion,
                Seed,
                GeneratorVersion,
                0,
                party,
                new[] { DungeonFloorSaveReference.Current(1), DungeonFloorSaveReference.Current(0) }
            );
            return new DungeonRunSave(
                manifest,
                new[] { CreateFloor(1, false), CreateFloor(0, firstDoorOpen) }
            );
        }

        internal static DungeonFloorSaveState CreateFloor(int depth, bool doorOpen)
        {
            string activeId = EncounterId(depth, "active");
            string clearedId = EncounterId(depth, "cleared");
            return new DungeonFloorSaveState(
                DungeonSaveSchema.FloorStateVersion,
                depth,
                CreateStaticFloorJson(depth),
                new[] { new DungeonDoorSaveState(DoorId(depth), doorOpen) },
                new[]
                {
                    new DungeonEncounterSaveState(
                        activeId,
                        depth == 0
                            ? DungeonEncounterSaveStatus.Active
                            : DungeonEncounterSaveStatus.Suspended
                    ),
                    new DungeonEncounterSaveState(clearedId, DungeonEncounterSaveStatus.Cleared),
                },
                new[]
                {
                    new DungeonEncounterCreatureSaveState(
                        activeId,
                        CreateCreature(
                            DungeonCreatureInstanceIdentity.Create(activeId, 0),
                            "kobold-warrior",
                            new DungeonSaveCell(2, 2),
                            7,
                            12,
                            richState: false
                        )
                    ),
                    new DungeonEncounterCreatureSaveState(
                        clearedId,
                        CreateCreature(
                            DungeonCreatureInstanceIdentity.Create(clearedId, 0),
                            "kobold-scout",
                            new DungeonSaveCell(6, 2),
                            0,
                            10,
                            richState: false
                        )
                    ),
                }
            );
        }

        internal static DungeonCreatureSaveState CreateCreature(
            string instanceId,
            string contentId,
            DungeonSaveCell cell,
            int currentHitPoints,
            int maximumHitPoints,
            bool richState
        )
        {
            DungeonConditionSaveState[] conditions = richState
                ? new[]
                {
                    new DungeonConditionSaveState(
                        "condition-application-b",
                        "frightened",
                        "source-dragon",
                        1
                    ),
                    new DungeonConditionSaveState(
                        "condition-application-a",
                        "frightened",
                        "source-dragon",
                        1
                    ),
                }
                : Array.Empty<DungeonConditionSaveState>();
            DungeonTimedEffectSaveState[] effects = richState
                ? new[]
                {
                    new DungeonTimedEffectSaveState(
                        "timed-bless",
                        "aura",
                        "bless-state-v1",
                        "party-0000",
                        "party-0000",
                        instanceId,
                        3,
                        2,
                        "{\"z\":2,\"a\":1}"
                    ),
                }
                : Array.Empty<DungeonTimedEffectSaveState>();
            DungeonPreparedRuleSaveState prepared = richState
                ? new DungeonPreparedRuleSaveState(
                    new[] { "self:effect:bless", "class:cleric" },
                    new[]
                    {
                        new DungeonPreparedEffectSaveState(
                            "prepared-bless",
                            "Bless",
                            "bless",
                            "spell-bless"
                        ),
                    },
                    new[] { new DungeonSpellPoolSaveState("rank-1-bless", 0, 1) }
                )
                : new DungeonPreparedRuleSaveState(
                    Array.Empty<string>(),
                    Array.Empty<DungeonPreparedEffectSaveState>(),
                    Array.Empty<DungeonSpellPoolSaveState>()
                );
            DungeonEquipmentSaveState equipment = richState
                ? new DungeonEquipmentSaveState(
                    new[]
                    {
                        new DungeonInventoryItemSaveState(
                            "item-crossbow",
                            "crossbow",
                            1,
                            DungeonEquipmentSlot.RightHand,
                            false
                        ),
                        new DungeonInventoryItemSaveState(
                            "item-armor",
                            "chain-mail",
                            1,
                            DungeonEquipmentSlot.Armor,
                            true
                        ),
                    },
                    new[] { new DungeonAmmunitionSaveState("bolt", 7) }
                )
                : new DungeonEquipmentSaveState(
                    Array.Empty<DungeonInventoryItemSaveState>(),
                    Array.Empty<DungeonAmmunitionSaveState>()
                );
            return new DungeonCreatureSaveState(
                instanceId,
                contentId,
                cell,
                new DungeonHealthSaveState(
                    currentHitPoints,
                    maximumHitPoints,
                    richState ? 3 : 0,
                    richState ? "spell:shield" : string.Empty,
                    richState ? new[] { "spell:shield" } : Array.Empty<string>()
                ),
                currentHitPoints == 0,
                conditions,
                effects,
                prepared,
                equipment
            );
        }

        private static string CreateStaticFloorJson(int depth)
        {
            DungeonLevelDocument document = new(
                new DungeonGenerationMetadata(GeneratorVersion, Seed, depth, 0),
                new[] { "#########", "#...#...#", "#...D...#", "#...#...#", "#########" },
                new[] { new DungeonRoom(1, 1, 1, 3, 3), new DungeonRoom(2, 5, 1, 7, 3) },
                new[] { new DungeonDoor(DoorId(depth), new DungeonCell(4, 2)) },
                Array.Empty<DungeonStair>(),
                new DungeonCell(2, 2),
                new[] { new DungeonCell(2, 2), new DungeonCell(6, 2) },
                Array.Empty<DungeonObjectPlacement>(),
                new[]
                {
                    new DungeonEncounterPlan(
                        EncounterId(depth, "active"),
                        1,
                        DungeonEncounterThreat.Trivial,
                        20,
                        new[] { new DungeonCell(2, 2) },
                        new[] { "kobold-warrior" }
                    ),
                    new DungeonEncounterPlan(
                        EncounterId(depth, "cleared"),
                        2,
                        DungeonEncounterThreat.Trivial,
                        20,
                        new[] { new DungeonCell(6, 2) },
                        new[] { "kobold-scout" }
                    ),
                }
            );
            return DungeonLevelJsonSerializer.Serialize(document);
        }

        private static string DoorId(int depth) => "door";

        private static string EncounterId(int depth, string suffix) => $"encounter-{suffix}";
    }
}
