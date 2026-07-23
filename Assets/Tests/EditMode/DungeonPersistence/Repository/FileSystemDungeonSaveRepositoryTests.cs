using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
    public void SavePublishesOnlyValidatedCurrentFloorArchive()
    {
        FileSystemDungeonSaveRepository repository = new(directory);

        DungeonSaveResult<bool> written = repository.Save(CreateSave(currentHitPoints: 8));
        DungeonSaveResult<DungeonRunSave> loaded = repository.Load();

        Assert.That(written.IsSuccess, Is.True);
        Assert.That(loaded.IsSuccess, Is.True);
        Assert.That(loaded.Value.Manifest.CurrentDepth, Is.EqualTo(0));
        Assert.That(loaded.Value.Manifest.Party.Members.Single().CurrentHitPoints, Is.EqualTo(8));
        Assert.That(
            Directory.GetFiles(directory).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "autosave.zip" })
        );
    }

    [Test]
    public void InterruptedReplacementPreservesPreviouslyCommittedArchive()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateSave(currentHitPoints: 8)).IsSuccess, Is.True);
        byte[] committed = File.ReadAllBytes(repository.AutosavePath);
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
        Assert.That(File.ReadAllBytes(repository.AutosavePath), Is.EqualTo(committed));
        Assert.That(File.Exists(repository.AutosavePath + ".tmp"), Is.False);
    }

    [Test]
    public void LoadRejectsUnknownManifestFields()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateSave(currentHitPoints: 8)).IsSuccess, Is.True);
        RewriteManifest(repository.AutosavePath, json => json.Insert(1, "\"unexpected\":true,"));

        DungeonSaveResult<DungeonRunSave> result = repository.Load();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.CorruptSave)
        );
    }

    [Test]
    public void LoadClassifiesUnsupportedManifestVersion()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateSave(currentHitPoints: 8)).IsSuccess, Is.True);
        RewriteManifest(
            repository.AutosavePath,
            json =>
                json.Replace(
                    "\"documentVersion\":1",
                    "\"documentVersion\":99",
                    StringComparison.Ordinal
                )
        );

        DungeonSaveResult<DungeonRunSave> result = repository.Load();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
        );
    }

    [Test]
    public void LoadClassifiesMissingManifestVersionAsCorrupt()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateSave(currentHitPoints: 8)).IsSuccess, Is.True);
        RewriteManifest(
            repository.AutosavePath,
            json => json.Replace("\"documentVersion\":1,", string.Empty, StringComparison.Ordinal)
        );

        DungeonSaveResult<DungeonRunSave> result = repository.Load();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.CorruptSave)
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
        DungeonActorSaveState actor = new(
            0,
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<DungeonConditionSaveState>(),
            Array.Empty<DungeonTimedEffectSaveState>(),
            Array.Empty<DungeonPreparedEffectSaveState>(),
            new DungeonEquipmentSaveState(
                DungeonEquipmentReference.Empty,
                DungeonEquipmentReference.Empty,
                DungeonEquipmentReference.Empty,
                Array.Empty<DungeonAmmunitionSaveState>(),
                Array.Empty<string>()
            )
        );
        DungeonRunSaveManifest manifest = new(
            DungeonSaveSchema.Version,
            123,
            "test-generator",
            0,
            new DungeonFloorSaveReference(DungeonSaveSchema.Version, DungeonSaveSchema.FloorPath),
            new DungeonPartySaveState(
                new[]
                {
                    new DungeonPartyMemberSaveState(
                        "party-slot",
                        "party-content",
                        1,
                        1,
                        currentHitPoints,
                        isDefeated: false,
                        actor
                    ),
                }
            )
        );
        return new DungeonRunSave(manifest, floor);
    }

    private static void RewriteManifest(string archivePath, Func<string, string> rewrite)
    {
        string temporaryPath = archivePath + ".rewrite";
        using (ZipArchive source = ZipFile.OpenRead(archivePath))
        using (ZipArchive target = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                ZipArchiveEntry replacement = target.CreateEntry(entry.FullName);
                using Stream input = entry.Open();
                using Stream output = replacement.Open();
                if (entry.FullName == DungeonSaveSchema.ManifestPath)
                {
                    using StreamReader reader = new(input, Encoding.UTF8);
                    using StreamWriter writer = new(
                        output,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    );
                    writer.Write(rewrite(reader.ReadToEnd()));
                }
                else
                {
                    input.CopyTo(output);
                }
            }
        }
        File.Delete(archivePath);
        File.Move(temporaryPath, archivePath);
    }
}
