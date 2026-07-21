using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>Classifies a persistence diagnostic for stable menu and telemetry handling.</summary>
    public enum DungeonSaveDiagnosticCode
    {
        /// <summary>No committed autosave exists at the repository path.</summary>
        MissingSave,

        /// <summary>A pointer or JSON payload is malformed, incomplete, or fails integrity validation.</summary>
        CorruptSave,

        /// <summary>A document uses a schema version this build cannot load or publish.</summary>
        IncompatibleVersion,

        /// <summary>The proposed in-memory transaction violates a cross-document invariant.</summary>
        InvalidSnapshot,

        /// <summary>The filesystem prevented a complete read or write.</summary>
        IoFailure,

        /// <summary>The current generation failed validation and the prior committed generation was recovered.</summary>
        RecoveredPreviousGeneration,
    }

    /// <summary>Classifies whether a diagnostic blocks the requested operation.</summary>
    public enum DungeonSaveDiagnosticSeverity
    {
        /// <summary>The operation failed and no partial save value is exposed.</summary>
        Error,

        /// <summary>The operation succeeded through a documented recovery path.</summary>
        Warning,
    }

    /// <summary>Provides a stable category, location, and actionable explanation for persistence UI.</summary>
    public sealed class DungeonSaveDiagnostic
    {
        /// <summary>Creates a structured persistence diagnostic.</summary>
        /// <param name="code">The stable diagnostic category.</param>
        /// <param name="severity">Whether the diagnostic blocked the operation.</param>
        /// <param name="path">The logical document or field associated with the problem.</param>
        /// <param name="message">An actionable explanation suitable for menu display or logs.</param>
        public DungeonSaveDiagnostic(
            DungeonSaveDiagnosticCode code,
            DungeonSaveDiagnosticSeverity severity,
            string path,
            string message
        )
        {
            if (!Enum.IsDefined(typeof(DungeonSaveDiagnosticCode), code))
                throw new ArgumentOutOfRangeException(nameof(code));
            if (!Enum.IsDefined(typeof(DungeonSaveDiagnosticSeverity), severity))
                throw new ArgumentOutOfRangeException(nameof(severity));
            Code = code;
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>Gets the stable diagnostic category.</summary>
        public DungeonSaveDiagnosticCode Code { get; }

        /// <summary>Gets whether the diagnostic blocked the operation.</summary>
        public DungeonSaveDiagnosticSeverity Severity { get; }

        /// <summary>Gets the logical document or field associated with the problem.</summary>
        public string Path { get; }

        /// <summary>Gets an actionable explanation suitable for menu display or logs.</summary>
        public string Message { get; }
    }

    /// <summary>Reports whether a complete transaction became the current autosave.</summary>
    public sealed class DungeonSaveWriteResult
    {
        internal DungeonSaveWriteResult(
            bool isSuccess,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
        {
            IsSuccess = isSuccess;
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray()
            );
        }

        /// <summary>Gets whether the validated transaction became current atomically.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets structured validation or I/O diagnostics.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// Represents either a complete validated autosave or a failure. The base type intentionally
    /// exposes no nullable or partially populated save value.
    /// </summary>
    public abstract class DungeonSaveLoadResult
    {
        private protected DungeonSaveLoadResult(
            bool isSuccess,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
        {
            IsSuccess = isSuccess;
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray()
            );
        }

        /// <summary>Gets whether a complete validated autosave is available.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets blocking errors or recovery warnings.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }
    }

    /// <summary>Represents a complete validated autosave, optionally recovered from the prior generation.</summary>
    public sealed class DungeonSaveLoadSuccess : DungeonSaveLoadResult
    {
        internal DungeonSaveLoadSuccess(
            DungeonRunSave save,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
            : base(true, diagnostics)
        {
            Save = save ?? throw new ArgumentNullException(nameof(save));
        }

        /// <summary>Gets the complete immutable autosave transaction.</summary>
        public DungeonRunSave Save { get; }
    }

    /// <summary>Represents a load failure that exposes diagnostics but no partial save.</summary>
    public sealed class DungeonSaveLoadFailure : DungeonSaveLoadResult
    {
        internal DungeonSaveLoadFailure(IEnumerable<DungeonSaveDiagnostic> diagnostics)
            : base(false, diagnostics) { }
    }

    /// <summary>Stores and loads one atomic dungeon autosave independently of Unity scene state.</summary>
    public interface IDungeonSaveRepository
    {
        /// <summary>
        /// Validates and publishes the complete manifest and every indexed floor as one transaction.
        /// The prior committed save remains current when validation or I/O fails.
        /// </summary>
        /// <param name="save">The complete immutable transaction to publish.</param>
        /// <returns>Success only after the atomic current-generation pointer is committed.</returns>
        DungeonSaveWriteResult Save(DungeonRunSave save);

        /// <summary>
        /// Loads and validates every document before exposing a save. A corrupt current generation
        /// may recover the prior committed generation with a warning.
        /// </summary>
        /// <returns>A complete save or structured diagnostics without partial state.</returns>
        DungeonSaveLoadResult Load();
    }
}
