using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Identifies one action-bar entry independently of its display text or implementation kind.
    /// </summary>
    /// <remarks>
    /// Use the same explicit key for a definition-backed action and the legacy action it replaces.
    /// The catalog never infers identity from localized or otherwise changeable display names.
    /// </remarks>
    public readonly struct ActionBarEntryKey : IEquatable<ActionBarEntryKey>
    {
        private readonly string value;

        /// <summary>
        /// Initializes a stable action-bar key.
        /// </summary>
        /// <param name="value">The stable, non-empty key text.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is empty or whitespace.</exception>
        public ActionBarEntryKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "An action-bar entry key cannot be blank.",
                    nameof(value)
                );

            this.value = value.Trim();
        }

        /// <summary>
        /// Gets the stable key text, or an empty string for the uninitialized default value.
        /// </summary>
        public string Value => value ?? string.Empty;

        /// <summary>
        /// Gets whether this is the uninitialized default key.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(value);

        /// <inheritdoc/>
        public bool Equals(ActionBarEntryKey other) =>
            string.Equals(value, other.value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionBarEntryKey other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        /// <inheritdoc/>
        public override string ToString() => Value;

        /// <summary>
        /// Compares two keys by their ordinal stable values.
        /// </summary>
        public static bool operator ==(ActionBarEntryKey left, ActionBarEntryKey right) =>
            left.Equals(right);

        /// <summary>
        /// Compares two keys by their ordinal stable values.
        /// </summary>
        public static bool operator !=(ActionBarEntryKey left, ActionBarEntryKey right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Supplies the metadata shared by legacy and definition-backed action-bar entries.
    /// </summary>
    public interface IActionBarEntry
    {
        /// <summary>
        /// Gets the explicit stable identity used only for replacement and catalog lookup.
        /// </summary>
        ActionBarEntryKey Key { get; }

        /// <summary>
        /// Gets the player-facing label. This text does not participate in entry identity.
        /// </summary>
        string DisplayName { get; }
    }

    /// <summary>
    /// Provides the type-erased action-bar contract for a definition-backed rules action.
    /// </summary>
    /// <remarks>
    /// The generic definition, selection, operation, and result types remain connected inside the
    /// implementation. Consumers never cast a selection payload or treat a legacy action as an
    /// operation.
    /// </remarks>
    public interface IDefinitionActionBarEntry : IActionBarEntry
    {
        /// <summary>
        /// Recomputes preview availability from the dispatcher's current immutable snapshot.
        /// </summary>
        /// <returns>A structural available or unavailable result.</returns>
        ActionAvailability GetAvailability();

        /// <summary>
        /// Runs typed selection and, only when it completes, creates and dispatches one root operation.
        /// </summary>
        /// <returns>The structural result of preview, selection, or dispatch.</returns>
        ValueTask<ActionBarExecutionOutcome> Execute();
    }

    /// <summary>
    /// Identifies where an invalid action-bar execution was rejected.
    /// </summary>
    public enum ActionBarInvalidSource
    {
        /// <summary>
        /// The selection workflow rejected a cancelled, malformed, or out-of-request adapter value.
        /// </summary>
        Selection,

        /// <summary>
        /// The authoritative rules dispatcher rejected the completed operation.
        /// </summary>
        Dispatcher,
    }

    /// <summary>
    /// Provides the common base for structurally distinct action-bar execution outcomes.
    /// </summary>
    public abstract class ActionBarExecutionOutcome
    {
        private protected ActionBarExecutionOutcome() { }
    }

    internal static class ActionBarOutcomeFacts
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts = Array.AsReadOnly(
            Array.Empty<RuleFact>()
        );

        public static IReadOnlyList<RuleFact> Copy(IReadOnlyList<RuleFact> facts)
        {
            if (facts == null)
                throw new ArgumentNullException(nameof(facts));
            if (facts.Count == 0)
                return NoFacts;

            RuleFact[] copy = new RuleFact[facts.Count];
            for (int index = 0; index < facts.Count; index++)
            {
                copy[index] =
                    facts[index]
                    ?? throw new ArgumentException(
                        "An outcome cannot contain a null fact.",
                        nameof(facts)
                    );
            }

            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Reports that execution did not start because its current snapshot preview was unavailable.
    /// </summary>
    public sealed class UnavailableActionBarExecutionOutcome : ActionBarExecutionOutcome
    {
        /// <summary>
        /// Initializes an unavailable outcome.
        /// </summary>
        /// <param name="reason">The non-empty player-facing or diagnostic explanation.</param>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is empty or whitespace.</exception>
        public UnavailableActionBarExecutionOutcome(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "An unavailable outcome requires a reason.",
                    nameof(reason)
                );

            Reason = reason;
        }

        /// <summary>
        /// Gets why the current preview cannot begin selection.
        /// </summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Reports that the user or adapter cancelled selection before any operation was created.
    /// </summary>
    public sealed class CancelledActionBarExecutionOutcome : ActionBarExecutionOutcome
    {
        internal CancelledActionBarExecutionOutcome() { }
    }

    /// <summary>
    /// Reports a structural selection failure or an authoritative invalid operation result.
    /// </summary>
    public sealed class InvalidActionBarExecutionOutcome : ActionBarExecutionOutcome
    {
        internal InvalidActionBarExecutionOutcome(
            string reason,
            ActionBarInvalidSource source,
            IReadOnlyList<RuleFact> facts
        )
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "An invalid outcome requires a reason.",
                    nameof(reason)
                );

            Reason = reason;
            Source = source;
            Facts = ActionBarOutcomeFacts.Copy(facts);
        }

        /// <summary>
        /// Gets why selection or authoritative dispatch was invalid.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Gets whether selection or dispatcher validation supplied <see cref="Reason"/>.
        /// </summary>
        public ActionBarInvalidSource Source { get; }

        /// <summary>
        /// Gets committed facts retained by an invalid dispatch. Selection failures always expose an empty list.
        /// </summary>
        public IReadOnlyList<RuleFact> Facts { get; }
    }

    /// <summary>
    /// Reports that a root operation was dispatched and was not rejected as invalid.
    /// </summary>
    /// <remarks>
    /// <see cref="Status"/> may be resolved, interrupted, or cancelled. Those statuses all differ
    /// from pre-dispatch selection cancellation because an operation frame was actually created.
    /// </remarks>
    public sealed class DispatchedActionBarExecutionOutcome : ActionBarExecutionOutcome
    {
        internal DispatchedActionBarExecutionOutcome(OpStatus status, IReadOnlyList<RuleFact> facts)
        {
            if (status == OpStatus.Invalid)
                throw new ArgumentException(
                    "Invalid dispatches use the structural invalid outcome.",
                    nameof(status)
                );

            Status = status;
            Facts = ActionBarOutcomeFacts.Copy(facts);
        }

        /// <summary>
        /// Gets the authoritative dispatcher status.
        /// </summary>
        public OpStatus Status { get; }

        /// <summary>
        /// Gets a defensive copy of facts committed by the dispatched operation subtree.
        /// </summary>
        public IReadOnlyList<RuleFact> Facts { get; }
    }
}
