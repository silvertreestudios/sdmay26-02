using System;

namespace Game.Rules.Runtime
{
    /// <summary>Marks one structurally complete spell-target shape.</summary>
    /// <remarks>
    /// Concrete target shapes expose only the data they require. Spell code pattern-matches the
    /// declared shape instead of reading unrelated optional creature, cell, and area members.
    /// </remarks>
    public interface ISpellTargetSelection { }

    /// <summary>Represents a spell that targets its caster and requires no extra target ID.</summary>
    public sealed class SelfSpellTargetSelection : ISpellTargetSelection
    {
        private static readonly SelfSpellTargetSelection InstanceValue =
            new SelfSpellTargetSelection();

        private SelfSpellTargetSelection() { }

        /// <summary>Gets the shared immutable self-target selection.</summary>
        public static SelfSpellTargetSelection Instance => InstanceValue;
    }

    /// <summary>Represents a spell targeting exactly one creature.</summary>
    public sealed class SingleCreatureSpellTargetSelection : ISpellTargetSelection
    {
        /// <summary>Gets the selected target creature.</summary>
        public CreatureId Target { get; }

        /// <summary>Creates a single-creature spell target.</summary>
        /// <param name="target">The non-empty creature ID.</param>
        public SingleCreatureSpellTargetSelection(CreatureId target)
        {
            if (target.IsEmpty)
                throw new ArgumentException("A spell target is required.", nameof(target));
            Target = target;
        }
    }

    /// <summary>Represents a spell targeting several ordered, distinct creatures.</summary>
    public sealed class MultipleCreatureSpellTargetSelection : ISpellTargetSelection
    {
        private readonly MultipleCreatureSelection selection;

        /// <summary>Gets the immutable creature IDs in selection order.</summary>
        public System.Collections.Generic.IReadOnlyList<CreatureId> Targets => selection.Creatures;

        /// <summary>Creates a multiple-creature spell target.</summary>
        /// <param name="targets">Distinct, non-empty creature IDs.</param>
        public MultipleCreatureSpellTargetSelection(
            System.Collections.Generic.IEnumerable<CreatureId> targets
        ) => selection = new MultipleCreatureSelection(targets);
    }

    /// <summary>Represents a spell targeting exactly one grid cell.</summary>
    public sealed class GridCellSpellTargetSelection : ISpellTargetSelection
    {
        /// <summary>Gets the selected target cell.</summary>
        public GridPosition Cell { get; }

        /// <summary>Creates a grid-cell spell target.</summary>
        /// <param name="cell">The selected rules-grid cell.</param>
        public GridCellSpellTargetSelection(GridPosition cell) => Cell = cell;
    }

    /// <summary>Represents a spell targeting an oriented area template.</summary>
    public sealed class AreaSpellTargetSelection : ISpellTargetSelection
    {
        /// <summary>Gets the complete selected area.</summary>
        public AreaSelection Area { get; }

        /// <summary>Creates an oriented-area spell target.</summary>
        /// <param name="area">The selected template, origin, and facing.</param>
        public AreaSpellTargetSelection(AreaSelection area)
        {
            if (area.Template.IsEmpty || area.Orientation.IsEmpty)
                throw new ArgumentException("A complete spell area is required.", nameof(area));
            Area = area;
        }
    }

    /// <summary>Contains the spell source, variant, and one structurally complete target shape.</summary>
    public sealed class CastSpellSelection
    {
        /// <summary>Gets the selected authoritative spell-slot pool.</summary>
        public SpellSlotPoolId SlotPool { get; }

        /// <summary>Gets the selected spell variant.</summary>
        public SpellVariantId Variant { get; }

        /// <summary>Gets the complete shape-specific targets.</summary>
        public ISpellTargetSelection Targets { get; }

        /// <summary>Creates a complete spell selection.</summary>
        /// <param name="slotPool">The non-empty spell-slot pool.</param>
        /// <param name="variant">The non-empty spell variant.</param>
        /// <param name="targets">A non-null, structurally complete target shape.</param>
        public CastSpellSelection(
            SpellSlotPoolId slotPool,
            SpellVariantId variant,
            ISpellTargetSelection targets
        )
        {
            if (slotPool.IsEmpty)
                throw new ArgumentException("A spell-slot pool is required.", nameof(slotPool));
            if (variant.IsEmpty)
                throw new ArgumentException("A spell variant is required.", nameof(variant));
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            SlotPool = slotPool;
            Variant = variant;
        }
    }
}
