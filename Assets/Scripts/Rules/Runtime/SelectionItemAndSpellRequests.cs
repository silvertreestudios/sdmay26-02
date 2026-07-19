using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Requests one item from a deterministic candidate set.</summary>
    public sealed class ItemSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<ItemId> candidates;

        /// <summary>Gets selectable items in presentation order.</summary>
        public IReadOnlyList<ItemId> Candidates => candidates;

        /// <summary>Creates an item request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="candidates">Distinct non-empty item IDs.</param>
        public ItemSelectionRequest(SelectionRequestId id, IEnumerable<ItemId> candidates)
            : base(id) =>
            this.candidates = SelectionCollections.CopyDistinct(
                candidates,
                nameof(candidates),
                candidate => candidate.IsEmpty,
                "Item candidates cannot contain an empty ID."
            );

        internal bool Accepts(ItemSelection selection) => candidates.Contains(selection.Item);
    }

    /// <summary>Requests one weapon item from a deterministic candidate set.</summary>
    public sealed class WeaponSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<ItemId> candidates;

        /// <summary>Gets selectable weapon items in presentation order.</summary>
        public IReadOnlyList<ItemId> Candidates => candidates;

        /// <summary>Creates a weapon request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="candidates">Distinct non-empty weapon item IDs.</param>
        public WeaponSelectionRequest(SelectionRequestId id, IEnumerable<ItemId> candidates)
            : base(id) =>
            this.candidates = SelectionCollections.CopyDistinct(
                candidates,
                nameof(candidates),
                candidate => candidate.IsEmpty,
                "Weapon candidates cannot contain an empty ID."
            );

        internal bool Accepts(WeaponSelection selection) => candidates.Contains(selection.Weapon);
    }

    /// <summary>Requests one spell variant from a deterministic candidate set.</summary>
    public sealed class SpellVariantSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<SpellVariantId> candidates;

        /// <summary>Gets selectable spell variants in presentation order.</summary>
        public IReadOnlyList<SpellVariantId> Candidates => candidates;

        /// <summary>Creates a spell-variant request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="candidates">Distinct non-empty variant IDs.</param>
        public SpellVariantSelectionRequest(
            SelectionRequestId id,
            IEnumerable<SpellVariantId> candidates
        )
            : base(id) =>
            this.candidates = SelectionCollections.CopyDistinct(
                candidates,
                nameof(candidates),
                candidate => candidate.IsEmpty,
                "Spell variants cannot contain an empty ID."
            );

        internal bool Accepts(SpellVariantSelection selection) =>
            candidates.Contains(selection.Variant);
    }

    /// <summary>Requests one authoritative spell-slot pool.</summary>
    public sealed class SpellSlotSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<SpellSlotPoolId> candidates;

        /// <summary>Gets selectable spell-slot pools in presentation order.</summary>
        public IReadOnlyList<SpellSlotPoolId> Candidates => candidates;

        /// <summary>Creates a spell-slot pool request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="candidates">Distinct non-empty pool IDs.</param>
        public SpellSlotSelectionRequest(
            SelectionRequestId id,
            IEnumerable<SpellSlotPoolId> candidates
        )
            : base(id) =>
            this.candidates = SelectionCollections.CopyDistinct(
                candidates,
                nameof(candidates),
                candidate => candidate.IsEmpty,
                "Spell-slot pools cannot contain an empty ID."
            );

        internal bool Accepts(SpellSlotSelection selection) => candidates.Contains(selection.Pool);
    }

    /// <summary>Requests an explicit confirmation or decline.</summary>
    public sealed class ConfirmationSelectionRequest : SelectionRequest
    {
        /// <summary>Creates a confirmation request.</summary>
        /// <param name="id">The stable request identity.</param>
        public ConfirmationSelectionRequest(SelectionRequestId id)
            : base(id) { }

        internal static bool Accepts(ConfirmationSelection selection) => true;
    }
}
