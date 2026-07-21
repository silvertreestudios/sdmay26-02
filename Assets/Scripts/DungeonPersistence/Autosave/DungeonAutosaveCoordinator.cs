using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;
using UnityEngine;

namespace Game.DungeonPersistence.Autosave
{
    /// <summary>
    /// Coalesces durable dungeon events and commits exact run state only at stable actor boundaries.
    /// </summary>
    /// <remarks>
    /// The component must be initialized explicitly after the generated-floor runtime. It observes
    /// only the project's existing action/turn events plus the owned runtime's persistence event,
    /// and removes every subscription while disabled or destroyed.
    /// </remarks>
    [DisallowMultipleComponent]
    internal sealed class DungeonAutosaveCoordinator : MonoBehaviour
    {
        private readonly SortedSet<DungeonAutosaveTriggerKind> pendingTriggers = new();
        private readonly SortedDictionary<int, DungeonFloorSaveState> committedFloors = new();
        private IDungeonSaveRepository repository;
        private IDungeonAutosaveCaptureSource captureSource;
        private DungeonRunSave committedSave;
        private int startingSeed;
        private string generatorVersion = string.Empty;
        private bool hasCommittedSave;
        private bool captureAsNewFloor;
        private bool subscriptionsActive;
        private bool reportedCurrentDeferral;

        /// <summary>Raised after a save, failure, or newly reported actor-busy deferral.</summary>
        public event Action<DungeonAutosaveAttemptResult> AutosaveAttempted = delegate { };

        /// <summary>Gets whether the component owns an explicit session and floor capture source.</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>Gets whether one or more triggers are waiting for a stable actor boundary.</summary>
        public bool HasPendingAutosave => pendingTriggers.Count > 0;

        /// <summary>
        /// Gets the latest save, failure, or deferral; before the first request its outcome is
        /// <see cref="DungeonAutosaveAttemptOutcome.NotAttempted"/>.
        /// </summary>
        public DungeonAutosaveAttemptResult LastResult { get; private set; } =
            DungeonAutosaveAttemptResult.NotAttempted();

        internal bool HasCommittedSave => hasCommittedSave;

        internal DungeonRunSave CommittedSave =>
            hasCommittedSave
                ? committedSave
                : throw new InvalidOperationException(
                    "A new dungeon run has not committed its first generated floor."
                );

        /// <summary>
        /// Initializes a newly generated floor and immediately requests its first atomic save.
        /// </summary>
        /// <param name="session">The run session retaining all previously generated depths.</param>
        /// <param name="staticFloorJson">The new floor's pristine deterministic generator JSON.</param>
        /// <param name="runtime">The fully initialized generated-floor runtime.</param>
        public void InitializeNewFloor(
            int runSeed,
            string algorithm,
            IDungeonSaveRepository repository,
            string staticFloorJson,
            DungeonEncounterRuntimeController runtime
        ) =>
            InitializeNewFloor(
                runSeed,
                algorithm,
                repository,
                new DungeonRuntimeAutosaveCaptureSource(staticFloorJson, runtime)
            );

        /// <summary>
        /// Initializes a newly generated floor through an injected source and immediately requests
        /// its first atomic save.
        /// </summary>
        /// <param name="session">The run session retaining all previously generated depths.</param>
        /// <param name="captureSource">The explicit isolated runtime capture boundary.</param>
        /// <remarks>This overload allows tests and alternate composition roots to avoid scene globals.</remarks>
        public void InitializeNewFloor(
            int runSeed,
            string algorithm,
            IDungeonSaveRepository repository,
            IDungeonAutosaveCaptureSource captureSource
        )
        {
            Initialize(
                runSeed,
                algorithm,
                repository,
                Array.Empty<DungeonFloorSaveState>(),
                captureSource,
                isNewFloor: true
            );
            RequestAndTry(DungeonAutosaveTriggerKind.FloorGenerated);
        }

        /// <summary>Initializes the current floor from the session's validated restored save.</summary>
        /// <param name="session">A session restored from one complete repository load.</param>
        /// <param name="runtime">The fully restored generated-floor runtime.</param>
        public void InitializeRestoredFloor(
            DungeonRunSave save,
            IDungeonSaveRepository repository,
            DungeonEncounterRuntimeController runtime
        )
        {
            DungeonFloorSaveState floor = RequireCurrentFloor(save);
            InitializeRestoredFloor(
                save,
                repository,
                new DungeonRuntimeAutosaveCaptureSource(floor.DocumentJson, runtime)
            );
        }

