using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Returns predeclared typed outcomes in call order so workflow tests can prove sequencing and
    /// short-circuit behavior without Unity input.
    /// </summary>
    internal sealed class ScriptedSelectionAdapter : ISelectionAdapter
    {
        private readonly Queue<object> outcomes;
        private readonly List<SelectionRequestId> requests = new List<SelectionRequestId>();

        public ScriptedSelectionAdapter(params object[] outcomes)
        {
            if (outcomes == null)
                throw new ArgumentNullException(nameof(outcomes));
            this.outcomes = new Queue<object>(outcomes);
        }

        public IReadOnlyList<SelectionRequestId> Requests => requests;

        public int Remaining => outcomes.Count;

        public ValueTask<SelectionOutcome<CreatureSelection>> SelectCreature(
            CreatureSelectionRequest request
        ) => Next<CreatureSelection>(request.Id);

        public ValueTask<SelectionOutcome<MultipleCreatureSelection>> SelectCreatures(
            MultipleCreatureSelectionRequest request
        ) => Next<MultipleCreatureSelection>(request.Id);

        public ValueTask<SelectionOutcome<ItemSelection>> SelectItem(
            ItemSelectionRequest request
        ) => Next<ItemSelection>(request.Id);

        public ValueTask<SelectionOutcome<WeaponSelection>> SelectWeapon(
            WeaponSelectionRequest request
        ) => Next<WeaponSelection>(request.Id);

        public ValueTask<SelectionOutcome<PathSelection>> SelectPath(
            PathSelectionRequest request
        ) => Next<PathSelection>(request.Id);

        public ValueTask<SelectionOutcome<GridCellSelection>> SelectGridCell(
            GridCellSelectionRequest request
        ) => Next<GridCellSelection>(request.Id);

        public ValueTask<SelectionOutcome<AreaSelection>> SelectArea(
            AreaSelectionRequest request
        ) => Next<AreaSelection>(request.Id);

        public ValueTask<SelectionOutcome<SpellVariantSelection>> SelectSpellVariant(
            SpellVariantSelectionRequest request
        ) => Next<SpellVariantSelection>(request.Id);

        public ValueTask<SelectionOutcome<SpellSlotSelection>> SelectSpellSlot(
            SpellSlotSelectionRequest request
        ) => Next<SpellSlotSelection>(request.Id);

        public ValueTask<SelectionOutcome<ConfirmationSelection>> Confirm(
            ConfirmationSelectionRequest request
        ) => Next<ConfirmationSelection>(request.Id);

        private ValueTask<SelectionOutcome<TSelection>> Next<TSelection>(SelectionRequestId id)
        {
            if (outcomes.Count == 0)
                throw new InvalidOperationException("No scripted selection outcome remains.");

            object outcome = outcomes.Dequeue();
            requests.Add(id);
            if (outcome is Task<SelectionOutcome<TSelection>> pending)
                return new ValueTask<SelectionOutcome<TSelection>>(pending);
            if (!(outcome is SelectionOutcome<TSelection> typed))
                throw new InvalidOperationException(
                    $"Scripted outcome does not produce {typeof(TSelection).Name}."
                );
            return new ValueTask<SelectionOutcome<TSelection>>(typed);
        }
    }
}
