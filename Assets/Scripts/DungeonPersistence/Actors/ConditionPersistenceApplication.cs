using System;

namespace Game.DungeonPersistence.Actors
{
    /// <summary>
    /// Exposes one live condition application to persistence without exposing the condition
    /// component's mutable dictionary.
    /// </summary>
    internal sealed class ConditionPersistenceApplication
    {
        /// <summary>Creates one immutable live condition application.</summary>
        /// <param name="conditionId">The non-blank condition name or definition ID.</param>
        /// <param name="value">The non-negative valued-condition amount.</param>
        /// <param name="source">
        /// The shared live source, or <see langword="null"/> for a legacy intrinsic application.
        /// </param>
        /// <param name="applicationId">
        /// Stable restored application identity, or an empty string for a new live application.
        /// </param>
        public ConditionPersistenceApplication(
            string conditionId,
            int value,
            ConditionSource source,
            string applicationId = ""
        )
        {
            if (string.IsNullOrWhiteSpace(conditionId))
                throw new ArgumentException("A condition ID is required.", nameof(conditionId));
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            ConditionId = conditionId.Trim();
            Value = value;
            Source = source;
            ApplicationId = applicationId?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Gets the stable application identity after restoration, or an empty string for a new
        /// live application that has not yet been captured.
        /// </summary>
        public string ApplicationId { get; private set; }

        /// <summary>Gets the condition name or definition ID.</summary>
        public string ConditionId { get; }

        /// <summary>Gets the non-negative valued-condition amount.</summary>
        public int Value { get; }

        /// <summary>Gets the shared live source, or null for a legacy intrinsic application.</summary>
        public ConditionSource Source { get; }

        internal void EnsurePersistenceIdentity(string applicationId)
        {
            string normalized = applicationId?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException(
                    "A condition application identity is required.",
                    nameof(applicationId)
                );
            if (ApplicationId.Length > 0 && ApplicationId != normalized)
                throw new InvalidOperationException(
                    "A condition application persistence identity cannot be replaced."
                );
            ApplicationId = normalized;
        }
    }
}
