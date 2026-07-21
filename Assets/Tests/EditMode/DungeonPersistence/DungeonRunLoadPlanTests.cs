using System.Linq;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Repository;
using NUnit.Framework;
using Tests.EditMode.DungeonPersistence.Repository;

public sealed class DungeonRunLoadPlanTests
{
    [Test]
    public void PrepareValidatesAndProjectsCurrentFloorBeforeSceneMutation()
    {
        DungeonRunSave source = WithSupportedPartyState(DungeonSaveTestFactory.CreateRun());

        DungeonSaveResult<DungeonRunLoadPlan> result = DungeonRunLoadPlan.Prepare(source);

        Assert.That(result.IsSuccess, Is.True);
        DungeonRunLoadPlan plan = result.Value;
        Assert.That(plan.CurrentFloor.Depth, Is.EqualTo(source.Manifest.CurrentDepth));
        Assert.That(plan.PopulationDocument.RuntimeState, Is.Not.Null);
        Assert.That(plan.PopulationDocument.RuntimeState.OpenDoorIds, Is.EqualTo(new[] { "door" }));
        Assert.That(
            plan.PopulationDocument.RuntimeState.ResolvedEncounterIds,
            Is.EqualTo(new[] { "encounter-cleared" })
        );
        Assert.That(plan.PopulationDocument.RuntimeState.Creatures, Has.Count.EqualTo(1));
        Assert.That(
            plan.PopulationDocument.RuntimeState.Creatures[0].InstanceId,
            Is.EqualTo("encounter-active/creature-0000")
        );
    }

    [Test]
    public void PrepareRejectsUnsupportedActorCodecBeforePopulationDocumentIsExposed()
    {
        DungeonRunSave source = WithUnsupportedPartyState(DungeonSaveTestFactory.CreateRun());

        DungeonSaveResult<DungeonRunLoadPlan> result = DungeonRunLoadPlan.Prepare(source);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Select(item => item.Code),
            Does.Contain(DungeonSaveDiagnosticCode.InvalidSnapshot)
        );
        Assert.That(result.Diagnostics[0].Message, Does.Contain("unsupported kind").IgnoreCase);
    }

    private static DungeonRunSave WithSupportedPartyState(DungeonRunSave source)
    {
        DungeonCreatureSaveState oldLeader = source.Manifest.Party.Members[0].Creature;
        DungeonCreatureSaveState leader = DungeonSaveTestFactory.CreateCreature(
            oldLeader.InstanceId,
            oldLeader.CreatureContentId,
            oldLeader.Cell,
            oldLeader.Health.CurrentHitPoints,
            oldLeader.Health.MaximumHitPoints,
            richState: false
        );
        DungeonPartySaveState party = new(
            source.Manifest.Party.LeaderRosterSlotId,
            new[]
            {
                new DungeonPartyMemberSaveState(
                    source.Manifest.Party.Members[0].RosterSlotId,
                    leader
                ),
                source.Manifest.Party.Members[1],
            }
        );
        DungeonRunSaveManifest manifest = new(
            source.Manifest.DocumentVersion,
            source.Manifest.StartingSeed,
            source.Manifest.GeneratorVersion,
            source.Manifest.CurrentDepth,
            party,
            source.Manifest.GeneratedFloors
        );
        return new DungeonRunSave(manifest, source.Floors);
    }

    private static DungeonRunSave WithUnsupportedPartyState(DungeonRunSave source)
    {
        DungeonCreatureSaveState oldLeader = source.Manifest.Party.Members[0].Creature;
        DungeonCreatureSaveState invalidLeader = new(
            oldLeader.InstanceId,
            oldLeader.CreatureContentId,
            oldLeader.Cell,
            oldLeader.Health,
            oldLeader.IsDefeated,
            oldLeader.Conditions,
            new[]
            {
                new DungeonTimedEffectSaveState(
                    "unsupported-effect",
                    "unsupported-kind",
                    "legacy-spell-effect/v1",
                    oldLeader.InstanceId,
                    oldLeader.InstanceId,
                    oldLeader.InstanceId,
                    1,
                    1,
                    "{}"
                ),
            },
            oldLeader.PreparedRules,
            oldLeader.Equipment
        );
        DungeonPartySaveState party = new(
            source.Manifest.Party.LeaderRosterSlotId,
            new[]
            {
                new DungeonPartyMemberSaveState(
                    source.Manifest.Party.Members[0].RosterSlotId,
                    invalidLeader
                ),
                source.Manifest.Party.Members[1],
            }
        );
        DungeonRunSaveManifest manifest = new(
            source.Manifest.DocumentVersion,
            source.Manifest.StartingSeed,
            source.Manifest.GeneratorVersion,
            source.Manifest.CurrentDepth,
            party,
            source.Manifest.GeneratedFloors
        );
        return new DungeonRunSave(manifest, source.Floors);
    }
}
