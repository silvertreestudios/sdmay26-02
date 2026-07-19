using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies the stable rules or content-defined question represented by a choice request.
    /// </summary>
    /// <remarks>
    /// Presentation adapters use this identity to select localized text and visuals. The runtime
    /// intentionally stores no player-facing prose or Unity presentation objects in the request.
    /// </remarks>
    public readonly struct ChoiceRequestId : IEquatable<ChoiceRequestId>
    {
        private readonly string value;

        /// <summary>Gets the stable, non-empty request identifier.</summary>
        /// <remarks>The uninitialized default identifier returns an empty string.</remarks>
        public string Value => value ?? string.Empty;

        /// <summary>Gets whether this is the uninitialized default identifier.</summary>
        public bool IsEmpty => Value.Length == 0;

        /// <summary>Creates a stable choice-request identifier.</summary>
        /// <param name="value">The non-empty identifier text.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
        public ChoiceRequestId(string value) =>
            this.value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(ChoiceRequestId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ChoiceRequestId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value);

        /// <inheritdoc/>
        public override string ToString() => Value;

        /// <summary>Compares two request identifiers by stable value.</summary>
        /// <param name="left">The first identifier.</param>
        /// <param name="right">The second identifier.</param>
        /// <returns><see langword="true"/> when both identifiers have the same value.</returns>
        public static bool operator ==(ChoiceRequestId left, ChoiceRequestId right) =>
            left.Equals(right);

        /// <summary>Compares two request identifiers by stable value.</summary>
        /// <param name="left">The first identifier.</param>
        /// <param name="right">The second identifier.</param>
        /// <returns><see langword="true"/> when the identifiers have different values.</returns>
        public static bool operator !=(ChoiceRequestId left, ChoiceRequestId right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Describes one immutable, typed set of choices that a prompt adapter can present or evaluate.
    /// </summary>
    /// <typeparam name="TChoice">The plain immutable data type used for each possible choice.</typeparam>
    /// <remarks>
    /// The collection is copied and duplicate values are rejected so adapter behavior is
    /// deterministic. A normal decline belongs in <typeparamref name="TChoice"/> itself, such as
    /// <see langword="false"/> for a yes-or-no reaction; it is still a selected, resolved choice.
    /// </remarks>
    public sealed class ChoiceRequest<TChoice>
    {
        private readonly IReadOnlyList<TChoice> choices;

        /// <summary>Gets the stable question identity used by adapters and replay data.</summary>
        public ChoiceRequestId Id { get; }

        /// <summary>Gets the non-empty choices in deterministic presentation order.</summary>
        public IReadOnlyList<TChoice> Choices => choices;

        /// <summary>Creates an immutable typed choice request.</summary>
        /// <param name="id">The stable question identity.</param>
        /// <param name="choices">The distinct, non-null choices in presentation order.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="id"/> is empty, or the choices are empty, duplicated, or contain a
        /// <see langword="null"/> value.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="choices"/> is <see langword="null"/>.</exception>
        public ChoiceRequest(ChoiceRequestId id, IEnumerable<TChoice> choices)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A choice request requires a stable identity.", nameof(id));
            if (choices == null)
                throw new ArgumentNullException(nameof(choices));

            TChoice[] copied = choices.ToArray();
            if (copied.Length == 0)
                throw new ArgumentException("A choice request requires at least one choice.", nameof(choices));

            HashSet<TChoice> unique = new HashSet<TChoice>();
            foreach (TChoice choice in copied)
            {
                if (ReferenceEquals(choice, null))
                    throw new ArgumentException("Choice requests cannot contain null values.", nameof(choices));
                if (!unique.Add(choice))
                    throw new ArgumentException("Choice requests cannot contain duplicate values.", nameof(choices));
            }

            Id = id;
            this.choices = Array.AsReadOnly(copied);
        }

        internal bool Contains(TChoice choice) => choices.Contains(choice);
    }

    /// <summary>Identifies a recoverable external reason that a prompt adapter could not answer.</summary>
    public enum PromptAdapterFailureKind
    {
        /// <summary>The adapter did not answer within its configured decision window.</summary>
        TimedOut = 1,

        /// <summary>The player, controller, or replay source disconnected before answering.</summary>
        Disconnected = 2
    }

    /// <summary>
    /// Carries a typed prompt-adapter failure without turning an expected external condition into an exception.
    /// </summary>
    public readonly struct PromptAdapterFailure : IEquatable<PromptAdapterFailure>
    {
        private readonly string reason;

        /// <summary>Gets the machine-readable failure category.</summary>
        public PromptAdapterFailureKind Kind { get; }

        /// <summary>Gets the non-empty diagnostic explanation supplied by the adapter boundary.</summary>
        /// <remarks>The uninitialized default failure returns an empty string.</remarks>
        public string Reason => reason ?? string.Empty;

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => Reason.Length == 0;

        /// <summary>Creates a typed prompt-adapter failure.</summary>
        /// <param name="kind">The timeout or disconnect category.</param>
        /// <param name="reason">A non-empty diagnostic explanation.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is undefined.</exception>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
        public PromptAdapterFailure(PromptAdapterFailureKind kind, string reason)
        {
            if (!Enum.IsDefined(typeof(PromptAdapterFailureKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A prompt-adapter failure requires a reason.", nameof(reason));

            Kind = kind;
            this.reason = reason.Trim();
        }

        /// <inheritdoc/>
        public bool Equals(PromptAdapterFailure other) =>
            Kind == other.Kind && string.Equals(Reason, other.Reason, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PromptAdapterFailure other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Kind, Reason);

        /// <summary>Compares the failure category and reason.</summary>
        /// <param name="left">The first failure.</param>
        /// <param name="right">The second failure.</param>
        /// <returns><see langword="true"/> when both failures carry the same category and reason.</returns>
        public static bool operator ==(PromptAdapterFailure left, PromptAdapterFailure right) =>
            left.Equals(right);

        /// <summary>Compares the failure category and reason.</summary>
        /// <param name="left">The first failure.</param>
        /// <param name="right">The second failure.</param>
        /// <returns><see langword="true"/> when the failures differ.</returns>
        public static bool operator !=(PromptAdapterFailure left, PromptAdapterFailure right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Provides the common contract for the structurally distinct resolved outcomes of a prompt.
    /// </summary>
    /// <typeparam name="TChoice">The immutable choice data type declared by the request.</typeparam>
    /// <remarks>
    /// Selected, unavailable, and adapter-failure outcomes all mean the prompt operation legally
    /// resolved. An explicit workflow cancellation is represented by
    /// <see cref="CancelledOpResult{TResult}"/> around this value type instead.
    /// </remarks>
    public abstract class ChoiceResult<TChoice>
    {
        private protected ChoiceResult()
        {
        }

        /// <summary>Creates a resolved selection, including a normal decline value.</summary>
        /// <param name="choice">One value declared by the corresponding request.</param>
        /// <returns>A result exposing the selected choice.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="choice"/> is <see langword="null"/>.</exception>
        public static SelectedChoiceResult<TChoice> Selected(TChoice choice) =>
            new SelectedChoiceResult<TChoice>(choice);

        /// <summary>Creates a resolved result indicating that no adapter can currently present the request.</summary>
        /// <param name="reason">A non-empty diagnostic explanation.</param>
        /// <returns>An unavailable result with no selected value.</returns>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
        public static UnavailableChoiceResult<TChoice> Unavailable(string reason) =>
            new UnavailableChoiceResult<TChoice>(reason);

        /// <summary>Creates a resolved timeout or disconnect result.</summary>
        /// <param name="failure">The typed adapter-boundary failure.</param>
        /// <returns>A failed prompt result with no selected value.</returns>
        /// <exception cref="ArgumentException"><paramref name="failure"/> is empty.</exception>
        public static FailedChoiceResult<TChoice> Failed(PromptAdapterFailure failure) =>
            new FailedChoiceResult<TChoice>(failure);
    }

    /// <summary>Represents a prompt that resolved with one request-declared choice.</summary>
    /// <typeparam name="TChoice">The immutable selected value type.</typeparam>
    public sealed class SelectedChoiceResult<TChoice> : ChoiceResult<TChoice>
    {
        /// <summary>Gets the selected value, including a content-defined decline value.</summary>
        public TChoice Choice { get; }

        internal SelectedChoiceResult(TChoice choice)
        {
            if (ReferenceEquals(choice, null))
                throw new ArgumentNullException(nameof(choice));
            Choice = choice;
        }
    }

    /// <summary>Represents a prompt that legally resolved but had no available adapter.</summary>
    /// <typeparam name="TChoice">The choice type the unavailable prompt would have returned.</typeparam>
    public sealed class UnavailableChoiceResult<TChoice> : ChoiceResult<TChoice>
    {
        /// <summary>Gets the non-empty diagnostic explanation.</summary>
        public string Reason { get; }

        internal UnavailableChoiceResult(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("An unavailable prompt requires a reason.", nameof(reason));
            Reason = reason.Trim();
        }
    }

    /// <summary>Represents a prompt that legally resolved with a typed adapter-boundary failure.</summary>
    /// <typeparam name="TChoice">The choice type the failed prompt would have returned.</typeparam>
    public sealed class FailedChoiceResult<TChoice> : ChoiceResult<TChoice>
    {
        /// <summary>Gets the timeout or disconnect information.</summary>
        public PromptAdapterFailure Failure { get; }

        internal FailedChoiceResult(PromptAdapterFailure failure)
        {
            if (failure.IsEmpty)
                throw new ArgumentException("A failed prompt requires failure details.", nameof(failure));
            Failure = failure;
        }
    }
}
