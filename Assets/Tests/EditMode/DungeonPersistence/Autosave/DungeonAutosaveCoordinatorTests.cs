using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Autosave;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;
using NUnit.Framework;
using Tests.EditMode.DungeonPersistence.Repository;
using UnityEngine;

namespace Tests.EditMode.DungeonPersistence.Autosave
{
    public sealed class DungeonAutosaveCoordinatorTests
    {
        private readonly List<GameObject> createdObjects = new();
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            OnActionComplete.RemoveAllListeners();
            OnActorActionCompleted.RemoveAllListeners();
            OnNextTurn.RemoveAllListeners();
            testRoot = Path.Combine(
                Directory.GetCurrentDirectory(),
                ".agent-temp",
                "ds-c",
                Guid.NewGuid().ToString("N")
            );
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject created in createdObjects)
            {
                if (created != null)
                    UnityEngine.Object.DestroyImmediate(created);
            }
            createdObjects.Clear();
            OnActionComplete.RemoveAllListeners();
            OnActorActionCompleted.RemoveAllListeners();
            OnNextTurn.RemoveAllListeners();
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
            else if (File.Exists(testRoot))
                File.Delete(testRoot);
        }

        [Test]
        public void NewFloorInitializationImmediatelyCommitsCompleteRun()
        {
            TestComposition composition = CreateNewComposition();
            List<DungeonAutosaveAttemptResult> published = new();
            composition.Coordinator.AutosaveAttempted += published.Add;

            composition.Coordinator.InitializeNewFloor(composition.Session, composition.Source);

            Assert.That(composition.Source.NewCaptureCount, Is.EqualTo(1));
            Assert.That(composition.Source.ExistingCaptureCount, Is.Zero);
            Assert.That(composition.Session.HasCommittedSave, Is.True);
            Assert.That(composition.Coordinator.LastResult.IsSuccess, Is.True);
            Assert.That(
                composition.Coordinator.LastResult.Triggers,
                Is.EqualTo(new[] { DungeonAutosaveTriggerKind.FloorGenerated })
            );
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(composition.Repository.Load(), Is.TypeOf<DungeonSaveLoadSuccess>());
        }

        [Test]
        public void RestoredFloorWaitsForNextDurableChangeBeforeRewriting()
        {
            DungeonRunSave save = DungeonSaveTestFactory.CreateRun();
            FileSystemDungeonSaveRepository repository = new(testRoot);
            Assert.That(repository.Save(save).IsSuccess, Is.True);
            DungeonRunAutosaveSession session = DungeonRunAutosaveSession.Restore(save, repository);
            FakeCaptureSource source = new(CaptureForDepth(save, depth: 0));
            DungeonAutosaveCoordinator coordinator = CreateCoordinator();

            coordinator.InitializeRestoredFloor(session, source);

            Assert.That(source.TotalCaptureCount, Is.Zero);
            Assert.That(
                coordinator.LastResult.Outcome,
                Is.EqualTo(DungeonAutosaveAttemptOutcome.NotAttempted)
            );

            source.RaisePersistentStateChanged(DungeonPersistentStateChangeKind.EncounterLifecycle);

            Assert.That(source.NewCaptureCount, Is.Zero);
            Assert.That(source.ExistingCaptureCount, Is.EqualTo(1));
            Assert.That(coordinator.LastResult.IsSuccess, Is.True);
        }

        [Test]
        public void BusyActorsCoalesceOrdinaryTriggersUntilStableBoundary()
        {
            TestComposition composition = CreateNewComposition();
            composition.Coordinator.InitializeNewFloor(composition.Session, composition.Source);
            composition.Source.AreActorsStable = false;

            composition.Source.RaisePersistentStateChanged(
                DungeonPersistentStateChangeKind.DoorOpened
            );
            OnActorActionCompleted.Invoke(composition.Coordinator.gameObject);
            OnNextTurn.Invoke(composition.Coordinator.gameObject);

            Assert.That(composition.Source.TotalCaptureCount, Is.EqualTo(1));
            Assert.That(composition.Coordinator.HasPendingAutosave, Is.True);
            Assert.That(
                composition.Coordinator.LastResult.Outcome,
                Is.EqualTo(DungeonAutosaveAttemptOutcome.DeferredActorsBusy)
            );
            Assert.That(
                composition.Coordinator.LastResult.Triggers,
                Is.EquivalentTo(
                    new[]
                    {
                        DungeonAutosaveTriggerKind.PersistentFloorStateChanged,
                        DungeonAutosaveTriggerKind.ActionCompleted,
                        DungeonAutosaveTriggerKind.TurnCompleted,
                    }
                )
            );

            composition.Coordinator.ProcessPendingAutosave();
            Assert.That(composition.Source.TotalCaptureCount, Is.EqualTo(1));

            composition.Source.AreActorsStable = true;
            DungeonAutosaveAttemptResult committed =
                composition.Coordinator.ProcessPendingAutosave();

            Assert.That(committed.IsSuccess, Is.True);
            Assert.That(composition.Source.NewCaptureCount, Is.EqualTo(1));
            Assert.That(composition.Source.ExistingCaptureCount, Is.EqualTo(1));
            Assert.That(composition.Coordinator.HasPendingAutosave, Is.False);
            Assert.That(
                committed.Triggers,
                Is.EquivalentTo(
                    new[]
                    {
                        DungeonAutosaveTriggerKind.PersistentFloorStateChanged,
                        DungeonAutosaveTriggerKind.ActionCompleted,
                        DungeonAutosaveTriggerKind.TurnCompleted,
                    }
                )
            );
        }

        [Test]
        public void BusyToIdleActionBoundarySavesExactlyOnceAndIgnoresLegacyUiCompletion()
        {
            TestComposition composition = CreateNewComposition();
            composition.Coordinator.InitializeNewFloor(composition.Session, composition.Source);
            int initialCaptures = composition.Source.TotalCaptureCount;
            GameObject actor = new("action-boundary-actor");
            createdObjects.Add(actor);
            TestActionController controller = actor.AddComponent<TestActionController>();

            controller.IsTakingAction = true;
            OnActionComplete.Invoke();

            Assert.That(composition.Source.TotalCaptureCount, Is.EqualTo(initialCaptures));

            controller.CompleteAction();
            controller.CompleteAction();
            OnActionComplete.Invoke();

            Assert.That(composition.Source.TotalCaptureCount, Is.EqualTo(initialCaptures + 1));
            Assert.That(
                composition.Coordinator.LastResult.Triggers,
                Is.EqualTo(new[] { DungeonAutosaveTriggerKind.ActionCompleted })
            );
        }

        [Test]
        public void CaptureAndWriteFailuresPreservePriorCommittedSession()
        {
            TestComposition composition = CreateNewComposition();
            composition.Coordinator.InitializeNewFloor(composition.Session, composition.Source);
            DungeonRunSave prior = composition.Session.CommittedSave;
            composition.Source.CaptureException = new InvalidOperationException(
                "synthetic capture failure"
            );

            composition.Source.RaisePersistentStateChanged(
                DungeonPersistentStateChangeKind.CreatureDefeated
            );

            Assert.That(
                composition.Coordinator.LastResult.Outcome,
                Is.EqualTo(DungeonAutosaveAttemptOutcome.CaptureFailed)
            );
            Assert.That(composition.Session.CommittedSave, Is.SameAs(prior));
            Assert.That(composition.Coordinator.HasPendingAutosave, Is.False);

            composition.Source.CaptureException = NoCaptureException.Instance;
            string staging = Path.Combine(testRoot, ".staging");
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            File.WriteAllText(staging, "block staging directory creation");

            composition.Source.RaisePersistentStateChanged(
                DungeonPersistentStateChangeKind.DoorOpened
            );

            Assert.That(
                composition.Coordinator.LastResult.Outcome,
                Is.EqualTo(DungeonAutosaveAttemptOutcome.WriteFailed)
            );
            Assert.That(
                composition.Coordinator.LastResult.RepositoryDiagnostics.Select(item => item.Code),
                Does.Contain(DungeonSaveDiagnosticCode.IoFailure)
            );
            Assert.That(composition.Session.CommittedSave, Is.SameAs(prior));
            DungeonSaveLoadSuccess loaded = composition.Repository.Load() as DungeonSaveLoadSuccess;
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Save.Manifest.CurrentDepth, Is.EqualTo(prior.Manifest.CurrentDepth));
        }

        [Test]
        public void StairPauseAndOrderlyQuitEachAttemptCheckpoint()
        {
            TestComposition composition = CreateNewComposition();
            composition.Coordinator.InitializeNewFloor(composition.Session, composition.Source);
            int initialCaptures = composition.Source.TotalCaptureCount;

            DungeonAutosaveAttemptResult stair =
                composition.Coordinator.TryAutosaveBeforeStairTravel();
            composition.Coordinator.TryAutosaveForApplicationPause();
            composition.Coordinator.TryAutosaveForApplicationQuit();

            Assert.That(stair.IsSuccess, Is.True);
            Assert.That(
                stair.Triggers,
                Is.EqualTo(new[] { DungeonAutosaveTriggerKind.StairTravel })
            );
            Assert.That(composition.Source.TotalCaptureCount, Is.EqualTo(initialCaptures + 3));
            Assert.That(
                composition.Coordinator.LastResult.Triggers,
                Is.EqualTo(new[] { DungeonAutosaveTriggerKind.ApplicationQuit })
            );
        }

        [Test]
        public void DestroyRemovesRuntimeActionAndTurnSubscriptions()
        {
            TestComposition composition = CreateNewComposition();
            composition.Coordinator.InitializeNewFloor(composition.Session, composition.Source);
            int initialCaptures = composition.Source.TotalCaptureCount;

            UnityEngine.Object.DestroyImmediate(composition.Coordinator.gameObject);
            GameObject unobservedActor = new("unobserved-actor");
            createdObjects.Add(unobservedActor);
            composition.Source.RaisePersistentStateChanged(
                DungeonPersistentStateChangeKind.DoorOpened
            );
            OnActorActionCompleted.Invoke(unobservedActor);
            OnActionComplete.Invoke();
            OnNextTurn.Invoke(unobservedActor);

            Assert.That(composition.Source.TotalCaptureCount, Is.EqualTo(initialCaptures));
        }

        [Test]
        public void ProductionFactoryBuildsDedicatedChildOfInjectedPersistentRoot()
        {
            string root = DungeonAutosaveProductionRepositoryFactory.BuildAutosaveRootPath(
                testRoot
            );

            Assert.That(
                root,
                Is.EqualTo(
                    Path.GetFullPath(
                        Path.Combine(
                            testRoot,
                            DungeonAutosaveProductionRepositoryFactory.AutosaveDirectoryName
                        )
                    )
                )
            );
            Assert.That(Path.GetDirectoryName(root), Is.EqualTo(Path.GetFullPath(testRoot)));
        }

        private TestComposition CreateNewComposition()
        {
            DungeonRunSave save = DungeonSaveTestFactory.CreateRun();
            FileSystemDungeonSaveRepository repository = new(testRoot);
            DungeonRunAutosaveSession session = DungeonRunAutosaveSession.CreateNew(
                save.Manifest.StartingSeed,
                save.Manifest.GeneratorVersion,
                repository
            );
            FakeCaptureSource source = new(CaptureForDepth(save, depth: 0));
            return new TestComposition(CreateCoordinator(), session, repository, source);
        }

        private DungeonAutosaveCoordinator CreateCoordinator()
        {
            GameObject owner = new(nameof(DungeonAutosaveCoordinatorTests));
            createdObjects.Add(owner);
            return owner.AddComponent<DungeonAutosaveCoordinator>();
        }

        private static DungeonCurrentFloorCapture CaptureForDepth(DungeonRunSave save, int depth) =>
            new(save.Manifest.Party, save.Floors.Single(floor => floor.Depth == depth));

        private sealed class TestComposition
        {
            internal TestComposition(
                DungeonAutosaveCoordinator coordinator,
                DungeonRunAutosaveSession session,
                FileSystemDungeonSaveRepository repository,
                FakeCaptureSource source
            )
            {
                Coordinator = coordinator;
                Session = session;
                Repository = repository;
                Source = source;
            }

            internal DungeonAutosaveCoordinator Coordinator { get; }

            internal DungeonRunAutosaveSession Session { get; }

            internal FileSystemDungeonSaveRepository Repository { get; }

            internal FakeCaptureSource Source { get; }
        }

        private sealed class FakeCaptureSource : IDungeonAutosaveCaptureSource
        {
            private readonly DungeonCurrentFloorCapture capture;

            internal FakeCaptureSource(DungeonCurrentFloorCapture capture)
            {
                this.capture = capture;
                Depth = capture.Floor.Depth;
            }

            public event Action<DungeonPersistentStateChangeKind> PersistentStateChanged = delegate
            { };

            public int Depth { get; }

            public bool AreActorsStable { get; set; } = true;

            internal Exception CaptureException { get; set; } = NoCaptureException.Instance;

            internal int NewCaptureCount { get; private set; }

            internal int ExistingCaptureCount { get; private set; }

            internal int TotalCaptureCount => NewCaptureCount + ExistingCaptureCount;

            public DungeonCurrentFloorCapture CaptureNew()
            {
                NewCaptureCount++;
                ThrowWhenConfigured();
                return capture;
            }

            public DungeonCurrentFloorCapture CaptureExisting(DungeonFloorSaveState previousFloor)
            {
                ExistingCaptureCount++;
                Assert.That(previousFloor.Depth, Is.EqualTo(Depth));
                ThrowWhenConfigured();
                return capture;
            }

            internal void RaisePersistentStateChanged(DungeonPersistentStateChangeKind change) =>
                PersistentStateChanged(change);

            private void ThrowWhenConfigured()
            {
                if (CaptureException is not NoCaptureException)
                    throw CaptureException;
            }
        }

        private sealed class NoCaptureException : Exception
        {
            internal static readonly NoCaptureException Instance = new();

            private NoCaptureException() { }
        }

        private sealed class TestActionController : ActionController
        {
            /// <inheritdoc/>
            public override void EndTurn() { }
        }
    }
}
