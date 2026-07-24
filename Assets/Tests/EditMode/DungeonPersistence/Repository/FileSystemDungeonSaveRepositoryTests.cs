using System;
using System.IO;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Repository;
using NUnit.Framework;

public sealed class FileSystemDungeonSaveRepositoryTests
{
    private string directory;

    [SetUp]
    public void SetUp()
    {
        directory = Path.GetFullPath(
            Path.Combine(".agent-temp", "repository-" + Guid.NewGuid().ToString("N"))
        );
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Test]
    public void SaveRoundTripsOneCurrentFloorJsonFile()
    {
        FileSystemDungeonSaveRepository repository = new(directory);

        DungeonSaveResult<bool> written = repository.Save(CreateSave(currentHitPoints: 8));
        DungeonSaveResult<DungeonRunSave> loaded = repository.Load();

        Assert.That(written.IsSuccess, Is.True);
        Assert.That(loaded.IsSuccess, Is.True);
        Assert.That(loaded.Value.Manifest.CurrentDepth, Is.EqualTo(0));
        Assert.That(loaded.Value.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(8));
        Assert.That(loaded.Value.FloorDocument.Rows[1], Is.EqualTo("#...#"));
        Assert.That(
            Directory.GetFiles(directory).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "autosave.json" })
        );
    }

    [Test]
    public void InterruptedReplacementPreservesPreviouslyCommittedSave()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateSave(currentHitPoints: 8)).IsSuccess, Is.True);
        string committed = File.ReadAllText(repository.AutosavePath);
        FileSystemDungeonSaveRepository interrupted = new(
            directory,
            (_, _) => throw new IOException("Simulated interrupted publish.")
        );

        DungeonSaveResult<bool> result = interrupted.Save(CreateSave(currentHitPoints: 3));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.IoFailure)
        );
        Assert.That(File.ReadAllText(repository.AutosavePath), Is.EqualTo(committed));
        Assert.That(File.Exists(repository.AutosavePath + ".tmp"), Is.False);
    }

    [Test]
    public void InvalidStagedJsonPreservesPreviouslyCommittedSave()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateSave(currentHitPoints: 8)).IsSuccess, Is.True);
        string committed = File.ReadAllText(repository.AutosavePath);
        FileSystemDungeonSaveRepository invalid = new(
            directory,
            staged: path => File.WriteAllText(path, "{}")
        );

        DungeonSaveResult<bool> result = invalid.Save(CreateSave(currentHitPoints: 3));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.CorruptSave)
        );
        Assert.That(File.ReadAllText(repository.AutosavePath), Is.EqualTo(committed));
    }

    [Test]
    public void LoadClassifiesUnsupportedManifestVersion()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateSave(currentHitPoints: 8)).IsSuccess, Is.True);
        string json = File.ReadAllText(repository.AutosavePath);
        string incompatible = json.Replace(
            "\"DocumentVersion\":1",
            "\"DocumentVersion\":99",
            StringComparison.Ordinal
        );
        Assert.That(incompatible, Is.Not.EqualTo(json));
        File.WriteAllText(repository.AutosavePath, incompatible);

        DungeonSaveResult<DungeonRunSave> result = repository.Load();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
        );
    }

    private static DungeonRunSave CreateSave(int currentHitPoints)
    {
        DungeonRuntimeState runtime = new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()
        );
        DungeonLevelDocument floor = new(
            new DungeonGenerationMetadata("test-generator", 123, 0, 0),
            new[] { "#####", "#...#", "#...#", "#...#", "#####" },
            new[] { new DungeonRoom(1, 1, 1, 3, 3) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>(),
            runtime
        );
        DungeonActorSaveState actor = new()
        {
            TemporaryHitPointSource = string.Empty,
            TemporaryHitPointImmunities = Array.Empty<string>(),
            Conditions = Array.Empty<DungeonConditionSaveState>(),
            TimedEffects = Array.Empty<DungeonTimedEffectSaveState>(),
            PreparedEffects = Array.Empty<DungeonPreparedEffectSaveState>(),
            Equipment = new DungeonEquipmentSaveState
            {
                LeftHandId = string.Empty,
                RightHandId = string.Empty,
                ArmorId = string.Empty,
                Ammunition = Array.Empty<AmmoCount>(),
                UnloadedWeaponIds = Array.Empty<string>(),
            },
        };
        DungeonRunSaveManifest manifest = new()
        {
            DocumentVersion = DungeonSaveSchema.Version,
            StartingSeed = 123,
            GeneratorVersion = "test-generator",
            CurrentDepth = 0,
            CurrentFloorVersion = DungeonSaveSchema.Version,
            CurrentFloorPath = DungeonSaveSchema.FloorPath,
            Party = new[]
            {
                new DungeonPartyMemberSaveState
                {
                    RosterSlotId = "party-slot",
                    CreatureContentId = "party-content",
                    CellX = 1,
                    CellZ = 1,
                    CurrentHitPoints = currentHitPoints,
                    IsDefeated = false,
                    State = actor,
                },
            },
        };
        return new DungeonRunSave(manifest, floor);
    }
}
