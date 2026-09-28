using System;
using System.IO;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;
using Game.Rules.Runtime;
using NUnit.Framework;
using UnityEngine;

public sealed class FileSystemDungeonSaveRepositoryTests
{
    [Serializable]
    private sealed class SaveFile
    {
        public DungeonRunSaveManifest Manifest;
        public DungeonFloorSavePayload[] Floors;
    }

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
    public void SaveRoundTripsThreeDepthsWithIndependentRuntimeAndActorState()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        DungeonRunSave save = CreateRun(0, 2, 5).WithSelectedFloor(2, Party(8));

        DungeonSaveResult<bool> written = repository.Save(save);
        DungeonSaveResult<DungeonRunSave> loaded = repository.Load();

        Assert.That(written.IsSuccess, Is.True);
        Assert.That(loaded.IsSuccess, Is.True);
        Assert.That(loaded.Value.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(
            loaded.Value.Manifest.Floors.Select(reference => reference.Depth),
            Is.EqualTo(new[] { 0, 2, 5 })
        );
        Assert.That(
            loaded.Value.Manifest.Floors.Select(reference => reference.Path),
            Is.EqualTo(new[] { "floors/0.json", "floors/2.json", "floors/5.json" })
        );

        foreach (int depth in new[] { 0, 2, 5 })
        {
            DungeonLevelDocument floor = loaded.Value.GetFloor(depth);
            Assert.That(floor.Doors.Single().IsOpen, Is.EqualTo(depth % 2 == 0));
            Assert.That(
                floor.RuntimeState.DefeatedCreatureIds.Single(),
                Is.EqualTo(InstanceId(depth % 2))
            );
            DungeonCreatureRuntimeState creature = floor.RuntimeState.Creatures.Single();
            Assert.That(creature.HitPoints, Is.EqualTo(10 + depth));
            DungeonSaveResult<DungeonActorSaveState> actor = DungeonSaveJson.ParseActor(
                creature.State
            );
            Assert.That(actor.IsSuccess, Is.True);
            Assert.That(actor.Value.TemporaryHitPoints, Is.EqualTo(depth + 1));
            Assert.That(actor.Value.TemporaryHitPointSource, Is.EqualTo("floor-" + depth));
        }
        Assert.That(
            Directory.GetFiles(directory).Select(Path.GetFileName),
            Is.EquivalentTo(new[] { "autosave.json" })
        );
    }

