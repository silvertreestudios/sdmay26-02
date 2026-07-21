using System;
using System.Linq;
using Game.DungeonGeneration;
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
            DungeonSaveResult<DungeonRunSave> result = DungeonSaveJsonCodec.ParseRun(json);

            Assert.That(result.IsSuccess, Is.True);
            DungeonRunSave actual = result.Value;
            Assert.That(DungeonSaveJsonCodec.SerializeRun(actual), Is.EqualTo(json));
            Assert.That(actual.Floors.Select(floor => floor.Depth), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(
                actual.Manifest.GeneratedFloors.Select(floor => floor.RelativePath),
                Is.EqualTo(new[] { "floors/depth-0000.json", "floors/depth-0001.json" })
            );
            Assert.That(actual.Floors[0].DocumentJson, Is.EqualTo(expected.Floors[0].DocumentJson));

            DungeonCreatureSaveState leader = actual.Manifest.Party.Members[0].Creature;
            Assert.That(leader.Health.CurrentHitPoints, Is.EqualTo(18));
            Assert.That(leader.Health.TemporaryHitPoints, Is.EqualTo(3));
            Assert.That(leader.Health.TemporaryHitPointSourceId, Is.EqualTo("spell:shield"));
            Assert.That(
                leader.Conditions.Select(item => item.ApplicationId),
                Is.EqualTo(new[] { "condition-application-a", "condition-application-b" })
            );
            Assert.That(leader.TimedEffects[0].StateJson, Is.EqualTo("{}"));
            Assert.That(
                leader.PreparedRules.RollOptions,
                Is.EqualTo(new[] { "class:cleric", "self:effect:bless" })
            );
            Assert.That(leader.PreparedRules.SpellPools[0].RemainingUses, Is.Zero);
            Assert.That(leader.Equipment.Items[1].IsLoaded, Is.False);
            DungeonLevelDocument firstFloor = actual.Floors[0].ParseDocument();
            Assert.That(firstFloor.Doors[0].IsOpen, Is.True);
            Assert.That(
                firstFloor.RuntimeState.ResolvedEncounterIds,
                Does.Contain("encounter-cleared")
            );
            Assert.That(
                firstFloor.RuntimeState.DefeatedCreatureIds,
                Does.Contain(DungeonCreatureInstanceIdentity.Create("encounter-cleared", 0))
            );
            Assert.That(
                actual
                    .Floors[0]
                    .ParseDocument()
                    .RuntimeState.Creatures.Select(creature => creature.InstanceId),
                Is.EqualTo(
                    actual
                        .Floors[1]
                        .ParseDocument()
                        .RuntimeState.Creatures.Select(creature => creature.InstanceId)
                ),
                "Plan-derived enemy IDs are scoped by depth and may repeat across floors."
            );
        }

        [Test]
        public void RunDiagnosticsIdentifyIndexedFloorDocument()
        {
            JObject run = JObject.Parse(
                DungeonSaveJsonCodec.SerializeRun(DungeonSaveTestFactory.CreateRun())
            );
            ((JObject)((JArray)run["floors"])[1])["documentVersion"] = 99;

            DungeonSaveResult<DungeonRunSave> result = DungeonSaveJsonCodec.ParseRun(
                run.ToString()
            );

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
            );
            Assert.That(result.Diagnostics[0].Path, Is.EqualTo("run.floors[1].documentVersion"));
        }

        [Test]
        public void StandaloneCreatureTokenIsVersionedStrictAndDeterministic()
        {
            DungeonCreatureSaveState expected = DungeonSaveTestFactory
                .CreateRun()
                .Manifest.Party.Members[0]
                .Creature;

            string json = DungeonSaveJsonCodec.SerializeCreature(expected);
            DungeonSaveResult<DungeonCreatureSaveState> parsed = DungeonSaveJsonCodec.ParseCreature(
                json
            );

            Assert.That(parsed.IsSuccess, Is.True);
            DungeonCreatureSaveState actual = parsed.Value;
            Assert.That(DungeonSaveJsonCodec.SerializeCreature(actual), Is.EqualTo(json));

            JObject incompatible = JObject.Parse(json);
            incompatible["documentVersion"] = 99;
            DungeonSaveResult<DungeonCreatureSaveState> incompatibleResult =
                DungeonSaveJsonCodec.ParseCreature(incompatible.ToString());
            Assert.That(incompatibleResult.IsSuccess, Is.False);
            Assert.That(
                incompatibleResult.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
            );

            JObject unknown = JObject.Parse(json);
            ((JObject)unknown["creature"])["initiative"] = 12;
            DungeonSaveResult<DungeonCreatureSaveState> unknownResult =
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
