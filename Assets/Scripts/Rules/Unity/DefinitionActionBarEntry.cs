using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Bridges one fully typed action definition into a heterogeneous Unity action bar.
    /// </summary>
    /// <typeparam name="TSelection">The exact immutable selection produced by the workflow.</typeparam>
    /// <typeparam name="TOp">The exact root action operation created by the definition.</typeparam>
    /// <typeparam name="TResult">The operation's successful result type.</typeparam>
    public sealed class DefinitionActionBarEntry<TSelection, TOp, TResult>
        : IDefinitionActionBarEntry
        where TOp : ActionOp<TResult>
    {
        private readonly IActionDefinition<TSelection, TOp, TResult> definition;
        private readonly CreatureId actor;
        private readonly RuleDispatcher dispatcher;
        private readonly ISelectionAdapter selectionAdapter;

        /// <summary>
        /// Initializes a definition-backed entry with all dependencies supplied by the encounter composition root.
        /// </summary>
        /// <param name="key">The explicit stable key used to replace a matching legacy entry.</param>
        /// <param name="displayName">The non-empty player-facing label.</param>
        /// <param name="definition">The typed definition that owns availability, selection, and operation creation.</param>
        /// <param name="actor">The stable rules identity of the acting creature.</param>
        /// <param name="dispatcher">The encounter dispatcher that owns the authoritative snapshot and root dispatch.</param>
        /// <param name="selectionAdapter">The player, AI, replay, or scripted selection adapter.</param>
        /// <exception cref="ArgumentException"><paramref name="displayName"/> is empty or <paramref name="actor"/> is uninitialized.</exception>
        /// <exception cref="ArgumentNullException">A required reference dependency is <see langword="null"/>.</exception>
        public DefinitionActionBarEntry(
            ActionBarEntryKey key,
            string displayName,
            IActionDefinition<TSelection, TOp, TResult> definition,
            CreatureId actor,
            RuleDispatcher dispatcher,
            ISelectionAdapter selectionAdapter
        )
        {
            if (key.IsEmpty)
                throw new ArgumentException(
                    "A definition entry requires a stable key.",
                    nameof(key)
                );
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException(
                    "An action-bar display name cannot be blank.",
                    nameof(displayName)
                );
            if (actor.IsEmpty)
                throw new ArgumentException("A definition entry requires an actor.", nameof(actor));

            Key = key;
            DisplayName = displayName;
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.actor = actor;
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.selectionAdapter =
                selectionAdapter ?? throw new ArgumentNullException(nameof(selectionAdapter));
        }

        /// <inheritdoc/>
        public ActionBarEntryKey Key { get; }

        /// <inheritdoc/>
        public string DisplayName { get; }

        /// <inheritdoc/>
        public ActionAvailability GetAvailability() =>
            definition.GetAvailability(dispatcher.Snapshot, actor)
            ?? throw new InvalidOperationException(
                "An action definition returned no availability result."
            );

        /// <inheritdoc/>
        public async ValueTask<ActionBarExecutionOutcome> Execute()
        {
            RulesSnapshot previewSnapshot = dispatcher.Snapshot;
            ActionAvailability availability =
                definition.GetAvailability(previewSnapshot, actor)
                ?? throw new InvalidOperationException(
                    "An action definition returned no availability result."
                );
            if (availability is UnavailableActionAvailability unavailable)
                return new UnavailableActionBarExecutionOutcome(unavailable.Reason);
            if (!(availability is AvailableActionAvailability))
                throw new InvalidOperationException(
                    "An action definition returned an unknown availability case."
                );

            SelectionWorkflow<TSelection> workflow =
                definition.CreateSelectionWorkflow(previewSnapshot, actor)
                ?? throw new InvalidOperationException(
                    "An action definition returned no selection workflow."
                );
            SelectionOutcome<TSelection> selection =
                await workflow.Run(selectionAdapter)
                ?? throw new InvalidOperationException("A selection workflow returned no outcome.");

            if (selection is CancelledSelectionOutcome<TSelection>)
                return new CancelledActionBarExecutionOutcome();
            if (selection is InvalidSelectionOutcome<TSelection> invalidSelection)
            {
                return new InvalidActionBarExecutionOutcome(
                    invalidSelection.Reason,
                    ActionBarInvalidSource.Selection,
                    Array.Empty<RuleFact>()
                );
            }
            if (!(selection is CompletedSelectionOutcome<TSelection> completed))
                throw new InvalidOperationException(
                    "A selection workflow returned an unknown outcome case."
                );

            TOp operation =
                definition.CreateOp(actor, completed.Selection)
                ?? throw new InvalidOperationException(
                    "An action definition returned no operation."
                );
            OpResult<TResult> result =
                await dispatcher.Dispatch(operation)
                ?? throw new InvalidOperationException(
                    "The dispatcher returned no operation result."
                );

            if (result is InvalidOpResult<TResult> invalidDispatch)
            {
                return new InvalidActionBarExecutionOutcome(
                    invalidDispatch.Reason,
                    ActionBarInvalidSource.Dispatcher,
                    invalidDispatch.Facts
                );
            }

            return new DispatchedActionBarExecutionOutcome(result.Status, result.Facts);
        }
    }
}
