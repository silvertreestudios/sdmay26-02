using System;

namespace Game.Rules.Unity.Composition
{
    /// <summary>
    /// Owns one immutable detached value that may be adopted by the next successful enrollment.
    /// </summary>
    /// <remarks>
    /// A valid pending value is distinct from whether that value is empty. A lease pins one
    /// generation through enrollment validation, and consumes it only after the complete batch
    /// transfers to encounter ownership. Once enrollment has completed, a detached read without a
    /// release projection is rejected instead of silently returning the initial empty value.
    /// </remarks>
    internal sealed class PendingImmutableValue<TValue>
        where TValue : class
    {
        private readonly TValue emptyValue;
        private readonly string featureName;
        private TValue pendingValue;
        private long generation;
        private bool hasPending;
        private bool wasEnrolled;

        internal PendingImmutableValue(TValue emptyValue, string featureName)
        {
            this.emptyValue = emptyValue ?? throw new ArgumentNullException(nameof(emptyValue));
            this.featureName = string.IsNullOrWhiteSpace(featureName)
                ? throw new ArgumentException(
                    "A detached persistence feature name is required.",
                    nameof(featureName)
                )
                : featureName;
            pendingValue = emptyValue;
        }

        /// <summary>Gets whether an exact detached value is waiting for enrollment.</summary>
        internal bool HasPending => hasPending;

        /// <summary>Reads detached persistence state when no live authority is available.</summary>
        internal TValue ReadDetached()
        {
            if (hasPending)
                return pendingValue;
            if (!wasEnrolled)
                return emptyValue;
            throw new InvalidOperationException(
                $"{featureName} persistence capture requires attached authoritative combat rules."
            );
        }

        /// <summary>Replaces the pending value and invalidates every prior enrollment lease.</summary>
        internal void Replace(TValue value)
        {
            pendingValue = value ?? throw new ArgumentNullException(nameof(value));
            generation = checked(generation + 1);
            hasPending = true;
        }

        /// <summary>Leases the current generation without consuming its immutable value.</summary>
        internal bool TryLease(out PendingImmutableValueLease<TValue> lease)
        {
            if (!hasPending)
            {
                lease = null;
                return false;
            }
            lease = new PendingImmutableValueLease<TValue>(this, generation, pendingValue);
            return true;
        }

        /// <summary>Creates finalization for an enrollment that has no pending input.</summary>
        internal IUnityCombatantBatchFinalizationContribution CreateEnrollmentFinalization() =>
            new MarkEnrolledFinalization(this);

        internal void Validate(long expectedGeneration)
        {
            if (!hasPending || generation != expectedGeneration)
                throw new InvalidOperationException(
                    $"{featureName} restore input changed during enrollment."
                );
        }

        internal void ConsumeAfterEnrollment(long expectedGeneration)
        {
            // Finalization validates the full batch before any contribution applies. This method
            // therefore contains only non-failing state changes.
            pendingValue = emptyValue;
            hasPending = false;
            wasEnrolled = true;
        }

        private sealed class MarkEnrolledFinalization : IUnityCombatantBatchFinalizationContribution
        {
            private readonly PendingImmutableValue<TValue> owner;

            internal MarkEnrolledFinalization(PendingImmutableValue<TValue> owner) =>
                this.owner = owner;

            public void Validate() { }

            public void Apply() => owner.wasEnrolled = true;
        }
    }

    /// <summary>Pins one pending immutable value until complete batch finalization.</summary>
    internal sealed class PendingImmutableValueLease<TValue>
        : IUnityCombatantBatchFinalizationContribution
        where TValue : class
    {
        private readonly PendingImmutableValue<TValue> owner;
        private readonly long generation;

        internal PendingImmutableValueLease(
            PendingImmutableValue<TValue> owner,
            long generation,
            TValue value
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.generation = generation;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Gets the immutable value pinned by this generation.</summary>
        internal TValue Value { get; }

        /// <inheritdoc/>
        public void Validate() => owner.Validate(generation);

        /// <inheritdoc/>
        public void Apply() => owner.ConsumeAfterEnrollment(generation);
    }
}
