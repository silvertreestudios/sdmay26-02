using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Autosave
{
    /// <summary>Identifies the stable gameplay boundary that requested an autosave.</summary>
    internal enum DungeonAutosaveTriggerKind
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
    internal enum DungeonAutosaveAttemptOutcome
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

    /// <summary>
    /// Describes one save, deferral, or failure without exposing a partial save transaction.
    /// </summary>
    internal sealed class DungeonAutosaveAttemptResult
    {
        private DungeonAutosaveAttemptResult(
            DungeonAutosaveAttemptOutcome outcome,
            IEnumerable<DungeonAutosaveTriggerKind> triggers,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
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
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray()
            );
        }

        /// <summary>Gets whether the complete run became the repository's current generation.</summary>
        public bool IsSuccess => Outcome == DungeonAutosaveAttemptOutcome.Saved;

        /// <summary>Gets the stable outcome category.</summary>
        public DungeonAutosaveAttemptOutcome Outcome { get; }

        /// <summary>Gets every coalesced gameplay boundary covered by this attempt.</summary>
        public IReadOnlyList<DungeonAutosaveTriggerKind> Triggers { get; }

        /// <summary>Gets capture, scheduling, validation, or I/O diagnostics.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }

        internal static DungeonAutosaveAttemptResult NotAttempted() =>
            new(
                DungeonAutosaveAttemptOutcome.NotAttempted,
                Array.Empty<DungeonAutosaveTriggerKind>(),
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
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.InvalidSnapshot,
                        DungeonSaveDiagnosticSeverity.Warning,
                        "autosave",
                        "Autosave is queued until every actor action has completed."
                    ),
                }
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
                        "The current dungeon floor could not be captured",
                        exception
                    ),
                }
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
                        "The autosave transaction could not be committed",
                        exception
                    ),
                }
            );

        internal static DungeonAutosaveAttemptResult FromWrite(
            IEnumerable<DungeonAutosaveTriggerKind> triggers,
            DungeonSaveResult<bool> write
        )
        {
            if (write == null)
                throw new ArgumentNullException(nameof(write));
            return new DungeonAutosaveAttemptResult(
                write.IsSuccess
                    ? DungeonAutosaveAttemptOutcome.Saved
                    : DungeonAutosaveAttemptOutcome.WriteFailed,
                triggers,
                write.Diagnostics
            );
        }

        private static DungeonSaveDiagnostic ExceptionDiagnostic(
            string context,
            Exception exception
        )
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));
            return new DungeonSaveDiagnostic(
                DungeonSaveDiagnosticCode.InvalidSnapshot,
                DungeonSaveDiagnosticSeverity.Error,
                "autosave",
                $"{context}: {exception.GetType().Name}: {exception.Message}"
            );
        }
    }
}
