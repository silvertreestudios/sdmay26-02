using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Autosave
{
    /// <summary>Identifies the stable gameplay boundary that requested an autosave.</summary>
    public enum DungeonAutosaveTriggerKind
    {
        /// <summary>A new deterministic floor finished generation and runtime initialization.</summary>
        FloorGenerated,

        /// <summary>A door, encounter, or creature lifecycle change became persistent.</summary>
        PersistentFloorStateChanged,

        /// <summary>An actor action completed and its durable effects are stable.</summary>
        ActionCompleted,

        /// <summary>A combat turn completed before the next actor began acting.</summary>
        TurnCompleted,

        /// <summary>Floor travel requested a durable checkpoint before changing scenes or depth.</summary>
        StairTravel,

        /// <summary>The application entered a suspended or background state.</summary>
        ApplicationPaused,

        /// <summary>The application began an orderly quit sequence.</summary>
        ApplicationQuit,
    }

    /// <summary>Classifies the result of the most recent coordinator attempt.</summary>
    public enum DungeonAutosaveAttemptOutcome
    {
        /// <summary>No autosave has been requested since coordinator initialization.</summary>
        NotAttempted,

        /// <summary>The complete run transaction committed atomically.</summary>
        Saved,

        /// <summary>The request remains queued until all actor actions are stable.</summary>
        DeferredActorsBusy,

        /// <summary>Runtime state could not be captured and no repository write was attempted.</summary>
        CaptureFailed,

        /// <summary>The repository rejected or could not atomically publish the candidate run.</summary>
        WriteFailed,
    }

    /// <summary>Provides stable coordinator-specific diagnostic categories.</summary>
    public enum DungeonAutosaveCoordinatorDiagnosticCode
    {
        /// <summary>At least one actor was still applying an action.</summary>
        ActorsBusy,

        /// <summary>The live runtime could not produce one exact current-floor capture.</summary>
        CaptureFailed,

        /// <summary>The atomic session commit threw before returning a repository result.</summary>
        CommitFailed,
    }

    /// <summary>Reports a coordinator failure or deferral independently of repository diagnostics.</summary>
    public sealed class DungeonAutosaveCoordinatorDiagnostic
    {
        /// <summary>Creates one stable autosave-coordinator diagnostic.</summary>
        /// <param name="code">The programmatic category for UI and telemetry.</param>
        /// <param name="message">A concise actionable explanation.</param>
        public DungeonAutosaveCoordinatorDiagnostic(
            DungeonAutosaveCoordinatorDiagnosticCode code,
            string message
        )
        {
            if (!Enum.IsDefined(typeof(DungeonAutosaveCoordinatorDiagnosticCode), code))
                throw new ArgumentOutOfRangeException(nameof(code));
            Code = code;
            Message = message ?? string.Empty;
        }

        /// <summary>Gets the programmatic diagnostic category.</summary>
        public DungeonAutosaveCoordinatorDiagnosticCode Code { get; }

        /// <summary>Gets the explanation suitable for logs or status UI.</summary>
        public string Message { get; }
    }

    /// <summary>
    /// Describes one save, deferral, or failure without exposing a partial save transaction.
    /// </summary>
    public sealed class DungeonAutosaveAttemptResult
    {
        private DungeonAutosaveAttemptResult(
            DungeonAutosaveAttemptOutcome outcome,
            IEnumerable<DungeonAutosaveTriggerKind> triggers,
            IEnumerable<DungeonAutosaveCoordinatorDiagnostic> coordinatorDiagnostics,
            IEnumerable<DungeonSaveDiagnostic> repositoryDiagnostics
        )
        {
            if (!Enum.IsDefined(typeof(DungeonAutosaveAttemptOutcome), outcome))
                throw new ArgumentOutOfRangeException(nameof(outcome));
            Outcome = outcome;
            Triggers = Array.AsReadOnly(
                (triggers ?? throw new ArgumentNullException(nameof(triggers)))
                    .Distinct()
                    .OrderBy(trigger => trigger)
                    .ToArray()
            );
            CoordinatorDiagnostics = Array.AsReadOnly(
                (
                    coordinatorDiagnostics
                    ?? throw new ArgumentNullException(nameof(coordinatorDiagnostics))
                ).ToArray()
            );
            RepositoryDiagnostics = Array.AsReadOnly(
                (
                    repositoryDiagnostics
                    ?? throw new ArgumentNullException(nameof(repositoryDiagnostics))
                ).ToArray()
            );
        }

        /// <summary>Gets whether the complete run became the repository's current generation.</summary>
        public bool IsSuccess => Outcome == DungeonAutosaveAttemptOutcome.Saved;

        /// <summary>Gets the stable outcome category.</summary>
        public DungeonAutosaveAttemptOutcome Outcome { get; }

        /// <summary>Gets every coalesced gameplay boundary covered by this attempt.</summary>
        public IReadOnlyList<DungeonAutosaveTriggerKind> Triggers { get; }

        /// <summary>Gets capture, scheduling, or unexpected commit diagnostics.</summary>
        public IReadOnlyList<DungeonAutosaveCoordinatorDiagnostic> CoordinatorDiagnostics { get; }

        /// <summary>Gets validation and I/O diagnostics returned by the atomic repository.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> RepositoryDiagnostics { get; }

        internal static DungeonAutosaveAttemptResult NotAttempted() =>
            new(
                DungeonAutosaveAttemptOutcome.NotAttempted,
                Array.Empty<DungeonAutosaveTriggerKind>(),
                Array.Empty<DungeonAutosaveCoordinatorDiagnostic>(),
                Array.Empty<DungeonSaveDiagnostic>()
            );

        internal static DungeonAutosaveAttemptResult Deferred(
            IEnumerable<DungeonAutosaveTriggerKind> triggers
        ) =>
            new(
                DungeonAutosaveAttemptOutcome.DeferredActorsBusy,
                triggers,
                new[]
                {
                    new DungeonAutosaveCoordinatorDiagnostic(
                        DungeonAutosaveCoordinatorDiagnosticCode.ActorsBusy,
                        "Autosave is queued until every actor action has completed."
                    ),
                },
                Array.Empty<DungeonSaveDiagnostic>()
            );

        internal static DungeonAutosaveAttemptResult CaptureFailed(
            IEnumerable<DungeonAutosaveTriggerKind> triggers,
            Exception exception
        ) =>
            new(
                DungeonAutosaveAttemptOutcome.CaptureFailed,
                triggers,
                new[]
                {
                    ExceptionDiagnostic(
                        DungeonAutosaveCoordinatorDiagnosticCode.CaptureFailed,
                        "The current dungeon floor could not be captured",
                        exception
                    ),
                },
                Array.Empty<DungeonSaveDiagnostic>()
            );

        internal static DungeonAutosaveAttemptResult CommitFailed(
            IEnumerable<DungeonAutosaveTriggerKind> triggers,
            Exception exception
        ) =>
            new(
                DungeonAutosaveAttemptOutcome.WriteFailed,
                triggers,
                new[]
                {
                    ExceptionDiagnostic(
                        DungeonAutosaveCoordinatorDiagnosticCode.CommitFailed,
                        "The autosave transaction could not be committed",
                        exception
                    ),
                },
                Array.Empty<DungeonSaveDiagnostic>()
            );

        internal static DungeonAutosaveAttemptResult FromWrite(
            IEnumerable<DungeonAutosaveTriggerKind> triggers,
            DungeonSaveWriteResult write
        )
        {
            if (write == null)
                throw new ArgumentNullException(nameof(write));
            return new DungeonAutosaveAttemptResult(
                write.IsSuccess
                    ? DungeonAutosaveAttemptOutcome.Saved
                    : DungeonAutosaveAttemptOutcome.WriteFailed,
                triggers,
                Array.Empty<DungeonAutosaveCoordinatorDiagnostic>(),
                write.Diagnostics
            );
        }

        private static DungeonAutosaveCoordinatorDiagnostic ExceptionDiagnostic(
            DungeonAutosaveCoordinatorDiagnosticCode code,
            string context,
            Exception exception
        )
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));
            return new DungeonAutosaveCoordinatorDiagnostic(
                code,
                $"{context}: {exception.GetType().Name}: {exception.Message}"
            );
        }
    }
}
