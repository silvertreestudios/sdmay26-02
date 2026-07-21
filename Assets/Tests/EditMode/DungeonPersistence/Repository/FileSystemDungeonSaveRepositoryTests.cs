using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Game.DungeonPersistence.Repository;
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
                "dungeon-save-tests",
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
        public void SaveAndLoadRoundTripsOneAtomicMultiFloorArchive()
        {
            DungeonRunSave expected = DungeonSaveTestFactory.CreateRun();

            DungeonSaveResult<bool> written = repository.Save(expected);
            DungeonSaveResult<DungeonRunSave> loaded = repository.Load();

            Assert.That(written.IsSuccess, Is.True, Format(written.Diagnostics));
            Assert.That(loaded.IsSuccess, Is.True);
            DungeonRunSave actual = loaded.Value;
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(actual),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(expected))
            );

            string archivePath = Path.Combine(testRoot, "autosave.zip");
            Assert.That(File.Exists(archivePath), Is.True);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.That(
                archive.Entries.Select(entry => entry.FullName),
                Is.EquivalentTo(
                    new[] { "manifest.json", "floors/depth-0000.json", "floors/depth-0001.json" }
                )
            );
        }

        [Test]
        public void MissingSaveReturnsStructuredDiagnosticWithoutCreatingFiles()
        {
            DungeonSaveResult<DungeonRunSave> result = repository.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(
                result.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.MissingSave)
            );
            Assert.That(result.Diagnostics[0].Path, Is.EqualTo("autosave.zip"));
            Assert.That(Directory.GetFileSystemEntries(testRoot), Is.Empty);
        }

        [Test]
        public void IncompatibleWriteLeavesPreviousAutosaveCurrent()
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

            DungeonSaveResult<bool> rejected = repository.Save(incompatible);

            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(
                rejected.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.IncompatibleVersion)
            );
            AssertRunEquals(previous, LoadSuccess(repository));
        }

        [Test]
        public void CorruptOnlyArchiveReturnsNoPartialSave()
        {
            Assert.That(repository.Save(DungeonSaveTestFactory.CreateRun()).IsSuccess, Is.True);
            File.WriteAllText(Path.Combine(testRoot, "autosave.zip"), "{not-an-archive");

            DungeonSaveResult<DungeonRunSave> result = repository.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics[0].Code,
                Is.EqualTo(DungeonSaveDiagnosticCode.CorruptSave)
            );
        }

        [Test]
        public void AbandonedTemporaryArchiveIsIgnored()
        {
            DungeonRunSave expected = DungeonSaveTestFactory.CreateRun();
            Assert.That(repository.Save(expected).IsSuccess, Is.True);
            File.WriteAllText(Path.Combine(testRoot, "autosave-interrupted.tmp"), "{incomplete");

            AssertRunEquals(expected, LoadSuccess(repository));
        }

        [Test]
        public void CorruptCurrentArchiveRecoversPreviousCommittedArchive()
        {
            DungeonRunSave first = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 18);
            DungeonRunSave second = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 17);
            Assert.That(repository.Save(first).IsSuccess, Is.True);
            Assert.That(repository.Save(second).IsSuccess, Is.True);
            File.WriteAllText(Path.Combine(testRoot, "autosave.zip"), "{corrupt");

            DungeonSaveResult<DungeonRunSave> result = repository.Load();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == DungeonSaveDiagnosticCode.RecoveredPreviousGeneration
                    && diagnostic.Severity == DungeonSaveDiagnosticSeverity.Warning
                ),
                Is.True
            );
            AssertRunEquals(first, result.Value);
        }

        [Test]
        public void SaveAfterRecoveryDoesNotReplaceTheLastValidBackupWithCorruption()
        {
            DungeonRunSave first = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 18);
            DungeonRunSave second = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 17);
            DungeonRunSave third = DungeonSaveTestFactory.CreateRun(leaderHitPoints: 16);
            Assert.That(repository.Save(first).IsSuccess, Is.True);
            Assert.That(repository.Save(second).IsSuccess, Is.True);
            File.WriteAllText(Path.Combine(testRoot, "autosave.zip"), "{corrupt");
            AssertRunEquals(first, LoadSuccess(repository));

            Assert.That(repository.Save(third).IsSuccess, Is.True);
            File.WriteAllText(Path.Combine(testRoot, "autosave.zip"), "{corrupt-again");

            AssertRunEquals(first, LoadSuccess(repository));
        }

        [Test]
        public void ArchiveWithUnindexedEntryIsRejected()
        {
            DungeonRunSave expected = DungeonSaveTestFactory.CreateRun();
            Assert.That(repository.Save(expected).IsSuccess, Is.True);
            string path = Path.Combine(testRoot, "autosave.zip");
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                using StreamWriter writer = new(archive.CreateEntry("extra.json").Open());
                writer.Write("{}");
            }

            DungeonSaveResult<DungeonRunSave> result = repository.Load();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == DungeonSaveDiagnosticCode.CorruptSave
                ),
                Is.True
            );
        }

        private static DungeonRunSave LoadSuccess(FileSystemDungeonSaveRepository target)
        {
            DungeonSaveResult<DungeonRunSave> result = target.Load();
            Assert.That(result.IsSuccess, Is.True, Format(result.Diagnostics));
            return result.Value;
        }

        private static void AssertRunEquals(DungeonRunSave expected, DungeonRunSave actual)
        {
            Assert.That(
                DungeonSaveJsonCodec.SerializeRun(actual),
                Is.EqualTo(DungeonSaveJsonCodec.SerializeRun(expected))
            );
        }

        private static string Format(
            System.Collections.Generic.IEnumerable<DungeonSaveDiagnostic> diagnostics
        ) => string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Message));
    }
}
