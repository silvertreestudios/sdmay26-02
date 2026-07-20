using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Creates one-step and immediately invalid typed selection workflows.</summary>
    public static class SelectionWorkflow
    {
        /// <summary>Creates a one-creature workflow.</summary>
        public static SelectionWorkflow<CreatureSelection> From(CreatureSelectionRequest request) =>
            Create(
                request,
                adapter => adapter.SelectCreature(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates a multiple-creature workflow.</summary>
        public static SelectionWorkflow<MultipleCreatureSelection> From(
            MultipleCreatureSelectionRequest request
        ) =>
            Create(
                request,
                adapter => adapter.SelectCreatures(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates an item workflow.</summary>
        public static SelectionWorkflow<ItemSelection> From(ItemSelectionRequest request) =>
            Create(
                request,
                adapter => adapter.SelectItem(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates a weapon workflow.</summary>
        public static SelectionWorkflow<WeaponSelection> From(WeaponSelectionRequest request) =>
            Create(
                request,
                adapter => adapter.SelectWeapon(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates a path workflow.</summary>
        public static SelectionWorkflow<PathSelection> From(PathSelectionRequest request) =>
            Create(
                request,
                adapter => adapter.SelectPath(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates a grid-cell workflow.</summary>
        public static SelectionWorkflow<GridCellSelection> From(GridCellSelectionRequest request) =>
            Create(
                request,
                adapter => adapter.SelectGridCell(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates an area-template and orientation workflow.</summary>
        public static SelectionWorkflow<AreaSelection> From(AreaSelectionRequest request) =>
            Create(
                request,
                adapter => adapter.SelectArea(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates a spell-variant workflow.</summary>
        public static SelectionWorkflow<SpellVariantSelection> From(
            SpellVariantSelectionRequest request
        ) =>
            Create(
                request,
                adapter => adapter.SelectSpellVariant(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates a spell-slot pool workflow.</summary>
        public static SelectionWorkflow<SpellSlotSelection> From(
            SpellSlotSelectionRequest request
        ) =>
            Create(
                request,
                adapter => adapter.SelectSpellSlot(request),
                selection => request.Accepts(selection)
            );

        /// <summary>Creates an explicit confirmation-or-decline workflow.</summary>
        public static SelectionWorkflow<ConfirmationSelection> From(
            ConfirmationSelectionRequest request
        ) =>
            Create(
                request,
                adapter => adapter.Confirm(request),
                ConfirmationSelectionRequest.Accepts
            );

        /// <summary>Creates a workflow that is already invalid and invokes no adapter.</summary>
        /// <typeparam name="TSelection">The selection type that cannot be produced.</typeparam>
        /// <param name="reason">A non-empty explanation.</param>
        /// <returns>An immediately invalid workflow.</returns>
        public static SelectionWorkflow<TSelection> Invalid<TSelection>(string reason)
        {
            InvalidSelectionOutcome<TSelection> invalid = SelectionOutcome<TSelection>.Invalid(
                reason
            );
            return new SelectionWorkflow<TSelection>(
                (_, _) => new ValueTask<SelectionOutcome<TSelection>>(invalid)
            );
        }

        private static SelectionWorkflow<TSelection> Create<TRequest, TSelection>(
            TRequest request,
            Func<ISelectionAdapter, ValueTask<SelectionOutcome<TSelection>>> select,
            Func<TSelection, bool> accepts
        )
            where TRequest : SelectionRequest
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return new SelectionWorkflow<TSelection>(
                async (adapter, _) =>
                {
                    SelectionOutcome<TSelection> outcome = await select(adapter);
                    if (outcome == null)
                        throw new InvalidOperationException(
                            $"Selection adapter returned no outcome for request '{request.Id}'."
                        );
                    if (
                        outcome is CompletedSelectionOutcome<TSelection> completed
                        && !accepts(completed.Selection)
                    )
                        return SelectionOutcome<TSelection>.Invalid(
                            $"Selection adapter returned a value outside request '{request.Id}'."
                        );
                    return outcome;
                }
            );
        }
    }
}
