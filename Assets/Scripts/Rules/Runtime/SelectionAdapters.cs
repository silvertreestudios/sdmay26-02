using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Resolves Unity-free selection primitives for a player, AI, replay, or deterministic test.
    /// </summary>
    /// <remarks>
    /// Implementations translate their own scene, UI, or planning objects into the stable IDs and
    /// plain values declared here. They do not dispatch operations or mutate rules state.
    /// </remarks>
    public interface ISelectionAdapter
    {
        /// <summary>Resolves one creature request.</summary>
        /// <param name="request">The immutable candidate set.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<CreatureSelection>> SelectCreature(
            CreatureSelectionRequest request
        );

        /// <summary>Resolves one multiple-creature request.</summary>
        /// <param name="request">The immutable candidates and count bounds.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<MultipleCreatureSelection>> SelectCreatures(
            MultipleCreatureSelectionRequest request
        );

        /// <summary>Resolves one item request.</summary>
        /// <param name="request">The immutable candidate set.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<ItemSelection>> SelectItem(ItemSelectionRequest request);

        /// <summary>Resolves one weapon request.</summary>
        /// <param name="request">The immutable candidate set.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<WeaponSelection>> SelectWeapon(WeaponSelectionRequest request);

        /// <summary>Resolves one ordered grid path request.</summary>
        /// <param name="request">The mover, start, destinations, and length bound.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<PathSelection>> SelectPath(PathSelectionRequest request);

        /// <summary>Resolves one grid-cell request.</summary>
        /// <param name="request">The immutable candidate set.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<GridCellSelection>> SelectGridCell(
            GridCellSelectionRequest request
        );

        /// <summary>Resolves one area-template and orientation request.</summary>
        /// <param name="request">The immutable templates and possible origins.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<AreaSelection>> SelectArea(AreaSelectionRequest request);

        /// <summary>Resolves one spell-variant request.</summary>
        /// <param name="request">The immutable candidate set.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<SpellVariantSelection>> SelectSpellVariant(
            SpellVariantSelectionRequest request
        );

        /// <summary>Resolves one spell-slot pool request.</summary>
        /// <param name="request">The immutable candidate set.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<SpellSlotSelection>> SelectSpellSlot(
            SpellSlotSelectionRequest request
        );

        /// <summary>Resolves an explicit confirmation or decline.</summary>
        /// <param name="request">The stable confirmation prompt.</param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<ConfirmationSelection>> Confirm(
            ConfirmationSelectionRequest request
        );
    }
}
