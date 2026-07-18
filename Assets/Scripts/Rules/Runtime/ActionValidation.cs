using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Represents the outcome of one pure action validator.
    /// </summary>
    /// <remarks>
    /// Structural valid and invalid cases prevent a successful validation from carrying a meaningless
    /// nullable rejection reason.
    /// </remarks>
    public abstract class ActionValidationResult
    {
        private ActionValidationResult()
        {
        }

        /// <summary>
        /// Gets the reusable successful validation result.
        /// </summary>
        public static ValidActionValidationResult Valid { get; } =
            new ValidActionValidationResult();

        /// <summary>
        /// Creates a validation result that stops the action before any cost or lifecycle window.
        /// </summary>
        /// <param name="reason">A non-empty caller-facing explanation.</param>
        /// <returns>An invalid structural result.</returns>
        public static InvalidActionValidationResult Invalid(string reason) =>
            new InvalidActionValidationResult(reason);

        /// <summary>
        /// Represents a successful action validation.
        /// </summary>
        public sealed class ValidActionValidationResult : ActionValidationResult
        {
            internal ValidActionValidationResult()
            {
            }
        }

        /// <summary>
        /// Represents an action that cannot legally begin.
        /// </summary>
        public sealed class InvalidActionValidationResult : ActionValidationResult
        {
            internal InvalidActionValidationResult(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                    throw new ArgumentException("An invalid action requires a reason.", nameof(reason));
                Reason = reason;
            }

            /// <summary>
            /// Gets the reason the action cannot begin.
            /// </summary>
            public string Reason { get; }
        }
    }

    /// <summary>
    /// Performs a side-effect-free legality check for one concrete action operation.
    /// </summary>
    /// <typeparam name="TOp">The concrete action type being validated.</typeparam>
    public interface IActionValidator<TOp>
        where TOp : IRuleOp
    {
        /// <summary>
        /// Validates the frozen action frame against its starting snapshot.
        /// </summary>
        /// <param name="frame">The action frame with its effective profile already frozen.</param>
        /// <param name="snapshot">The same authoritative snapshot used to resolve the profile.</param>
        /// <returns>A valid result or the first reason the action cannot legally begin.</returns>
        ActionValidationResult Validate(OpFrame<TOp> frame, RulesSnapshot snapshot);
    }

}
