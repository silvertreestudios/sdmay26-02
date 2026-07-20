using System;

namespace Game.Rules.Runtime
{
    /// <summary>Provides structurally distinct completed, cancelled, and invalid outcomes.</summary>
    /// <typeparam name="TSelection">The complete plain data produced by the workflow.</typeparam>
    /// <remarks>
    /// Callers cannot read a missing selection from cancellation or pair an invalid reason with a
    /// supposedly completed value because each case has its own concrete type.
    /// </remarks>
    public abstract class SelectionOutcome<TSelection>
    {
        private static readonly CancelledSelectionOutcome<TSelection> CancelledValue =
            new CancelledSelectionOutcome<TSelection>();

        private protected SelectionOutcome() { }

        /// <summary>Creates a completed outcome containing the final typed selection.</summary>
        /// <param name="selection">The complete non-null selection.</param>
        /// <returns>A completed workflow outcome.</returns>
        public static CompletedSelectionOutcome<TSelection> Completed(TSelection selection) =>
            new CompletedSelectionOutcome<TSelection>(selection);

        /// <summary>Gets the shared outcome representing explicit cancellation.</summary>
        public static CancelledSelectionOutcome<TSelection> Cancelled => CancelledValue;

        /// <summary>Creates an invalid outcome explaining why no operation can be created.</summary>
        /// <param name="reason">A non-empty diagnostic explanation.</param>
        /// <returns>An invalid workflow outcome.</returns>
        public static InvalidSelectionOutcome<TSelection> Invalid(string reason) =>
            new InvalidSelectionOutcome<TSelection>(reason);
    }

    /// <summary>Represents a workflow that completed every step with valid typed data.</summary>
    /// <typeparam name="TSelection">The complete plain selection type.</typeparam>
    public sealed class CompletedSelectionOutcome<TSelection> : SelectionOutcome<TSelection>
    {
        /// <summary>Gets the selection from which a root operation may be created.</summary>
        public TSelection Selection { get; }

        internal CompletedSelectionOutcome(TSelection selection)
        {
            if (ReferenceEquals(selection, null))
                throw new ArgumentNullException(nameof(selection));
            Selection = selection;
        }
    }

    /// <summary>Represents an explicitly cancelled workflow with no partial selection.</summary>
    /// <typeparam name="TSelection">The selection type that was not produced.</typeparam>
    public sealed class CancelledSelectionOutcome<TSelection> : SelectionOutcome<TSelection>
    {
        internal CancelledSelectionOutcome() { }
    }

    /// <summary>Represents a workflow that could not produce a valid complete selection.</summary>
    /// <typeparam name="TSelection">The selection type that was not produced.</typeparam>
    public sealed class InvalidSelectionOutcome<TSelection> : SelectionOutcome<TSelection>
    {
        /// <summary>Gets the non-empty explanation of why selection could not complete.</summary>
        public string Reason { get; }

        internal InvalidSelectionOutcome(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "An invalid selection requires a reason.",
                    nameof(reason)
                );
            Reason = reason.Trim();
        }
    }
}
