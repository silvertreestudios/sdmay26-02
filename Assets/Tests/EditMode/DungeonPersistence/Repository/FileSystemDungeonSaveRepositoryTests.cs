using System;
using System.IO;
using System.Linq;
using Game.DungeonPersistence.Repository;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.DungeonPersistence.Repository
{
    [TestFixture]
    public sealed class FileSystemDungeonSaveRepositoryTests
    {
        private string testRoot;
        private FileSystemDungeonSaveRepository repository;

        [SetUp]
        public void SetUp()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            testRoot = Path.Combine(
                projectRoot,
                ".agent-temp",
                "ds-r",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(testRoot);
            repository = new FileSystemDungeonSaveRepository(testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }

        [Test]
        public void SaveAndLoadRoundTripsOneAtomicMultiFloorAutosave()
        {
            DungeonRunSave expected = DungeonSaveTestFactory.CreateRun();

            DungeonSaveWriteResult written = repository.Save(expected);
            DungeonSaveLoadResult loaded = repository.Load();

            Assert.That(written.IsSuccess, Is.True, Format(written.Diagnostics));
            Assert.That(loaded, Is.TypeOf<DungeonSaveLoadSuccess>());
            DungeonRunSave actual = ((DungeonSaveLoadSuccess)loaded).Save;
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(actual),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(expected))
            );
            Assert.That(
                actual.Floors[0].StaticFloorJson,
                Is.EqualTo(expected.Floors[0].StaticFloorJson)
            );
            Assert.That(File.Exists(Path.Combine(testRoot, "current.json")), Is.True);
        }

        [Test]
        public void MissingSaveReturnsStructuredDiagnosticWithoutCreatingFiles()
        {
            DungeonSaveLoadResult result = repository.Load();

            Assert.That(result, Is.TypeOf<DungeonSaveLoadFailure>());
            Assert.That(result.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(
                result.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.MissingSave)
            );
            Assert.That(result.Diagnostics[0].Path, Is.EqualTo("current.json"));
            Assert.That(Directory.GetFileSystemEntries(testRoot), Is.Empty);
        }

        [Test]
        public void CorruptOnlyGenerationReturnsStructuredDiagnosticAndNoPartialSave()
        {
            Assert.That(repository.Save(DungeonSaveTestFactory.CreateRun()).IsSuccess, Is.True);
            string generation = CurrentGenerationPath(testRoot);
            File.WriteAllText(Path.Combine(generation, "floors", "depth-0000.json"), "{not-json");

            DungeonSaveLoadResult result = repository.Load();

            Assert.That(result, Is.TypeOf<DungeonSaveLoadFailure>());
            Assert.That(
                result.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.CorruptSave)
            );
        }

        [Test]
        public void IncompatibleWriteLeavesPreviousValidAutosaveCurrent()
        {
            DungeonRunSave previous = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 18);
            Assert.That(repository.Save(previous).IsSuccess, Is.True);
            DungeonRunSaveManifest original = previous.Manifest;
            DungeonRunSave incompatible = new(
                new DungeonRunSaveManifest(
                    99,
                    original.StartingSeed,
                    original.GeneratorVersion,
                    original.CurrentDepth,
                    original.Party,
                    original.GeneratedFloors
                ),
                previous.Floors
            );

            DungeonSaveWriteResult rejected = repository.Save(incompatible);
            DungeonRunSave loaded = ((DungeonSaveLoadSuccess)repository.Load()).Save;

            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(
                rejected.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
            );
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(loaded),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(previous))
            );
        }

        [Test]
        public void InvalidPlanCoverageLeavesPreviousValidAutosaveCurrent()
        {
            DungeonRunSave previous = DungeonSaveTestFactory.CreateRun();
            Assert.That(repository.Save(previous).IsSuccess, Is.True);
            DungeonFloorSaveState validFloor = previous.Floors[0];
            DungeonEncounterCreatureSaveState validActive = validFloor.Creatures.Single(creature =>
                creature.EncounterId == "encounter-active"
            );
            DungeonEncounterCreatureSaveState invalidActive = new(
                "encounter-active",
                DungeonSaveTestFactory.CreateCreature(
                    "arbitrary-instance",
                    validActive.Creature.CreatureContentId,
                    validActive.Creature.Cell,
                    validActive.Creature.Health.CurrentHitPoints,
                    validActive.Creature.Health.MaximumHitPoints,
                    richState: false
                )
            );
            DungeonFloorSaveState invalidFloor = new(
                validFloor.DocumentVersion,
                validFloor.Depth,
                validFloor.StaticFloorJson,
                validFloor.Doors,
                validFloor.Encounters,
                validFloor.Creatures.Select(creature =>
                    creature == validActive ? invalidActive : creature
                )
            );
            DungeonRunSave invalid = new(
                previous.Manifest,
                previous.Floors.Select(floor =>
                    floor.Depth == invalidFloor.Depth ? invalidFloor : floor
                )
            );

            DungeonSaveWriteResult rejected = repository.Save(invalid);
            DungeonRunSave loaded = ((DungeonSaveLoadSuccess)repository.Load()).Save;

            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(
                rejected.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == DungeonSaveDiagnosticCode.InvalidSnapshot
                    && diagnostic.Message.Contains("plan-derived")
                ),
                Is.True,
                Format(rejected.Diagnostics)
            );
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(loaded),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(previous))
            );
        }

        [Test]
        public void LivingActorCannotOccupyAClosedDoorCell()
        {
            DungeonRunSave valid = DungeonSaveTestFactory.CreateRun(firstDoorOpen: false);
            DungeonFloorSaveState validFloor = valid.Floors[0];
            DungeonEncounterCreatureSaveState validActive = validFloor.Creatures.Single(creature =>
                creature.EncounterId == "encounter-active"
            );
            DungeonEncounterCreatureSaveState actorOnClosedDoor = new(
                validActive.EncounterId,
                DungeonSaveTestFactory.CreateCreature(
                    validActive.Creature.InstanceId,
                    validActive.Creature.CreatureContentId,
                    new DungeonSaveCell(4, 2),
                    validActive.Creature.Health.CurrentHitPoints,
                    validActive.Creature.Health.MaximumHitPoints,
                    richState: false
                )
            );
            DungeonFloorSaveState invalidFloor = new(
                validFloor.DocumentVersion,
                validFloor.Depth,
                validFloor.StaticFloorJson,
                validFloor.Doors,
                validFloor.Encounters,
                validFloor.Creatures.Select(creature =>
                    creature == validActive ? actorOnClosedDoor : creature
                )
            );
            DungeonRunSave invalid = new(
                valid.Manifest,
                valid.Floors.Select(floor => floor.Depth == 0 ? invalidFloor : floor)
            );

            DungeonSaveWriteResult result = repository.Save(invalid);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Message.Contains("walkable static-floor cell")
                ),
                Is.True,
                Format(result.Diagnostics)
            );
            Assert.That(
                repository.Load().Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.MissingSave)
            );
        }

        [Test]
        public void StagedButUncommittedTransactionIsIgnored()
        {
            DungeonRunSave expected = DungeonSaveTestFactory.CreateRun();
            Assert.That(repository.Save(expected).IsSuccess, Is.True);
            string abandoned = Path.Combine(testRoot, ".staging", "interrupted", "generation");
            Directory.CreateDirectory(abandoned);
            File.WriteAllText(Path.Combine(abandoned, "manifest.json"), "{incomplete");
            File.WriteAllText(Path.Combine(testRoot, "current-interrupted.next"), "{incomplete");

            DungeonSaveLoadResult result = repository.Load();

            Assert.That(result, Is.TypeOf<DungeonSaveLoadSuccess>());
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(((DungeonSaveLoadSuccess)result).Save),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(expected))
            );
        }

        [Test]
        public void CorruptCurrentGenerationRecoversPriorCommittedGeneration()
        {
            DungeonRunSave first = DungeonSaveTestFactory.CreateRun(
                leaderHitPoints: 18,
                firstDoorOpen: true
            );
            DungeonRunSave second = DungeonSaveTestFactory.CreateRun(
                leaderHitPoints: 17,
                firstDoorOpen: false
            );
            Assert.That(repository.Save(first).IsSuccess, Is.True);
            Assert.That(repository.Save(second).IsSuccess, Is.True);
            File.WriteAllText(
                Path.Combine(CurrentGenerationPath(testRoot), "manifest.json"),
                "{corrupt"
            );

            DungeonSaveLoadResult result = repository.Load();

            Assert.That(result, Is.TypeOf<DungeonSaveLoadSuccess>());
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == DungeonSaveDiagnosticCode.RecoveredPreviousGeneration
                    && diagnostic.Severity == DungeonSaveDiagnosticSeverity.Warning
                ),
                Is.True
            );
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(((DungeonSaveLoadSuccess)result).Save),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(first))
            );
        }

        [Test]
        public void SuccessfulCommitsRetainOnlyCurrentAndPreviousGenerations()
        {
            Assert.That(
                repository.Save(DungeonSaveTestFactory.CreateRun(leaderHitPoints: 18)).IsSuccess,
                Is.True
            );
            Assert.That(
                repository.Save(DungeonSaveTestFactory.CreateRun(leaderHitPoints: 17)).IsSuccess,
                Is.True
            );
            Assert.That(
                repository.Save(DungeonSaveTestFactory.CreateRun(leaderHitPoints: 16)).IsSuccess,
                Is.True
            );

            string[] retained = Directory.GetDirectories(Path.Combine(testRoot, "generations"));

            Assert.That(retained, Has.Length.EqualTo(2));
            Assert.That(
                retained.Select(Path.GetFileName),
                Is.EquivalentTo(
                    new[]
                    {
                        PointerGenerationId(testRoot, "current.json"),
                        PointerGenerationId(testRoot, "previous.json"),
                    }
                )
            );
        }

        [Test]
        public void SaveAfterRecoveryPreservesLastValidGenerationAsPrevious()
        {
            DungeonRunSave first = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 18);
            DungeonRunSave second = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 17);
            DungeonRunSave third = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 16);
            Assert.That(repository.Save(first).IsSuccess, Is.True);
            Assert.That(repository.Save(second).IsSuccess, Is.True);
            File.WriteAllText(
                Path.Combine(CurrentGenerationPath(testRoot), "manifest.json"),
                "{corrupt"
            );
            Assert.That(repository.Load(), Is.TypeOf<DungeonSaveLoadSuccess>());

            DungeonSaveWriteResult result = repository.Save(third);
            string currentId = PointerGenerationId(testRoot, "current.json");
            string previousId = PointerGenerationId(testRoot, "previous.json");

            Assert.That(result.IsSuccess, Is.True, Format(result.Diagnostics));
            Assert.That(currentId, Is.Not.EqualTo(previousId));
            File.WriteAllText(
                Path.Combine(testRoot, "generations", currentId, "manifest.json"),
                "{corrupt"
            );
            DungeonSaveLoadSuccess recovered = repository.Load() as DungeonSaveLoadSuccess;
            Assert.That(recovered, Is.Not.Null);
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(recovered.Save),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(first))
            );
        }

        [Test]
        public void EquivalentTransactionsProduceIdenticalGenerationPathsAndPayloads()
        {
            string firstRoot = Path.Combine(testRoot, "first");
            string secondRoot = Path.Combine(testRoot, "second");
            FileSystemDungeonSaveRepository firstRepository = new(firstRoot);
            FileSystemDungeonSaveRepository secondRepository = new(secondRoot);

            Assert.That(
                firstRepository.Save(DungeonSaveTestFactory.CreateRun()).IsSuccess,
                Is.True
            );
            Assert.That(
                secondRepository.Save(DungeonSaveTestFactory.CreateRun()).IsSuccess,
                Is.True
            );
            string firstGeneration = CurrentGenerationPath(firstRoot);
            string secondGeneration = CurrentGenerationPath(secondRoot);

            Assert.That(
                Path.GetFileName(firstGeneration),
                Is.EqualTo(Path.GetFileName(secondGeneration))
            );
            Assert.That(
                File.ReadAllText(Path.Combine(firstGeneration, "manifest.json")),
                Is.EqualTo(File.ReadAllText(Path.Combine(secondGeneration, "manifest.json")))
            );
            Assert.That(
                File.ReadAllText(Path.Combine(firstGeneration, "floors", "depth-0001.json")),
                Is.EqualTo(
                    File.ReadAllText(Path.Combine(secondGeneration, "floors", "depth-0001.json"))
                )
            );
        }

        private static string CurrentGenerationPath(string root)
        {
            return Path.Combine(root, "generations", PointerGenerationId(root, "current.json"));
        }

        private static string PointerGenerationId(string root, string fileName)
        {
            JObject pointer = JObject.Parse(File.ReadAllText(Path.Combine(root, fileName)));
            return pointer["generationId"].Value<string>();
        }

        private static string Format(
            System.Collections.Generic.IEnumerable<DungeonSaveDiagnostic> diagnostics
        ) => string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Message));
    }
}
