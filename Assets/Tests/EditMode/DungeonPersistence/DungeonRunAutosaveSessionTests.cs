using System;
using System.IO;
using System.Linq;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;
using NUnit.Framework;

public sealed class DungeonRunAutosaveSessionTests
{
    private string testRoot;

    [SetUp]
    public void SetUp()
    {
        testRoot = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".agent-temp",
            "ds-s",
            Guid.NewGuid().ToString("N")
        );
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testRoot))
            Directory.Delete(testRoot, recursive: true);
        else if (File.Exists(testRoot))
            File.Delete(testRoot);
    }

    [Test]
    public void CommitCurrentFloorPublishesAllDepthsAndAdvancesOnlyAfterSuccess()
    {
        const int seed = 24159;
        DungeonLevelDocument depthZero = Generate(seed, depth: 0);
        DungeonLevelDocument depthOne = Generate(seed, depth: 1);
        FileSystemDungeonSaveRepository repository = new(testRoot);
        DungeonRunAutosaveSession session = DungeonRunAutosaveSession.CreateNew(
            seed,
            depthZero.Generation.Algorithm,
            repository
        );

        DungeonSaveWriteResult first = session.CommitCurrentFloor(Capture(depthZero));
        DungeonSaveWriteResult second = session.CommitCurrentFloor(Capture(depthOne));

        Assert.That(first.IsSuccess, Is.True, Diagnostics(first));
        Assert.That(second.IsSuccess, Is.True, Diagnostics(second));
        Assert.That(session.HasCommittedSave, Is.True);
        Assert.That(session.CommittedSave.Manifest.CurrentDepth, Is.EqualTo(1));
        Assert.That(
            session.CommittedSave.Floors.Select(floor => floor.Depth),
            Is.EqualTo(new[] { 0, 1 })
        );
        DungeonSaveLoadResult loaded = repository.Load();
        Assert.That(loaded, Is.TypeOf<DungeonSaveLoadSuccess>());
        DungeonRunSave save = ((DungeonSaveLoadSuccess)loaded).Save;
        Assert.That(save.Manifest.CurrentDepth, Is.EqualTo(1));
        Assert.That(save.Floors.Select(floor => floor.Depth), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(
            save.Floors[0].StaticFloorJson,
            Is.EqualTo(DungeonLevelJsonSerializer.Serialize(depthZero))
        );
        Assert.That(
            save.Floors[1].StaticFloorJson,
            Is.EqualTo(DungeonLevelJsonSerializer.Serialize(depthOne))
        );
    }

    [Test]
    public void FailedLaterCommitKeepsSessionAndRepositoryOnPriorGeneration()
    {
        const int seed = 34159;
        DungeonLevelDocument depthZero = Generate(seed, depth: 0);
        DungeonLevelDocument depthOne = Generate(seed, depth: 1);
        FileSystemDungeonSaveRepository repository = new(testRoot);
        DungeonRunAutosaveSession session = DungeonRunAutosaveSession.CreateNew(
            seed,
            depthZero.Generation.Algorithm,
            repository
        );
        Assert.That(session.CommitCurrentFloor(Capture(depthZero)).IsSuccess, Is.True);
        string staging = Path.Combine(testRoot, ".staging");
        Directory.Delete(staging, recursive: true);
        File.WriteAllText(staging, "block staging directory creation");

        DungeonSaveWriteResult failed = session.CommitCurrentFloor(Capture(depthOne));

        Assert.That(failed.IsSuccess, Is.False);
        Assert.That(
            failed.Diagnostics.Select(item => item.Code),
            Does.Contain(DungeonSaveDiagnosticCode.IoFailure)
        );
        Assert.That(session.CommittedSave.Manifest.CurrentDepth, Is.EqualTo(0));
        DungeonSaveLoadResult loaded = repository.Load();
        Assert.That(loaded, Is.TypeOf<DungeonSaveLoadSuccess>());
        Assert.That(((DungeonSaveLoadSuccess)loaded).Save.Manifest.CurrentDepth, Is.EqualTo(0));
    }

    private static DungeonLevelDocument Generate(int seed, int depth)
    {
        DungeonGenerationResult generated = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = seed,
                Depth = depth,
                Width = 31,
                Height = 31,
                MinimumRoomCount = 0,
                StairCount = 2,
            }
        );
        Assert.That(
            generated.IsSuccess,
            Is.True,
            string.Join(" ", generated.Diagnostics.Select(item => item.Message))
        );
        return generated.Document;
    }

    private static DungeonCurrentFloorCapture Capture(DungeonLevelDocument document)
    {
        DungeonCreatureSaveState actor = new(
            "party-actor-1",
            "party-content-1",
            new DungeonSaveCell(document.StartCell.X, document.StartCell.Z),
            new DungeonHealthSaveState(10, 10, 0, string.Empty, Array.Empty<string>()),
            false,
            Array.Empty<DungeonConditionSaveState>(),
            Array.Empty<DungeonTimedEffectSaveState>(),
            new DungeonPreparedRuleSaveState(
                Array.Empty<string>(),
                Array.Empty<DungeonPreparedEffectSaveState>(),
                Array.Empty<DungeonSpellPoolSaveState>()
            ),
            new DungeonEquipmentSaveState(
                Array.Empty<DungeonInventoryItemSaveState>(),
                Array.Empty<DungeonAmmunitionSaveState>()
            )
        );
        DungeonPartySaveState party = new(
            "roster-1",
            new[] { new DungeonPartyMemberSaveState("roster-1", actor) }
        );
        DungeonFloorSaveState floor = new(
            DungeonSaveSchema.FloorStateVersion,
            document.Generation.Depth,
            DungeonLevelJsonSerializer.Serialize(document),
            document.Doors.Select(door => new DungeonDoorSaveState(door.Id, false)),
            document.EncounterPlans.Select(plan => new DungeonEncounterSaveState(
                plan.Id,
                DungeonEncounterSaveStatus.Dormant
            )),
            Array.Empty<DungeonEncounterCreatureSaveState>()
        );
        return new DungeonCurrentFloorCapture(party, floor);
    }

    private static string Diagnostics(DungeonSaveWriteResult result) =>
        string.Join(" ", result.Diagnostics.Select(item => item.Message));
}
