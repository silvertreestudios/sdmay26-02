using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Supplies synchronous AI decisions for every typed selection primitive.
    /// </summary>
    /// <remarks>
    /// Implementations may inspect AI-owned planning state, but they receive no UI Toolkit or grid-FSM
    /// services. Returning a structural invalid result lets the workflow stop without creating an Op.
    /// </remarks>
    public interface IAiSelectionPlanner
    {
        /// <summary>Selects one creature for the request.</summary>
        SelectionOutcome<CreatureSelection> SelectCreature(CreatureSelectionRequest request);

        /// <summary>Selects an ordered group of creatures for the request.</summary>
        SelectionOutcome<MultipleCreatureSelection> SelectCreatures(
            MultipleCreatureSelectionRequest request
        );

        /// <summary>Selects one item for the request.</summary>
        SelectionOutcome<ItemSelection> SelectItem(ItemSelectionRequest request);

        /// <summary>Selects one weapon for the request.</summary>
        SelectionOutcome<WeaponSelection> SelectWeapon(WeaponSelectionRequest request);

        /// <summary>Selects one path for the request.</summary>
        SelectionOutcome<PathSelection> SelectPath(PathSelectionRequest request);

        /// <summary>Selects one grid cell for the request.</summary>
        SelectionOutcome<GridCellSelection> SelectGridCell(GridCellSelectionRequest request);

        /// <summary>Selects one area placement for the request.</summary>
        SelectionOutcome<AreaSelection> SelectArea(AreaSelectionRequest request);

        /// <summary>Selects one spell variant for the request.</summary>
        SelectionOutcome<SpellVariantSelection> SelectSpellVariant(
            SpellVariantSelectionRequest request
        );

        /// <summary>Selects one spell-slot pool for the request.</summary>
        SelectionOutcome<SpellSlotSelection> SelectSpellSlot(SpellSlotSelectionRequest request);

        /// <summary>Answers one confirmation request.</summary>
        SelectionOutcome<ConfirmationSelection> Confirm(ConfirmationSelectionRequest request);
    }

    /// <summary>
    /// Exposes synchronous AI planning through the asynchronous workflow adapter contract.
    /// </summary>
    public sealed class AiSelectionAdapter : ISelectionAdapter
    {
        private readonly IAiSelectionPlanner planner;

        /// <summary>
        /// Initializes an adapter for an AI planner.
        /// </summary>
        /// <param name="planner">The required synchronous typed-decision source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="planner"/> is <see langword="null"/>.</exception>
        public AiSelectionAdapter(IAiSelectionPlanner planner) =>
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<CreatureSelection>> SelectCreature(
            CreatureSelectionRequest request
        ) => CompletedTask(planner.SelectCreature(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<MultipleCreatureSelection>> SelectCreatures(
            MultipleCreatureSelectionRequest request
        ) => CompletedTask(planner.SelectCreatures(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<ItemSelection>> SelectItem(
            ItemSelectionRequest request
        ) => CompletedTask(planner.SelectItem(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<WeaponSelection>> SelectWeapon(
            WeaponSelectionRequest request
        ) => CompletedTask(planner.SelectWeapon(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<PathSelection>> SelectPath(
            PathSelectionRequest request
        ) => CompletedTask(planner.SelectPath(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<GridCellSelection>> SelectGridCell(
            GridCellSelectionRequest request
        ) => CompletedTask(planner.SelectGridCell(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<AreaSelection>> SelectArea(
            AreaSelectionRequest request
        ) => CompletedTask(planner.SelectArea(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<SpellVariantSelection>> SelectSpellVariant(
            SpellVariantSelectionRequest request
        ) => CompletedTask(planner.SelectSpellVariant(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<SpellSlotSelection>> SelectSpellSlot(
            SpellSlotSelectionRequest request
        ) => CompletedTask(planner.SelectSpellSlot(request));

        /// <inheritdoc/>
        public ValueTask<SelectionOutcome<ConfirmationSelection>> Confirm(
            ConfirmationSelectionRequest request
        ) => CompletedTask(planner.Confirm(request));

        private static ValueTask<SelectionOutcome<TSelection>> CompletedTask<TSelection>(
            SelectionOutcome<TSelection> outcome
        )
        {
            if (outcome == null)
                throw new InvalidOperationException("An AI planner returned no selection outcome.");

            return new ValueTask<SelectionOutcome<TSelection>>(outcome);
        }
    }
}
