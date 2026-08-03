using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Describes which exact action definitions may spend an optional action resource.
    /// </summary>
    /// <remarks>
    /// <see cref="None"/> represents an absent optional resource. A present resource is either
    /// <see cref="Unrestricted"/> or a nonempty restricted set created by <see cref="Restricted"/>.
    /// This value answers definition membership only. A restricted PF2e action resource must also
    /// require an exact single-action cost when it is spent; an allowed subordinate action does not
    /// authorize the two- or three-action activity that contains it.
    /// </remarks>
    public sealed class ActionAllowance : IEquatable<ActionAllowance>
    {
        private readonly IReadOnlyList<ActionDefinitionId> allowedActions;
        private readonly HashSet<ActionDefinitionId> allowedActionLookup;

        private ActionAllowance(bool isUnrestricted, ActionDefinitionId[] allowedActions)
        {
            IsUnrestricted = isUnrestricted;
            this.allowedActions = Array.AsReadOnly(allowedActions);
            allowedActionLookup = new HashSet<ActionDefinitionId>(allowedActions);
        }

        /// <summary>Gets the value representing an absent optional action resource.</summary>
        public static ActionAllowance None { get; } =
            new ActionAllowance(false, Array.Empty<ActionDefinitionId>());

        /// <summary>Gets an allowance that permits every valid action definition.</summary>
        public static ActionAllowance Unrestricted { get; } =
            new ActionAllowance(true, Array.Empty<ActionDefinitionId>());

        /// <summary>Creates an allowance restricted to a nonempty set of action definitions.</summary>
        /// <param name="allowedActions">The definitions that may spend the resource.</param>
        /// <returns>An immutable allowance in canonical ordinal order.</returns>
        public static ActionAllowance Restricted(IEnumerable<ActionDefinitionId> allowedActions)
        {
            if (allowedActions == null)
                throw new ArgumentNullException(nameof(allowedActions));

            ActionDefinitionId[] copied = allowedActions.ToArray();
            if (copied.Length == 0)
                throw new ArgumentException(
                    "A restricted allowance must contain at least one action definition.",
                    nameof(allowedActions)
                );
            if (copied.Any(action => action.IsEmpty))
                throw new ArgumentException(
                    "An allowance cannot contain an empty action definition.",
                    nameof(allowedActions)
                );

            return new ActionAllowance(
                false,
                copied.Distinct().OrderBy(action => action.Value, StringComparer.Ordinal).ToArray()
            );
        }

        /// <summary>Gets whether this value represents an absent optional resource.</summary>
        public bool IsNone => !IsUnrestricted && allowedActions.Count == 0;

        /// <summary>Gets whether a present resource permits only listed definitions.</summary>
        public bool IsRestricted => !IsUnrestricted && allowedActions.Count > 0;

        /// <summary>Gets whether the present resource permits every action definition.</summary>
        public bool IsUnrestricted { get; }

        /// <summary>
        /// Gets the canonical allowed definitions, empty for <see cref="None"/> and
        /// <see cref="Unrestricted"/>.
        /// </summary>
        public IReadOnlyList<ActionDefinitionId> AllowedActions => allowedActions;

        /// <summary>Tests whether this allowance permits the supplied exact action definition.</summary>
        /// <param name="action">The valid action definition to inspect.</param>
        /// <returns><see langword="true"/> when the definition is permitted.</returns>
        public bool Allows(ActionDefinitionId action)
        {
            if (action.IsEmpty)
                throw new ArgumentException("An action definition is required.", nameof(action));
            return IsUnrestricted || allowedActionLookup.Contains(action);
        }

        /// <summary>
        /// Tests whether this allowance may pay the complete action-economy cost of an invocation.
        /// </summary>
        /// <param name="action">The exact top-level action definition being invoked.</param>
        /// <param name="profile">The invocation's resolved action profile.</param>
        /// <returns>
        /// <see langword="true"/> only for an allowed definition whose complete cost is exactly one
        /// action.
        /// </returns>
        public bool Allows(ActionDefinitionId action, ActionProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            return profile.Cost == ActionCost.One && Allows(action);
        }

        /// <summary>Combines two optional allowances, with unrestricted permission dominating.</summary>
        /// <param name="other">The allowance to combine with this value.</param>
        /// <returns>The canonical union of both allowances.</returns>
        public ActionAllowance Union(ActionAllowance other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));
            if (IsUnrestricted || other.IsUnrestricted)
                return Unrestricted;
            if (allowedActions.Count == 0)
                return other;
            if (other.allowedActions.Count == 0)
                return this;
            return Restricted(allowedActions.Concat(other.allowedActions));
        }

        /// <inheritdoc/>
        public bool Equals(ActionAllowance other) =>
            other != null
            && IsUnrestricted == other.IsUnrestricted
            && allowedActions.SequenceEqual(other.allowedActions);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionAllowance other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(IsUnrestricted);
            foreach (ActionDefinitionId action in allowedActions)
                hash.Add(action);
            return hash.ToHashCode();
        }

        /// <summary>Compares two allowances by value.</summary>
        public static bool operator ==(ActionAllowance left, ActionAllowance right) =>
            ReferenceEquals(left, right) || (left?.Equals(right) ?? false);

        /// <summary>Compares two allowances by value.</summary>
        public static bool operator !=(ActionAllowance left, ActionAllowance right) =>
            !(left == right);
    }

    /// <summary>
    /// Connects an action's preview, typed input workflow, and immutable root operation without
    /// choosing a presentation or registration model.
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
