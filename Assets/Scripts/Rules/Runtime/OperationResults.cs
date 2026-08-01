using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    internal interface ISettledOperationResult<TResult>
    {
        TResult Settle(RulesSnapshot snapshot);
    }

    /// <summary>
    /// Provides the common contract for every structurally distinct operation outcome.
    /// </summary>
    /// <typeparam name="TResult">The value type produced by a resolved operation.</typeparam>
    /// <remarks>
    /// Each outcome is represented by one sealed derived type. This prevents callers from reading
    /// a successful value or invalid reason from an outcome that cannot contain it. Facts include
    /// commits made directly by the operation and by every nested descendant that completed within
    /// its frame, including commits retained by interrupted or cancelled outcomes.
    /// </remarks>
    public abstract class OpResult<TResult>
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts = Array.AsReadOnly(
            Array.Empty<RuleFact>()
        );

        private protected OpResult(IReadOnlyList<RuleFact> facts)
        {
            Facts = facts ?? NoFacts;
        }

        /// <summary>
        /// Gets the final outcome category for diagnostics and compact status reporting.
        /// </summary>
        /// <remarks>
        /// Use the concrete result type when reading outcome-specific data. This value mirrors that
        /// type and is not a separate discriminator that controls the validity of another property.
        /// </remarks>
        public abstract OpStatus Status { get; }

        /// <summary>
        /// Gets the committed facts produced by this operation subtree in commit order.
        /// </summary>
        public IReadOnlyList<RuleFact> Facts { get; }

        /// <summary>
        /// Creates a resolved result with no attached facts.
        /// </summary>
        /// <param name="value">The value produced by the operation.</param>
        /// <returns>A resolved result. The dispatcher attaches subtree facts before returning it.</returns>
        public static ResolvedOpResult<TResult> Resolved(TResult value) =>
            new ResolvedOpResult<TResult>(value, NoFacts);

        /// <summary>
        /// Creates an invalid result with no attached facts.
        /// </summary>
        /// <param name="reason">A non-empty explanation suitable for diagnostics or callers.</param>
        /// <returns>An invalid operation result.</returns>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is empty or whitespace.</exception>
        public static InvalidOpResult<TResult> Invalid(string reason) =>
            new InvalidOpResult<TResult>(reason, NoFacts);

        /// <summary>
        /// Creates a result indicating that runtime behavior interrupted the operation.
        /// </summary>
        /// <returns>An interrupted operation result.</returns>
        public static InterruptedOpResult<TResult> Interrupted() =>
            new InterruptedOpResult<TResult>(NoFacts);

        /// <summary>
        /// Creates a result indicating that the operation was cancelled.
        /// </summary>
        /// <returns>A cancelled operation result.</returns>
        public static CancelledOpResult<TResult> Cancelled() =>
            new CancelledOpResult<TResult>(NoFacts);

        internal abstract OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts);
    }

    /// <summary>
    /// Represents an operation that legally resolved and produced a value.
    /// </summary>
    /// <typeparam name="TResult">The type of the resolved value.</typeparam>
    public sealed class ResolvedOpResult<TResult> : OpResult<TResult>
    {
        internal ResolvedOpResult(TResult value, IReadOnlyList<RuleFact> facts)
            : base(facts)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Resolved;

        /// <summary>
        /// Gets the value produced by the resolved operation.
        /// </summary>
        public TResult Value { get; }

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new ResolvedOpResult<TResult>(Value, facts);
    }

    /// <summary>
    /// Represents an operation that could not legally begin or produce a resolved value.
    /// </summary>
    /// <typeparam name="TResult">The value type the operation would produce if resolved.</typeparam>
    public sealed class InvalidOpResult<TResult> : OpResult<TResult>
    {
        internal InvalidOpResult(string reason, IReadOnlyList<RuleFact> facts)
            : base(facts)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("An invalid result requires a reason.", nameof(reason));

            Reason = reason;
        }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Invalid;

        /// <summary>
        /// Gets the explanation of why the operation was invalid.
        /// </summary>
        public string Reason { get; }

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new InvalidOpResult<TResult>(Reason, facts);
    }

    /// <summary>
    /// Represents an operation that legally began but was disrupted before normal resolution.
    /// </summary>
    /// <typeparam name="TResult">The value type the operation would produce if resolved.</typeparam>
    public sealed class InterruptedOpResult<TResult> : OpResult<TResult>
    {
        internal InterruptedOpResult(IReadOnlyList<RuleFact> facts)
            : base(facts) { }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Interrupted;

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new InterruptedOpResult<TResult>(facts);
    }

    /// <summary>
    /// Represents an explicitly cancelled operation that did not complete normal resolution.
    /// </summary>
    /// <typeparam name="TResult">The value type the operation would produce if resolved.</typeparam>
    public sealed class CancelledOpResult<TResult> : OpResult<TResult>
    {
        internal CancelledOpResult(IReadOnlyList<RuleFact> facts)
            : base(facts) { }

        /// <inheritdoc/>
        public override OpStatus Status => OpStatus.Cancelled;

        internal override OpResult<TResult> WithFacts(IReadOnlyList<RuleFact> facts) =>
            new CancelledOpResult<TResult>(facts);
    }
}