    [Test]
    public void CurrentCheckpointReplacesOnlyTheSelectedFloorPayload()
    {
        DungeonRunSave original = CreateRun(0, 2, 5).WithSelectedFloor(2, Party(8));
        string[] originalPayloads = original.FloorPayloads.Select(item => item.FloorJson).ToArray();

        DungeonRunSave checkpoint = original.WithCurrentCheckpoint(
            Party(4),
            CreateFloor(2, actorTemporaryHitPoints: 42)
        );

        Assert.That(checkpoint.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(checkpoint.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(4));
        Assert.That(checkpoint.FloorPayloads[0].FloorJson, Is.EqualTo(originalPayloads[0]));
        Assert.That(checkpoint.FloorPayloads[1].FloorJson, Is.Not.EqualTo(originalPayloads[1]));
        Assert.That(checkpoint.FloorPayloads[2].FloorJson, Is.EqualTo(originalPayloads[2]));
        Assert.That(
            DungeonSaveJson
                .ParseActor(checkpoint.GetFloor(2).RuntimeState.Creatures.Single().State)
                .Value.TemporaryHitPoints,
            Is.EqualTo(42)
        );
    }

    [Test]
    public void SelectAndAddUpdateOnlyGlobalPartyCurrentDepthAndFloorIndex()
    {
        DungeonRunSave original = CreateRun(0, 5);
        string[] originalPayloads = original.FloorPayloads.Select(item => item.FloorJson).ToArray();

        DungeonRunSave selected = original.WithSelectedFloor(0, Party(7));
        DungeonRunSave added = selected.WithAddedAndSelectedFloor(
            Party(3),
            CreateFloor(2, actorTemporaryHitPoints: 22)
        );

        Assert.That(selected.Manifest.CurrentDepth, Is.EqualTo(0));
        Assert.That(selected.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(7));
        Assert.That(
            selected.FloorPayloads.Select(item => item.FloorJson),
            Is.EqualTo(originalPayloads)
        );
        Assert.That(added.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(added.Manifest.Party.Single().CurrentHitPoints, Is.EqualTo(3));
        Assert.That(
            added.Manifest.Floors.Select(reference => reference.Depth),
            Is.EqualTo(new[] { 0, 2, 5 })
        );
        Assert.That(added.FloorPayloads[0].FloorJson, Is.EqualTo(originalPayloads[0]));
        Assert.That(added.FloorPayloads[2].FloorJson, Is.EqualTo(originalPayloads[1]));
    }

    [Test]
    public void SerializationOrdersSparseDepthsAndPayloadsDeterministically()
    {
        DungeonRunSave save = DungeonRunSave
            .CreateNew(Party(12), CreateFloor(0))
            .WithAddedAndSelectedFloor(Party(11), CreateFloor(5))
            .WithAddedAndSelectedFloor(Party(10), CreateFloor(2));

        string first = DungeonSaveJson.Serialize(save);
        string second = DungeonSaveJson.Serialize(save);
        DungeonSaveResult<DungeonRunSave> parsed = DungeonSaveJson.Parse(first, "memory");

        Assert.That(first, Is.EqualTo(second));
        Assert.That(parsed.IsSuccess, Is.True);
        Assert.That(
            parsed.Value.Manifest.Floors.Select(reference => reference.Depth),
            Is.EqualTo(new[] { 0, 2, 5 })
        );
        Assert.That(
            parsed.Value.FloorPayloads.Select(payload => payload.Path),
            Is.EqualTo(new[] { "floors/0.json", "floors/2.json", "floors/5.json" })
        );
    }

    [Test]
    public void ConstructorRejectsDuplicateUnorderedAndUnindexedDepths()
    {
        DungeonRunSave valid = CreateRun(0, 2);
        DungeonRunSaveManifest duplicate = valid.Manifest;
        duplicate.Floors[1].Depth = 0;
        duplicate.Floors[1].Path = DungeonSaveSchema.FloorPath(0);
        DungeonFloorSavePayload[] duplicatePayloads = valid.FloorPayloads.ToArray();
        duplicatePayloads[1].Path = DungeonSaveSchema.FloorPath(0);
        DungeonRunSaveManifest unindexed = valid.Manifest;
        unindexed.CurrentDepth = 9;

        Assert.That(
            () => new DungeonRunSave(duplicate, duplicatePayloads),
            Throws.ArgumentException
        );
        Assert.That(
            () => new DungeonRunSave(valid.Manifest, valid.FloorPayloads.Take(1)),
            Throws.ArgumentException
        );
        Assert.That(
            () => new DungeonRunSave(unindexed, valid.FloorPayloads),
            Throws.ArgumentException
        );
    }

    [Test]
    public void ConstructorRejectsMismatchedNoncanonicalAndCorruptPayloads()
    {
        DungeonRunSave valid = CreateRun(0, 2);
        DungeonFloorSavePayload[] mismatched = valid.FloorPayloads.ToArray();
        mismatched[1].Path = "floors/02.json";
        DungeonFloorSavePayload[] corrupt = valid.FloorPayloads.ToArray();
        corrupt[1].FloorJson = "{}";
        DungeonFloorSavePayload[] wrongMetadata = valid.FloorPayloads.ToArray();
        wrongMetadata[1].FloorJson = DungeonLevelJsonSerializer.Serialize(
            CreateFloor(2, runSeed: 999)
        );
        DungeonFloorSavePayload[] invalidActor = valid.FloorPayloads.ToArray();
        invalidActor[1].FloorJson = DungeonLevelJsonSerializer.Serialize(
            CreateFloor(2, embeddedActorState: "{}")
        );

        Assert.That(() => new DungeonRunSave(valid.Manifest, mismatched), Throws.ArgumentException);
        Assert.That(() => new DungeonRunSave(valid.Manifest, corrupt), Throws.ArgumentException);
        Assert.That(
            () => new DungeonRunSave(valid.Manifest, wrongMetadata),
            Throws.ArgumentException
        );
        Assert.That(
            () => new DungeonRunSave(valid.Manifest, invalidActor),
            Throws.ArgumentException
        );
    }

    [Test]
    public void LoadAllowsRunGlobalPartyAndFloorLocalEnemyToShareLocalText()
    {
        DungeonRunSave valid = DungeonRunSave
            .CreateNew(Party(12), CreateFloor(0))
            .WithAddedAndSelectedFloor(Party(10), CreateFloor(2, encounterId: "encounter-2"));
        DungeonRunSaveManifest manifest = valid.Manifest;
        manifest.Party[0].RosterSlotId = InstanceId("encounter-1", 1);

        DungeonSaveResult<DungeonRunSave> result = ParseCandidate(
            manifest,
            valid.FloorPayloads.ToArray()
        );

        Assert.That(valid.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(result.IsSuccess, Is.True, result.Diagnostics.FirstOrDefault()?.Message);
    }

    [Test]
    public void LoadRejectsInactiveFloorWithUnresolvedRulesEffectSource()
    {
        DungeonRunSave valid = CreateRun(0, 2);
        DungeonActorSaveState actor = ActorState(1, "floor-0");
        actor.RulesEffects = new[]
        {
            new DungeonRulesEffectSaveState
            {
                EffectId = "effect-1",
                BindingId = "binding-1",
                DefinitionId = RageActionDefinition.EffectDefinitionId.Value,
                SourceActor = DungeonRulesActorReference.Floor(0, "missing-actor"),
                BindingOwnerActor = DungeonRulesActorReference.Floor(
                    0,
                    InstanceId("encounter-1", 1)
                ),
                TimingSourceActor = DungeonRulesActorReference.Floor(
                    0,
                    InstanceId("encounter-1", 1)
                ),
                RuleSource = "rage",
                DurationKind = EffectDurationKind.Minutes,
                DurationAmount = 1,
                BindingEnabled = true,
                HasTiming = true,
                RemainingBoundaries = 1,
                StateKind = "rage",
                StatePayload = "{\"StartedByQuickTempered\":false}",
            },
        };
        DungeonFloorSavePayload[] payloads = valid.FloorPayloads.ToArray();
        payloads[0].FloorJson = DungeonLevelJsonSerializer.Serialize(
            CreateFloor(0, embeddedActorState: DungeonSaveJson.SerializeActor(actor))
        );

        DungeonSaveResult<DungeonRunSave> result = ParseCandidate(valid.Manifest, payloads);

        Assert.That(valid.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("unavailable"));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("floors/0.json"));
    }

    [Test]
    public void LoadAcceptsCurrentPartyEffectSourcedByCurrentFloorEnemyWithVisitedHistory()
    {
        DungeonRunSave valid = DungeonRunSave
            .CreateNew(Party(12), CreateFloor(0, encounterId: "older-encounter"))
            .WithAddedAndSelectedFloor(Party(10), CreateFloor(2, encounterId: "current-encounter"));
        DungeonRunSaveManifest manifest = valid.Manifest;
        manifest.Party[0].State.RulesEffects = new[]
        {
            new DungeonRulesEffectSaveState
            {
                EffectId = "current-floor-slowed-effect",
                BindingId = "current-floor-slowed-binding",
                DefinitionId = ConditionRules.DefinitionId.Value,
                SourceActor = DungeonRulesActorReference.Floor(
                    2,
                    InstanceId("current-encounter", 1)
                ),
                BindingOwnerActor = DungeonRulesActorReference.Party("party-slot"),
                RuleSource = "current-floor-enemy-slowed",
                DurationKind = EffectDurationKind.Indefinite,
                BindingEnabled = true,
                StateKind = "condition",
                StatePayload = "{\"Condition\":\"slowed\",\"Value\":1}",
            },
        };

        DungeonSaveResult<DungeonRunSave> result = ParseCandidate(
            manifest,
            valid.FloorPayloads.ToArray()
        );

        Assert.That(valid.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(result.IsSuccess, Is.True, result.Diagnostics.FirstOrDefault()?.Message);
        Assert.That(result.Value.Manifest.Party[0].State.RulesEffects, Has.Length.EqualTo(1));
    }

    [Test]
    public void LoadRejectsOutdatedManifestAndUnsupportedFloorDocumentVersions()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Assert.That(repository.Save(CreateRun(0, 2)).IsSuccess, Is.True);
        string json = File.ReadAllText(repository.AutosavePath);
        string outdated = json.Replace(
            "\"DocumentVersion\":4",
            "\"DocumentVersion\":2",
            StringComparison.Ordinal
        );
        File.WriteAllText(repository.AutosavePath, outdated);

        DungeonSaveResult<DungeonRunSave> manifestResult = repository.Load();

        Assert.That(manifestResult.IsSuccess, Is.False);
        Assert.That(
            manifestResult.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
        );

        File.WriteAllText(repository.AutosavePath, json);
        string unsupportedFloor = json.Replace(
            "\"DocumentVersion\":1",
            "\"DocumentVersion\":99",
            StringComparison.Ordinal
        );
        Assert.That(unsupportedFloor, Is.Not.EqualTo(json));
        File.WriteAllText(repository.AutosavePath, unsupportedFloor);

        DungeonSaveResult<DungeonRunSave> floorResult = repository.Load();

        Assert.That(floorResult.IsSuccess, Is.False);
        Assert.That(
            floorResult.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
        );
    }

    [Test]
    public void InterruptedReplacementPreservesPreviouslyCommittedCurrentDepth()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        DungeonRunSave committedSave = CreateRun(0, 2).WithSelectedFloor(2, Party(8));
        Assert.That(repository.Save(committedSave).IsSuccess, Is.True);
        string committed = File.ReadAllText(repository.AutosavePath);
        FileSystemDungeonSaveRepository interrupted = new(
            directory,
            (_, _) => throw new IOException("Simulated interrupted publish.")
        );

        DungeonSaveResult<bool> result = interrupted.Save(
            committedSave.WithSelectedFloor(0, Party(3))
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.IoFailure)
        );
        Assert.That(File.ReadAllText(repository.AutosavePath), Is.EqualTo(committed));
        Assert.That(repository.Load().Value.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(File.Exists(repository.AutosavePath + ".tmp"), Is.False);
    }

    [Test]
    public void CheckpointRestoresExactExistingOrMissingRepositoryContents()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        Directory.CreateDirectory(directory);
        const string priorContents = "unparsed prior autosave bytes";
        File.WriteAllText(repository.AutosavePath, priorContents);
        DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> existing =
            repository.CaptureCheckpoint();
        Assert.That(existing.IsSuccess, Is.True, FormatDiagnostics(existing.Diagnostics));
        DungeonSaveResult<bool> savedOverExisting = repository.Save(CreateRun(0));
        Assert.That(
            savedOverExisting.IsSuccess,
            Is.True,
            FormatDiagnostics(savedOverExisting.Diagnostics)
        );

        DungeonSaveResult<bool> restoredExisting = repository.RestoreCheckpoint(existing.Value);

        Assert.That(
            restoredExisting.IsSuccess,
            Is.True,
            FormatDiagnostics(restoredExisting.Diagnostics)
        );
        Assert.That(File.ReadAllText(repository.AutosavePath), Is.EqualTo(priorContents));

        File.Delete(repository.AutosavePath);
        DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> missing =
            repository.CaptureCheckpoint();
        Assert.That(missing.IsSuccess, Is.True, FormatDiagnostics(missing.Diagnostics));
        DungeonSaveResult<bool> savedWhenMissing = repository.Save(CreateRun(0));
        Assert.That(
            savedWhenMissing.IsSuccess,
            Is.True,
            FormatDiagnostics(savedWhenMissing.Diagnostics)
        );

        DungeonSaveResult<bool> restoredMissing = repository.RestoreCheckpoint(missing.Value);

        Assert.That(
            restoredMissing.IsSuccess,
            Is.True,
            FormatDiagnostics(restoredMissing.Diagnostics)
        );
        Assert.That(File.Exists(repository.AutosavePath), Is.False);
    }

