using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Runs and composes an ordered sequence of Unity-free selection steps.</summary>
    /// <typeparam name="TSelection">The complete plain value produced by the workflow.</typeparam>
    /// <remarks>
    /// Cancellation and invalidity short-circuit later steps and discard partial values. Mapping
    /// and composition run only after their preceding selection has completed.
    /// </remarks>
    public sealed class SelectionWorkflow<TSelection>
    {
        private readonly Func<
            ISelectionAdapter,
            CancellationToken,
            ValueTask<SelectionOutcome<TSelection>>
        > execute;

        internal SelectionWorkflow(
            Func<
                ISelectionAdapter,
                CancellationToken,
                ValueTask<SelectionOutcome<TSelection>>
            > execute
        ) => this.execute = execute ?? throw new ArgumentNullException(nameof(execute));

        /// <summary>Runs this workflow through one player, AI, replay, or test adapter.</summary>
        /// <param name="adapter">The non-null adapter that resolves primitive requests.</param>
        /// <returns>The completed, cancelled, or invalid workflow outcome.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="adapter"/> is <see langword="null"/>.</exception>
        public ValueTask<SelectionOutcome<TSelection>> Run(ISelectionAdapter adapter) =>
            Run(adapter, CancellationToken.None);

        /// <summary>
        /// Runs this workflow while allowing its presentation owner to discard a pending selection
        /// before it can advance to another step or create an operation.
        /// </summary>
        /// <param name="adapter">The non-null adapter that resolves primitive requests.</param>
        /// <param name="cancellationToken">
        /// A token that structurally cancels the workflow between asynchronous selection boundaries.
        /// Adapters do not need to observe the token themselves; a late result is discarded after it returns.
        /// </param>
        /// <returns>The completed, cancelled, or invalid workflow outcome.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="adapter"/> is <see langword="null"/>.</exception>
        public async ValueTask<SelectionOutcome<TSelection>> Run(
            ISelectionAdapter adapter,
            CancellationToken cancellationToken
        )
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            if (cancellationToken.IsCancellationRequested)
                return SelectionOutcome<TSelection>.Cancelled;

            SelectionOutcome<TSelection> outcome = await execute(adapter, cancellationToken);
            if (outcome == null)
                throw new InvalidOperationException("A selection workflow returned no outcome.");
            if (cancellationToken.IsCancellationRequested)
                return SelectionOutcome<TSelection>.Cancelled;
            return outcome;
        }

        /// <summary>Projects a completed workflow into another typed immutable value.</summary>
        /// <typeparam name="TResult">The projected result type.</typeparam>
        /// <param name="selector">The pure projection applied only to a completed value.</param>
        /// <returns>A workflow that preserves cancellation or invalidity structurally.</returns>
        public SelectionWorkflow<TResult> Select<TResult>(Func<TSelection, TResult> selector)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            return new SelectionWorkflow<TResult>(
                async (adapter, cancellationToken) =>
                {
                    SelectionOutcome<TSelection> outcome = await Run(adapter, cancellationToken);
                    if (outcome is CompletedSelectionOutcome<TSelection> completed)
                        return SelectionOutcome<TResult>.Completed(selector(completed.Selection));
                    if (outcome is InvalidSelectionOutcome<TSelection> invalid)
                        return SelectionOutcome<TResult>.Invalid(invalid.Reason);
                    if (outcome is CancelledSelectionOutcome<TSelection>)
                        return SelectionOutcome<TResult>.Cancelled;
                    throw new InvalidOperationException(
                        "The workflow returned an unknown outcome type."
                    );
                }
            );
        }

        /// <summary>Runs a dependent next workflow after this workflow completes.</summary>
        /// <typeparam name="TNext">The next completed selection type.</typeparam>
        /// <param name="next">
        /// Builds the next workflow from the completed first value. It is never called after
        /// cancellation or invalidity.
        /// </param>
        /// <returns>A workflow preserving both completed values in their execution order.</returns>
        public SelectionWorkflow<OrderedSelection<TSelection, TNext>> Then<TNext>(
            Func<TSelection, SelectionWorkflow<TNext>> next
        )
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            return new SelectionWorkflow<OrderedSelection<TSelection, TNext>>(
                async (adapter, cancellationToken) =>
                {
                    SelectionOutcome<TSelection> firstOutcome = await Run(
                        adapter,
                        cancellationToken
                    );
                    if (firstOutcome is InvalidSelectionOutcome<TSelection> firstInvalid)
                        return SelectionOutcome<OrderedSelection<TSelection, TNext>>.Invalid(
                            firstInvalid.Reason
                        );
                    if (firstOutcome is CancelledSelectionOutcome<TSelection>)
                        return SelectionOutcome<OrderedSelection<TSelection, TNext>>.Cancelled;
                    if (!(firstOutcome is CompletedSelectionOutcome<TSelection> firstCompleted))
                        throw new InvalidOperationException(
                            "The workflow returned an unknown outcome type."
                        );

                    SelectionWorkflow<TNext> nextWorkflow = next(firstCompleted.Selection);
                    if (nextWorkflow == null)
                        throw new InvalidOperationException(
                            "A composed workflow returned no next step."
                        );

                    SelectionOutcome<TNext> secondOutcome = await nextWorkflow.Run(
                        adapter,
                        cancellationToken
                    );
                    if (secondOutcome is InvalidSelectionOutcome<TNext> secondInvalid)
                        return SelectionOutcome<OrderedSelection<TSelection, TNext>>.Invalid(
                            secondInvalid.Reason
                        );
                    if (secondOutcome is CancelledSelectionOutcome<TNext>)
                        return SelectionOutcome<OrderedSelection<TSelection, TNext>>.Cancelled;
                    if (secondOutcome is CompletedSelectionOutcome<TNext> secondCompleted)
                        return SelectionOutcome<OrderedSelection<TSelection, TNext>>.Completed(
                            new OrderedSelection<TSelection, TNext>(
                                firstCompleted.Selection,
                                secondCompleted.Selection
                            )
                        );
                    throw new InvalidOperationException(
                        "The workflow returned an unknown outcome type."
                    );
                }
            );
        }
    }
}
