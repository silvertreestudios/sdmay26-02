using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Supplies raw player interaction results without coupling typed workflows to UI Toolkit or the grid FSM.
    /// </summary>
    /// <remarks>
    /// A concrete presentation layer may use any interaction technology. It reports scene references or
    /// Unity grid coordinates here; <see cref="PlayerSelectionAdapter"/> performs the boundary mapping.
    /// </remarks>
    public interface IPlayerSelectionSource
    {
        /// <summary>Requests one creature scene object.</summary>
        ValueTask<SelectionOutcome<GameObject>> SelectCreature(CreatureSelectionRequest request);

        /// <summary>Requests an ordered group of creature scene objects.</summary>
        ValueTask<SelectionOutcome<IReadOnlyList<GameObject>>> SelectCreatures(
            MultipleCreatureSelectionRequest request
        );

        /// <summary>Requests one item scene object.</summary>
        ValueTask<SelectionOutcome<GameObject>> SelectItem(ItemSelectionRequest request);

        /// <summary>Requests one weapon scene object.</summary>
        ValueTask<SelectionOutcome<GameObject>> SelectWeapon(WeaponSelectionRequest request);

        /// <summary>Requests an ordered path in Unity grid coordinates.</summary>
        ValueTask<SelectionOutcome<IReadOnlyList<Vector3Int>>> SelectPath(
            PathSelectionRequest request
        );

        /// <summary>Requests one Unity grid coordinate.</summary>
        ValueTask<SelectionOutcome<Vector3Int>> SelectGridCell(GridCellSelectionRequest request);

        /// <summary>Requests one Unity-side area template placement and orientation.</summary>
        ValueTask<SelectionOutcome<UnityAreaSelection>> SelectArea(AreaSelectionRequest request);

        /// <summary>Requests one stable spell variant from the supplied candidates.</summary>
        ValueTask<SelectionOutcome<SpellVariantId>> SelectSpellVariant(
            SpellVariantSelectionRequest request
        );

        /// <summary>Requests one stable spell-slot pool from the supplied candidates.</summary>
        ValueTask<SelectionOutcome<SpellSlotPoolId>> SelectSpellSlot(
            SpellSlotSelectionRequest request
        );

        /// <summary>Requests a yes-or-no confirmation.</summary>
        ValueTask<SelectionOutcome<bool>> Confirm(ConfirmationSelectionRequest request);
    }

    /// <summary>
    /// Adapts player-facing scene input to the same typed selection protocol used by rules workflows.
    /// </summary>
    public sealed class PlayerSelectionAdapter : ISelectionAdapter
    {
        private readonly IPlayerSelectionSource source;
        private readonly IUnitySelectionMapper mapper;

        /// <summary>
        /// Initializes the adapter with an interaction source and encounter-local reference mapper.
        /// </summary>
        /// <param name="source">The presentation-specific asynchronous input source.</param>
        /// <param name="mapper">The encounter-local mapper for scene and Unity grid values.</param>
        /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
        public PlayerSelectionAdapter(IPlayerSelectionSource source, IUnitySelectionMapper mapper)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<CreatureSelection>> SelectCreature(
            CreatureSelectionRequest request
        ) => Map(await source.SelectCreature(request), mapper.MapCreature);

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<MultipleCreatureSelection>> SelectCreatures(
            MultipleCreatureSelectionRequest request
        ) => Map(await source.SelectCreatures(request), MapCreatures);

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<ItemSelection>> SelectItem(
            ItemSelectionRequest request
        ) => Map(await source.SelectItem(request), mapper.MapItem);

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<WeaponSelection>> SelectWeapon(
            WeaponSelectionRequest request
        ) => Map(await source.SelectWeapon(request), mapper.MapWeapon);

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<PathSelection>> SelectPath(
            PathSelectionRequest request
        ) => Map(await source.SelectPath(request), MapPath);

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<GridCellSelection>> SelectGridCell(
            GridCellSelectionRequest request
        ) => Map(await source.SelectGridCell(request), mapper.MapGridCell);

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<AreaSelection>> SelectArea(
            AreaSelectionRequest request
        ) => Map(await source.SelectArea(request), mapper.MapArea);

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<SpellVariantSelection>> SelectSpellVariant(
            SpellVariantSelectionRequest request
        ) =>
            Map(
                await source.SelectSpellVariant(request),
                value => new SpellVariantSelection(value)
            );

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<SpellSlotSelection>> SelectSpellSlot(
            SpellSlotSelectionRequest request
        ) => Map(await source.SelectSpellSlot(request), value => new SpellSlotSelection(value));

        /// <inheritdoc/>
        public async ValueTask<SelectionOutcome<ConfirmationSelection>> Confirm(
            ConfirmationSelectionRequest request
        ) => Map(await source.Confirm(request), value => new ConfirmationSelection(value));

        private MultipleCreatureSelection MapCreatures(IReadOnlyList<GameObject> sceneObjects) =>
            mapper.MapCreatures(Copy(sceneObjects, "A multiple-creature selection"));

        private PathSelection MapPath(IReadOnlyList<Vector3Int> path) =>
            mapper.MapPath(Copy(path, "A path selection"));

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string description)
        {
            if (values == null)
                throw new InvalidOperationException($"{description} returned no values.");

            T[] copy = new T[values.Count];
            for (int index = 0; index < values.Count; index++)
                copy[index] = values[index];
            return Array.AsReadOnly(copy);
        }

        private static SelectionOutcome<TTarget> Map<TSource, TTarget>(
            SelectionOutcome<TSource> outcome,
            Func<TSource, TTarget> map
        )
        {
            if (outcome == null)
                throw new InvalidOperationException(
                    "A player selection source returned no outcome."
                );

            if (outcome is CompletedSelectionOutcome<TSource> completed)
                return SelectionOutcome<TTarget>.Completed(map(completed.Selection));
            if (outcome is CancelledSelectionOutcome<TSource>)
                return SelectionOutcome<TTarget>.Cancelled;
            if (outcome is InvalidSelectionOutcome<TSource> invalid)
                return SelectionOutcome<TTarget>.Invalid(invalid.Reason);

            throw new InvalidOperationException(
                "A player selection source returned an unknown outcome case."
            );
        }
    }
}