    /// <summary>Checks that staging preserves the old pathname contents until complete bytes publish.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void SavePublishesExactStagedBytes(bool destinationExists)
    {
        Directory.CreateDirectory(directory);
        string autosavePath = Path.Combine(directory, "autosave.json");
        byte[] prior = { 0, 255, 128, 13, 10 };
        if (destinationExists)
            File.WriteAllBytes(autosavePath, prior);
        byte[] stagedBytes = Array.Empty<byte>();
        int stagedCalls = 0;
        FileSystemDungeonSaveRepository repository = new(
            directory,
            staged: path =>
            {
                stagedCalls++;
                Assert.That(File.Exists(autosavePath), Is.EqualTo(destinationExists));
                if (destinationExists)
                    Assert.That(File.ReadAllBytes(autosavePath), Is.EqualTo(prior));
                stagedBytes = File.ReadAllBytes(path);
            }
        );

        DungeonSaveResult<bool> saved = repository.Save(
            CreateRun(0, 2).WithSelectedFloor(2, Party(8))
        );

        Assert.That(saved.IsSuccess, Is.True, FormatDiagnostics(saved.Diagnostics));
        Assert.That(stagedCalls, Is.EqualTo(1));
        Assert.That(stagedBytes, Is.Not.Empty);
        Assert.That(File.ReadAllBytes(autosavePath), Is.EqualTo(stagedBytes));
        DungeonSaveResult<DungeonRunSave> loaded = repository.Load();
        Assert.That(loaded.IsSuccess, Is.True, FormatDiagnostics(loaded.Diagnostics));
        Assert.That(loaded.Value.Manifest.CurrentDepth, Is.EqualTo(2));
        Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { autosavePath }));
    }

    /// <summary>Rollback restores opaque bytes, including invalid UTF-8, without parsing or re-encoding.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void CheckpointRestoresOpaqueBytesOverExistingOrMissingDestination(
        bool destinationExists
    )
    {
        Directory.CreateDirectory(directory);
        FileSystemDungeonSaveRepository repository = new(directory);
        byte[] prior = { 0, 255, 128, 13, 10, 239, 187, 191 };
        File.WriteAllBytes(repository.AutosavePath, prior);
        DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> checkpoint =
            repository.CaptureCheckpoint();
        Assert.That(checkpoint.IsSuccess, Is.True, FormatDiagnostics(checkpoint.Diagnostics));
        if (destinationExists)
            File.WriteAllBytes(repository.AutosavePath, new byte[] { 42, 43 });
        else
            File.Delete(repository.AutosavePath);

        DungeonSaveResult<bool> restored = repository.RestoreCheckpoint(checkpoint.Value);

        Assert.That(restored.IsSuccess, Is.True, FormatDiagnostics(restored.Diagnostics));
        Assert.That(File.ReadAllBytes(repository.AutosavePath), Is.EqualTo(prior));
        Assert.That(
            Directory.GetFiles(directory),
            Is.EquivalentTo(new[] { repository.AutosavePath })
        );
    }

    /// <summary>A failed rollback publication reports failure and leaves the current bytes intact.</summary>
    [Test]
    public void InterruptedRollbackPreservesCurrentBytesAndCleansStaging()
    {
        Directory.CreateDirectory(directory);
        byte[] prior = { 0, 255, 128 };
        byte[] current = { 10, 20, 30, 40 };
        int publishCalls = 0;
        FileSystemDungeonSaveRepository repository = new(
            directory,
            (source, destination) =>
            {
                publishCalls++;
                Assert.That(File.ReadAllBytes(source), Is.EqualTo(prior));
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(current));
                throw new IOException("Simulated interrupted rollback publication.");
            }
        );
        File.WriteAllBytes(repository.AutosavePath, prior);
        DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> checkpoint =
            repository.CaptureCheckpoint();
        Assert.That(checkpoint.IsSuccess, Is.True, FormatDiagnostics(checkpoint.Diagnostics));
        File.WriteAllBytes(repository.AutosavePath, current);

        DungeonSaveResult<bool> restored = repository.RestoreCheckpoint(checkpoint.Value);

        Assert.That(restored.IsSuccess, Is.False, FormatDiagnostics(restored.Diagnostics));
        Assert.That(
            restored.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.IoFailure)
        );
        Assert.That(publishCalls, Is.EqualTo(1));
        Assert.That(File.ReadAllBytes(repository.AutosavePath), Is.EqualTo(current));
        Assert.That(
            Directory.GetFiles(directory),
            Is.EquivalentTo(new[] { repository.AutosavePath })
        );
    }

    /// <summary>Windows sharing denial must fail Save and rollback without damaging committed bytes.</summary>
    [TestCase(false)]
    [TestCase(true)]
    [Platform(Include = "Win")]
    public void DestinationDenyingDeletePreservesCurrentBytes(bool restoreCheckpoint)
    {
        Directory.CreateDirectory(directory);
        FileSystemDungeonSaveRepository repository = new(directory);
        File.WriteAllBytes(repository.AutosavePath, new byte[] { 0, 255, 128 });
        DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> checkpoint =
            repository.CaptureCheckpoint();
        Assert.That(checkpoint.IsSuccess, Is.True, FormatDiagnostics(checkpoint.Diagnostics));
        byte[] current = { 10, 20, 30, 40 };
        File.WriteAllBytes(repository.AutosavePath, current);

        // Keep the conflicting handle alive throughout publication. No timing, external owner,
        // retry, or injected publisher is involved; reads remain available for byte assertions.
        using (
            FileStream held = new(
                repository.AutosavePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            )
        )
        {
            DungeonSaveResult<bool> result = restoreCheckpoint
                ? repository.RestoreCheckpoint(checkpoint.Value)
                : repository.Save(CreateRun(0));

            Assert.That(result.IsSuccess, Is.False, FormatDiagnostics(result.Diagnostics));
            Assert.That(
                result.Diagnostics.Single().Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.IoFailure)
            );
            Assert.That(result.Diagnostics.Single().Path, Is.EqualTo(repository.AutosavePath));
            Assert.That(File.ReadAllBytes(repository.AutosavePath), Is.EqualTo(current));
            Assert.That(
                Directory.GetFiles(directory),
                Is.EquivalentTo(new[] { repository.AutosavePath })
            );
        }
    }

    [Test]
    public void InvalidStagedEnvelopePreservesPreviouslyCommittedCurrentDepth()
    {
        FileSystemDungeonSaveRepository repository = new(directory);
        DungeonRunSave committedSave = CreateRun(0, 2).WithSelectedFloor(2, Party(8));
        Assert.That(repository.Save(committedSave).IsSuccess, Is.True);
        string committed = File.ReadAllText(repository.AutosavePath);
        FileSystemDungeonSaveRepository invalid = new(
            directory,
            staged: path => File.WriteAllText(path, "{}")
        );

        DungeonSaveResult<bool> result = invalid.Save(committedSave.WithSelectedFloor(0, Party(3)));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Diagnostics.Single().Code,
            Is.EqualTo(DungeonSaveDiagnosticCode.CorruptSave)
        );
        Assert.That(File.ReadAllText(repository.AutosavePath), Is.EqualTo(committed));
        Assert.That(repository.Load().Value.Manifest.CurrentDepth, Is.EqualTo(2));
    }

    /// <summary>Repeated publication and rollback must preserve every captured byte and missing state.</summary>
    [Test]
    public void RepeatedSaveAndRollbackPreserveExactCheckpoints()
    {
        FileSystemDungeonSaveRepository repository = new(
            Path.Combine(directory, "saves-\u00e9-\u65e5")
        );
        DungeonRunSave save = CreateRun(0, 2);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            bool existed = iteration % 3 != 0;
            byte[] prior = { 0, 255, 128, (byte)iteration, 13, 10 };
            Directory.CreateDirectory(Path.GetDirectoryName(repository.AutosavePath));
            if (existed)
                File.WriteAllBytes(repository.AutosavePath, prior);
            else
                File.Delete(repository.AutosavePath);

            DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> checkpoint =
                repository.CaptureCheckpoint();
            Assert.That(checkpoint.IsSuccess, Is.True, FormatDiagnostics(checkpoint.Diagnostics));
            DungeonSaveResult<bool> saved = repository.Save(save);
            Assert.That(
                saved.IsSuccess,
                Is.True,
                $"Save iteration {iteration}: {FormatDiagnostics(saved.Diagnostics)}"
            );
            Assert.That(
                File.ReadAllText(repository.AutosavePath),
                Is.EqualTo(DungeonSaveJson.Serialize(save))
            );
            DungeonSaveResult<bool> restored = repository.RestoreCheckpoint(checkpoint.Value);
            Assert.That(
                restored.IsSuccess,
                Is.True,
                $"Rollback iteration {iteration}: {FormatDiagnostics(restored.Diagnostics)}"
            );
            Assert.That(File.Exists(repository.AutosavePath), Is.EqualTo(existed));
            if (existed)
                Assert.That(File.ReadAllBytes(repository.AutosavePath), Is.EqualTo(prior));
            Assert.That(
                Directory.GetFiles(Path.GetDirectoryName(repository.AutosavePath)),
                Is.EquivalentTo(existed ? new[] { repository.AutosavePath } : Array.Empty<string>())
            );
        }
    }

    /// <summary>Windows rejects an open destination without changing bytes, then publishes after close.</summary>
    [TestCase(false)]
    [TestCase(true)]
    [Platform(Include = "Win")]
    public void PublicationWithOpenDestinationPreservesBytesAndSucceedsAfterClose(
        bool restoreCheckpoint
    )
    {
        Directory.CreateDirectory(directory);
        FileSystemDungeonSaveRepository repository = new(directory);
        byte[] captured = { 0, 255, 128 };
        byte[] current = { 10, 20, 30, 40 };
        File.WriteAllBytes(repository.AutosavePath, captured);
        DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> checkpoint =
            repository.CaptureCheckpoint();
        Assert.That(checkpoint.IsSuccess, Is.True, FormatDiagnostics(checkpoint.Diagnostics));
        File.WriteAllBytes(repository.AutosavePath, current);
        DungeonRunSave save = CreateRun(0);

        // Windows replacement rename rejects an open destination, including delete-sharing
        // readers. Atomicity requires keeping its committed bytes intact when that happens.
        using (
            FileStream reader = new(
                repository.AutosavePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            )
        )
        {
            DungeonSaveResult<bool> blocked = restoreCheckpoint
                ? repository.RestoreCheckpoint(checkpoint.Value)
                : repository.Save(save);
            Assert.That(blocked.IsSuccess, Is.False, FormatDiagnostics(blocked.Diagnostics));
            Assert.That(
                blocked.Diagnostics.Single().Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.IoFailure)
            );
            using MemoryStream oldContents = new();
            reader.CopyTo(oldContents);
            Assert.That(oldContents.ToArray(), Is.EqualTo(current));
            Assert.That(File.ReadAllBytes(repository.AutosavePath), Is.EqualTo(current));
            Assert.That(
                Directory.GetFiles(directory),
                Is.EquivalentTo(new[] { repository.AutosavePath })
            );
        }

        DungeonSaveResult<bool> result = restoreCheckpoint
            ? repository.RestoreCheckpoint(checkpoint.Value)
            : repository.Save(save);

        Assert.That(result.IsSuccess, Is.True, FormatDiagnostics(result.Diagnostics));
        byte[] expected = restoreCheckpoint
            ? captured
            : new System.Text.UTF8Encoding(false).GetBytes(DungeonSaveJson.Serialize(save));
        Assert.That(File.ReadAllBytes(repository.AutosavePath), Is.EqualTo(expected));
        Assert.That(
            Directory.GetFiles(directory),
            Is.EquivalentTo(new[] { repository.AutosavePath })
        );
    }

    private static DungeonRunSave CreateRun(params int[] depths)
    {
        DungeonRunSave save = DungeonRunSave.CreateNew(Party(12), CreateFloor(depths[0]));
        foreach (int depth in depths.Skip(1))
            save = save.WithAddedAndSelectedFloor(Party(12 - depth), CreateFloor(depth));
        return save;
    }

    private static DungeonSaveResult<DungeonRunSave> ParseCandidate(
        DungeonRunSaveManifest manifest,
        DungeonFloorSavePayload[] payloads
    ) =>
        DungeonSaveJson.Parse(
            JsonUtility.ToJson(new SaveFile { Manifest = manifest, Floors = payloads }),
            "memory"
        );

    private static DungeonPartyMemberSaveState[] Party(int currentHitPoints) =>
        new[]
        {
            new DungeonPartyMemberSaveState
            {
                RosterSlotId = "party-slot",
                CreatureContentId = "party-content",
                CellX = 1,
                CellZ = 1,
                CurrentHitPoints = currentHitPoints,
                IsDefeated = false,
                State = ActorState(0, string.Empty),
            },
        };

    private static DungeonLevelDocument CreateFloor(
        int depth,
        int actorTemporaryHitPoints = -1,
        int runSeed = 123,
        string embeddedActorState = null,
        string encounterId = "encounter-1"
    )
    {
        int defeatedIndex = depth % 2;
        int livingIndex = 1 - defeatedIndex;
        DungeonCell livingCell = livingIndex == 0 ? new DungeonCell(0, 1) : new DungeonCell(2, 1);
        bool doorOpen = depth % 2 == 0;
        int temporaryHitPoints = actorTemporaryHitPoints < 0 ? depth + 1 : actorTemporaryHitPoints;
        string actorJson =
            embeddedActorState
            ?? DungeonSaveJson.SerializeActor(ActorState(temporaryHitPoints, "floor-" + depth));
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata("test-generator", runSeed, depth, depth + 1),
            new[] { "#D#", "...", "###" },
            new[] { new DungeonRoom(1, 0, 1, 2, 1) },
            new[] { new DungeonDoor("door-0001", new DungeonCell(1, 2), doorOpen) },
            Array.Empty<DungeonStair>(),
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            new[]
            {
                new DungeonEncounterPlan(
                    encounterId,
                    1,
                    DungeonEncounterThreat.Low,
                    60,
                    new[] { new DungeonCell(0, 1), new DungeonCell(2, 1) },
                    new[] { "creature-a", "creature-b" }
                ),
            },
            new DungeonRuntimeState(
                doorOpen ? new[] { "door-0001" } : Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { InstanceId(encounterId, defeatedIndex) },
                new[]
                {
                    new DungeonCreatureRuntimeState(
                        InstanceId(encounterId, livingIndex),
                        livingIndex == 0 ? "creature-a" : "creature-b",
                        encounterId,
                        livingCell,
                        10 + depth,
                        actorJson
                    ),
                }
            )
        );
    }

    private static DungeonActorSaveState ActorState(
        int temporaryHitPoints,
        string temporaryHitPointSource
    ) =>
        new()
        {
            TemporaryHitPoints = temporaryHitPoints,
            TemporaryHitPointSource =
                temporaryHitPoints == 0 ? string.Empty : temporaryHitPointSource,
            TemporaryHitPointImmunities = Array.Empty<string>(),
            RulesEffects = Array.Empty<DungeonRulesEffectSaveState>(),
            PreparedEffects = Array.Empty<DungeonPreparedEffectSaveState>(),
            SpellSlots = Array.Empty<DungeonSpellSlotSaveState>(),
            Equipment = new DungeonEquipmentSaveState
            {
                LeftHandId = string.Empty,
                RightHandId = string.Empty,
                ArmorId = string.Empty,
                Ammunition = Array.Empty<AmmoCount>(),
                UnloadedWeaponIds = Array.Empty<string>(),
            },
        };

    private static string InstanceId(int index) => $"encounter-1/creature-{index:0000}";

    private static string InstanceId(string encounterId, int index) =>
        $"{encounterId}/creature-{index:0000}";

    private static string FormatDiagnostics(
        System.Collections.Generic.IEnumerable<DungeonSaveDiagnostic> diagnostics
    ) =>
        string.Join(
            "; ",
            diagnostics.Select(diagnostic =>
                $"{diagnostic.Code} at {diagnostic.Path}: {diagnostic.Message}"
            )
        );
}
