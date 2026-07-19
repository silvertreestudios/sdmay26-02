using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Connects an action catalog entry to its preview, typed input workflow, and immutable root
    /// operation.
    /// </summary>
    /// <typeparam name="TSelection">The action-specific plain data produced by every choice.</typeparam>
    /// <typeparam name="TOp">The public root operation created from the selection.</typeparam>
    /// <typeparam name="TResult">The feature-specific result produced by the operation.</typeparam>
    /// <remarks>
    /// Availability and workflow construction use preview state. <see cref="CreateOp"/> does not
    /// make that preview authoritative; normal action validation still rejects stale choices.
    /// </remarks>
    public interface IActionDefinition<TSelection, TOp, TResult>
        where TOp : ActionOp<TResult>
    {
        /// <summary>Describes whether the action can be offered in the supplied preview.</summary>
        /// <param name="snapshot">The committed state used only for preview decisions.</param>
        /// <param name="actor">The creature for whom the action is offered.</param>
        /// <returns>A structurally available or unavailable value.</returns>
        ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor);

        /// <summary>Creates the ordered typed choices required before dispatch.</summary>
        /// <param name="snapshot">The committed state used to build the preview workflow.</param>
        /// <param name="actor">The creature making the choices.</param>
        /// <returns>A complete Unity-free workflow.</returns>
        SelectionWorkflow<TSelection> CreateSelectionWorkflow(
            RulesSnapshot snapshot,
            CreatureId actor
        );

        /// <summary>Creates one immutable public root operation from a completed selection.</summary>
        /// <param name="actor">The creature attempting the action.</param>
        /// <param name="selection">The complete action-specific selection.</param>
        /// <returns>The operation to submit to the rules dispatcher exactly once.</returns>
        TOp CreateOp(CreatureId actor, TSelection selection);
    }

    /// <summary>Provides structurally distinct action-availability states.</summary>
    /// <remarks>
    /// Callers inspect the concrete type instead of pairing a flag with an optional reason. A
    /// reason therefore exists only when the action is actually unavailable.
    /// </remarks>
    public abstract class ActionAvailability
    {
        private static readonly AvailableActionAvailability AvailableValue =
            new AvailableActionAvailability();

        private protected ActionAvailability() { }

        /// <summary>Gets the shared value representing an action that may be selected.</summary>
        public static AvailableActionAvailability Available => AvailableValue;

        /// <summary>Creates an unavailable state with an explanatory reason.</summary>
        /// <param name="reason">A non-empty explanation.</param>
        /// <returns>An unavailable value containing only the reason.</returns>
        /// <exception cref="ArgumentException"><paramref name="reason"/> is blank.</exception>
        public static UnavailableActionAvailability Unavailable(string reason) =>
            new UnavailableActionAvailability(reason);
    }

    /// <summary>Represents an action that may begin its selection workflow.</summary>
    public sealed class AvailableActionAvailability : ActionAvailability
    {
        internal AvailableActionAvailability() { }
    }

    /// <summary>Represents an action that cannot currently begin selection.</summary>
    public sealed class UnavailableActionAvailability
        : ActionAvailability,
            IEquatable<UnavailableActionAvailability>
    {
        /// <summary>Gets the non-empty explanation of why the action is unavailable.</summary>
        public string Reason { get; }

        internal UnavailableActionAvailability(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "An unavailable action requires a reason.",
                    nameof(reason)
                );
            Reason = reason.Trim();
        }

        /// <inheritdoc/>
        public bool Equals(UnavailableActionAvailability other) =>
            other != null && string.Equals(Reason, other.Reason, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is UnavailableActionAvailability other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Reason);
    }
}
