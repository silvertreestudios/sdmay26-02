using System;
using System.Linq;
using Game.DungeonPersistence.Repository;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Tests.EditMode.DungeonPersistence.Repository
{
    [TestFixture]
    public sealed class DungeonSaveJsonCodecTests
    {
        [Test]
        public void RunRoundTripPreservesAllMeaningfulStateAcrossFloors()
        {
            DungeonRunSave expected = DungeonSaveTestFactory.CreateRun();

            string json = DungeonSaveJsonCodec.SerializeRun(expected);
            DungeonSaveParseResult<DungeonRunSave> result = DungeonSaveJsonCodec.ParseRun(json);

            Assert.That(result, Is.TypeOf<DungeonSaveParseSuccess<DungeonRunSave>>());
            DungeonRunSave actual = ((DungeonSaveParseSuccess<DungeonRunSave>)result).Value;
            Assert.That(DungeonSaveJsonCodec.SerializeRun(actual), Is.EqualTo(json));
            Assert.That(actual.Floors.Select(floor => floor.Depth), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(
                actual.Manifest.GeneratedFloors.Select(floor => floor.RelativePath),
                Is.EqualTo(new[] { "floors/depth-0000.json", "floors/depth-0001.json" })
            );
            Assert.That(
                actual.Floors[0].StaticFloorJson,
                Is.EqualTo(expected.Floors[0].StaticFloorJson)
            );

            DungeonCreatureSaveState leader = actual.Manifest.Party.Members[0].Creature;
            Assert.That(leader.Health.CurrentHitPoints, Is.EqualTo(18));
            Assert.That(leader.Health.TemporaryHitPoints, Is.EqualTo(3));
            Assert.That(leader.Health.TemporaryHitPointSourceId, Is.EqualTo("spell:shield"));
            Assert.That(
                leader.Conditions.Select(item => item.ApplicationId),
                Is.EqualTo(new[] { "condition-application-a", "condition-application-b" })
            );
            Assert.That(leader.TimedEffects[0].StateJson, Is.EqualTo("{\"a\":1,\"z\":2}"));
            Assert.That(
                leader.PreparedRules.RollOptions,
                Is.EqualTo(new[] { "class:cleric", "self:effect:bless" })
            );
            Assert.That(leader.PreparedRules.SpellPools[0].RemainingUses, Is.Zero);
            Assert.That(leader.Equipment.Items[1].IsLoaded, Is.False);
            Assert.That(actual.Floors[0].Doors[0].IsOpen, Is.True);
            Assert.That(
                actual.Floors[0].Encounters[0].Status,
                Is.EqualTo(DungeonEncounterSaveStatus.Active)
            );
            Assert.That(actual.Floors[0].Creatures[1].Creature.IsDefeated, Is.True);
            Assert.That(
                actual.Floors[0].Creatures.Select(creature => creature.Creature.InstanceId),
                Is.EqualTo(
                    actual.Floors[1].Creatures.Select(creature => creature.Creature.InstanceId)
                ),
                "Plan-derived enemy IDs are scoped by depth and may repeat across floors."
            );
        }

        [Test]
        public void StandaloneCreatureTokenIsVersionedStrictAndDeterministic()
        {
            DungeonCreatureSaveState expected = DungeonSaveTestFactory
                .CreateRun()
                .Manifest.Party.Members[0]
                .Creature;

            string json = DungeonSaveJsonCodec.SerializeCreature(expected);
            DungeonSaveParseResult<DungeonCreatureSaveState> parsed =
                DungeonSaveJsonCodec.ParseCreature(json);

            Assert.That(parsed, Is.TypeOf<DungeonSaveParseSuccess<DungeonCreatureSaveState>>());
            DungeonCreatureSaveState actual = (
                (DungeonSaveParseSuccess<DungeonCreatureSaveState>)parsed
            ).Value;
            Assert.That(DungeonSaveJsonCodec.SerializeCreature(actual), Is.EqualTo(json));

            JObject incompatible = JObject.Parse(json);
            incompatible["documentVersion"] = 99;
            DungeonSaveParseResult<DungeonCreatureSaveState> incompatibleResult =
                DungeonSaveJsonCodec.ParseCreature(incompatible.ToString());
            Assert.That(incompatibleResult.IsSuccess, Is.False);
            Assert.That(
                incompatibleResult.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
            );

            JObject unknown = JObject.Parse(json);
            ((JObject)unknown["creature"])["initiative"] = 12;
            DungeonSaveParseResult<DungeonCreatureSaveState> unknownResult =
                DungeonSaveJsonCodec.ParseCreature(unknown.ToString());
            Assert.That(unknownResult.IsSuccess, Is.False);
            Assert.That(
                unknownResult.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.CorruptSave)
            );
        }

        [Test]
        public void PartyLeaderMustBeLivingOrCanonicallyAbsentAfterTotalDefeat()
        {
            DungeonCreatureSaveState defeatedA = DungeonSaveTestFactory.CreateCreature(
                "party-defeated-a",
                "fighter",
                new DungeonSaveCell(1, 1),
                0,
                20,
                richState: false
            );
            DungeonCreatureSaveState defeatedB = DungeonSaveTestFactory.CreateCreature(
                "party-defeated-b",
                "wizard",
                new DungeonSaveCell(1, 1),
                0,
                16,
                richState: false
            );
            DungeonPartyMemberSaveState[] members =
            {
                new("slot-a", defeatedA),
                new("slot-b", defeatedB),
            };

            Assert.DoesNotThrow(() => new DungeonPartySaveState(string.Empty, members));
            Assert.Throws<ArgumentException>(() => new DungeonPartySaveState("slot-a", members));

            DungeonCreatureSaveState living = DungeonSaveTestFactory.CreateCreature(
                "party-living",
                "fighter",
                new DungeonSaveCell(2, 2),
                10,
                20,
                richState: false
            );
            Assert.Throws<ArgumentException>(() =>
                new DungeonPartySaveState(
                    "slot-a",
                    new[]
                    {
                        new DungeonPartyMemberSaveState("slot-a", defeatedA),
                        new DungeonPartyMemberSaveState("slot-living", living),
                    }
                )
            );
        }
    }
}