        /// <summary>Initializes a restored floor through an injected isolated capture source.</summary>
        /// <param name="session">A session restored from one complete repository load.</param>
        /// <param name="captureSource">The explicit isolated runtime capture boundary.</param>
        /// <remarks>
        /// Loading does not rewrite an already valid generation. Later durable events recapture the
        /// restored floor while retaining defeated actor records from the committed state.
        /// </remarks>
        public void InitializeRestoredFloor(
            DungeonRunSave save,
            IDungeonSaveRepository repository,
            IDungeonAutosaveCaptureSource captureSource
        )
        {
            DungeonFloorSaveState floor = RequireCurrentFloor(save);
            if (captureSource == null)
                throw new ArgumentNullException(nameof(captureSource));
            if (floor.Depth != captureSource.Depth)
                throw new ArgumentException(
                    "The restored capture source must match the run's current depth.",
                    nameof(captureSource)
                );
            Initialize(
                save.Manifest.StartingSeed,
                save.Manifest.GeneratorVersion,
                repository,
                save.Floors,
                captureSource,
                isNewFloor: false
            );
            committedSave = save;
            hasCommittedSave = true;
        }

        /// <summary>
        /// Requests a checkpoint before stair travel and returns whether it committed immediately.
        /// </summary>
        /// <returns>
        /// A successful result only when child-floor integration may safely leave the current
        /// floor; callers should retain the scene for deferred or failed results.
        /// </returns>
        public DungeonAutosaveAttemptResult TryAutosaveBeforeStairTravel() =>
            RequestAndTry(DungeonAutosaveTriggerKind.StairTravel);

        /// <summary>Requests a checkpoint when the application enters the paused state.</summary>
        /// <returns>The immediate save, deferral, or failure result.</returns>
        /// <remarks>
        /// Unity lifecycle callbacks delegate to this explicit boundary so composition and tests
        /// can request the same checkpoint without invoking a MonoBehaviour message by name.
        /// </remarks>
        public DungeonAutosaveAttemptResult TryAutosaveForApplicationPause() =>
            RequestAndTry(DungeonAutosaveTriggerKind.ApplicationPaused);

        /// <summary>Requests a checkpoint before an orderly application shutdown.</summary>
        /// <returns>The immediate save, deferral, or failure result.</returns>
        /// <remarks>
        /// Unity lifecycle callbacks delegate to this explicit boundary so composition and tests
        /// can request the same checkpoint without invoking a MonoBehaviour message by name.
        /// </remarks>
        public DungeonAutosaveAttemptResult TryAutosaveForApplicationQuit() =>
            RequestAndTry(DungeonAutosaveTriggerKind.ApplicationQuit);

        /// <summary>
        /// Re-evaluates a queued request, normally on the next Unity update after a durable change
        /// occurred while one or more actors were still busy.
        /// </summary>
        /// <returns>The latest result; no event is raised when no request is pending.</returns>
        public DungeonAutosaveAttemptResult ProcessPendingAutosave()
        {
            RequireInitialized();
            return TryFlushPending();
        }

        private void Initialize(
            int runSeed,
            string algorithm,
            IDungeonSaveRepository saveRepository,
            IEnumerable<DungeonFloorSaveState> floors,
            IDungeonAutosaveCaptureSource source,
            bool isNewFloor
        )
        {
            if (IsInitialized)
                throw new InvalidOperationException(
                    "A dungeon autosave coordinator can only be initialized once."
                );
            if (string.IsNullOrWhiteSpace(algorithm))
                throw new ArgumentException(
                    "A stable generator version is required.",
                    nameof(algorithm)
                );
            repository = saveRepository ?? throw new ArgumentNullException(nameof(saveRepository));
            captureSource = source ?? throw new ArgumentNullException(nameof(source));
            startingSeed = runSeed;
            generatorVersion = algorithm;
            foreach (DungeonFloorSaveState floor in floors)
                committedFloors.Add(floor.Depth, floor);

            bool depthAlreadyCommitted = committedFloors.ContainsKey(source.Depth);
            if (isNewFloor && depthAlreadyCommitted)
                throw new ArgumentException(
                    "A newly generated floor cannot replace an already committed depth.",
                    nameof(source)
                );
            if (!isNewFloor && !depthAlreadyCommitted)
                throw new ArgumentException(
                    "A restored floor requires prior committed state for its depth.",
                    nameof(source)
                );

            captureAsNewFloor = isNewFloor;
            IsInitialized = true;
            if (isActiveAndEnabled)
                Subscribe();
        }

        private DungeonAutosaveAttemptResult RequestAndTry(DungeonAutosaveTriggerKind trigger)
        {
            RequireInitialized();
            if (pendingTriggers.Add(trigger))
                reportedCurrentDeferral = false;
            return TryFlushPending();
        }

