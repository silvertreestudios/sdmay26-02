using System;

namespace Game.Rules.Runtime
{
    /// <summary>Creates one-step and immediately invalid typed selection workflows.</summary>
    public static class SelectionWorkflow
    {
        /// <summary>Creates a one-step workflow for any concrete typed request.</summary>
        /// <typeparam name="TSelection">The complete answer type required by the request.</typeparam>
        /// <param name="request">The required immutable request resolved by the workflow boundary.</param>
        /// <returns>
        /// A workflow that verifies a completed resolver answer against
        /// <see cref="SelectionRequest{TSelection}.Accepts"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        public static SelectionWorkflow<TSelection> From<TSelection>(
            SelectionRequest<TSelection> request
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return new SelectionWorkflow<TSelection>(
                async (resolver, cancellationToken) =>
                {
                    SelectionOutcome<TSelection> outcome = await resolver.Select(
                        request,
                        cancellationToken
                    );
                    if (outcome == null)
                        throw new InvalidOperationException(
                            "A selection resolver returned no outcome."
                        );
                    if (
                        outcome is CompletedSelectionOutcome<TSelection> completed
                        && !request.Accepts(completed.Selection)
                    )
                    {
                        return SelectionOutcome<TSelection>.Invalid(
                            "A selection resolver returned a value outside the request."
                        );
                    }

                    return outcome;
                }
            );
        }

        /// <summary>Creates a workflow that is already invalid and invokes no resolver.</summary>
        /// <typeparam name="TSelection">The selection type that cannot be produced.</typeparam>
        /// <param name="reason">A non-empty explanation.</param>
        /// <returns>An immediately invalid workflow.</returns>
        public static SelectionWorkflow<TSelection> Invalid<TSelection>(string reason)
        {
            InvalidSelectionOutcome<TSelection> invalid = SelectionOutcome<TSelection>.Invalid(
                reason
            );
            return new SelectionWorkflow<TSelection>(
                (_, _) =>
                    new System.Threading.Tasks.ValueTask<SelectionOutcome<TSelection>>(invalid)
            );
        }
    }
}
