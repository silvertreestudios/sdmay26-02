using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Provides stable identity shared by every immutable selection request.</summary>
    public abstract class SelectionRequest
    {
        /// <summary>Gets the stable request identity used by UI, AI, and replay adapters.</summary>
        public SelectionRequestId Id { get; }

        private protected SelectionRequest(SelectionRequestId id)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A selection request ID is required.", nameof(id));
            Id = id;
        }
    }

    /// <summary>Requests one creature from a deterministic candidate set.</summary>
    public sealed class CreatureSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<CreatureId> candidates;

        /// <summary>Gets selectable creatures in presentation order.</summary>
        public IReadOnlyList<CreatureId> Candidates => candidates;

        /// <summary>Creates a one-creature request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="candidates">Distinct non-empty creature IDs.</param>
        public CreatureSelectionRequest(SelectionRequestId id, IEnumerable<CreatureId> candidates)
            : base(id) =>
            this.candidates = SelectionCollections.CopyDistinct(
                candidates,
                nameof(candidates),
                candidate => candidate.IsEmpty,
                "Creature candidates cannot contain an empty ID."
            );

        internal bool Accepts(CreatureSelection selection) =>
            candidates.Contains(selection.Creature);
    }

    /// <summary>Requests an ordered number of distinct creatures from one candidate set.</summary>
    public sealed class MultipleCreatureSelectionRequest : SelectionRequest
    {
        private readonly IReadOnlyList<CreatureId> candidates;

        /// <summary>Gets selectable creatures in presentation order.</summary>
        public IReadOnlyList<CreatureId> Candidates => candidates;

        /// <summary>Gets the minimum required number of creatures.</summary>
        public int Minimum { get; }

        /// <summary>Gets the maximum permitted number of creatures.</summary>
        public int Maximum { get; }

        /// <summary>Creates a multiple-creature request.</summary>
        /// <param name="id">The stable request identity.</param>
        /// <param name="candidates">Distinct non-empty creature IDs.</param>
        /// <param name="minimum">The positive minimum number of choices.</param>
        /// <param name="maximum">The maximum, no greater than the candidate count.</param>
        public MultipleCreatureSelectionRequest(
            SelectionRequestId id,
            IEnumerable<CreatureId> candidates,
            int minimum,
            int maximum
        )
            : base(id)
        {
            this.candidates = SelectionCollections.CopyDistinct(
                candidates,
                nameof(candidates),
                candidate => candidate.IsEmpty,
                "Creature candidates cannot contain an empty ID."
            );
            if (minimum <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            if (maximum < minimum || maximum > this.candidates.Count)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            Minimum = minimum;
            Maximum = maximum;
        }

        internal bool Accepts(MultipleCreatureSelection selection) =>
            selection != null
            && selection.Creatures.Count >= Minimum
            && selection.Creatures.Count <= Maximum
            && selection.Creatures.All(candidates.Contains);
    }
}