        private DungeonAutosaveAttemptResult TryFlushPending()
        {
            if (pendingTriggers.Count == 0)
                return LastResult;

            DungeonAutosaveTriggerKind[] triggers = pendingTriggers.ToArray();
            bool actorsStable;
            try
            {
                actorsStable = captureSource.AreActorsStable;
            }
            catch (Exception exception)
            {
                ClearPending();
                return Publish(DungeonAutosaveAttemptResult.CaptureFailed(triggers, exception));
            }

            if (!actorsStable)
            {
                if (!reportedCurrentDeferral)
                {
                    reportedCurrentDeferral = true;
                    return Publish(DungeonAutosaveAttemptResult.Deferred(triggers));
                }
                return LastResult;
            }

            DungeonCurrentFloorCapture capture;
            try
            {
                capture = captureAsNewFloor
                    ? captureSource.CaptureNew()
                    : captureSource.CaptureExisting(committedFloors[captureSource.Depth]);
            }
            catch (Exception exception)
            {
                ClearPending();
                return Publish(DungeonAutosaveAttemptResult.CaptureFailed(triggers, exception));
            }

            DungeonSaveResult<bool> write;
            try
            {
                write = CommitCurrentFloor(capture);
            }
            catch (Exception exception)
            {
                ClearPending();
                return Publish(DungeonAutosaveAttemptResult.CommitFailed(triggers, exception));
            }

            ClearPending();
            DungeonAutosaveAttemptResult result = DungeonAutosaveAttemptResult.FromWrite(
                triggers,
                write
            );
            if (result.IsSuccess)
                captureAsNewFloor = false;
            return Publish(result);
        }

        private DungeonAutosaveAttemptResult Publish(DungeonAutosaveAttemptResult result)
        {
            LastResult = result ?? throw new ArgumentNullException(nameof(result));
            AutosaveAttempted(result);
            return result;
        }

        private void ClearPending()
        {
            pendingTriggers.Clear();
            reportedCurrentDeferral = false;
        }

        private void RequireInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "Initialize the dungeon autosave coordinator before requesting a save."
                );
        }

        private DungeonSaveResult<bool> CommitCurrentFloor(DungeonCurrentFloorCapture capture)
        {
            SortedDictionary<int, DungeonFloorSaveState> candidateFloors = new(committedFloors)
            {
                [capture.Floor.Depth] = capture.Floor,
            };
            DungeonRunSave candidate = new(
                new DungeonRunSaveManifest(
                    DungeonSaveSchema.RunManifestVersion,
                    startingSeed,
                    generatorVersion,
                    capture.Floor.Depth,
                    capture.Party,
                    candidateFloors.Keys.Select(DungeonFloorSaveReference.Current)
                ),
                candidateFloors.Values
            );
            DungeonSaveResult<bool> result = repository.Save(candidate);
            if (!result.IsSuccess)
                return result;

            committedFloors.Clear();
            foreach (KeyValuePair<int, DungeonFloorSaveState> floor in candidateFloors)
                committedFloors.Add(floor.Key, floor.Value);
            committedSave = candidate;
            hasCommittedSave = true;
            return result;
        }

        private static DungeonFloorSaveState RequireCurrentFloor(DungeonRunSave save)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            return save.Floors.Single(floor => floor.Depth == save.Manifest.CurrentDepth);
        }

        private void Subscribe()
        {
            if (!IsInitialized || subscriptionsActive)
                return;
            captureSource.PersistentStateChanged += OnPersistentStateChanged;
            OnActorActionCompleted.AddListener(OnActionCompleted);
            OnNextTurn.AddListener(OnTurnCompleted);
            subscriptionsActive = true;
        }

        private void Unsubscribe()
        {
            if (!subscriptionsActive)
                return;
            subscriptionsActive = false;
            captureSource.PersistentStateChanged -= OnPersistentStateChanged;
            OnActorActionCompleted.RemoveListener(OnActionCompleted);
            OnNextTurn.RemoveListener(OnTurnCompleted);
        }

        private void OnPersistentStateChanged(DungeonPersistentStateChangeKind change)
        {
            if (this != null && subscriptionsActive)
                RequestAndTry(DungeonAutosaveTriggerKind.PersistentFloorStateChanged);
        }

        private void OnActionCompleted(GameObject completedActor)
        {
            if (this != null && subscriptionsActive)
                RequestAndTry(DungeonAutosaveTriggerKind.ActionCompleted);
        }

        private void OnTurnCompleted(GameObject actorBeginningTurn)
        {
            if (this != null && subscriptionsActive)
                RequestAndTry(DungeonAutosaveTriggerKind.TurnCompleted);
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void OnDestroy()
        {
            Unsubscribe();
            IsInitialized = false;
        }

        private void Update()
        {
            if (IsInitialized && HasPendingAutosave)
                TryFlushPending();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && IsInitialized)
                TryAutosaveForApplicationPause();
        }

        private void OnApplicationQuit()
        {
            if (IsInitialized)
                TryAutosaveForApplicationQuit();
        }
    }
}
