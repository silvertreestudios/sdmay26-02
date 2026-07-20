namespace Game.Rules.Runtime
{
    /// <summary>Records that a movement action opened or replaced its scoped allowance.</summary>
    public sealed class MovementBudgetStartedFact : RuleFact
    {
        internal MovementBudgetStartedFact(MovementBudgetState budget)
        {
            Budget = budget;
        }

        /// <summary>Gets the complete newly committed budget.</summary>
        public MovementBudgetState Budget { get; }
    }

    /// <summary>Records the turn-boundary removal of persistent movement accounting.</summary>
    public sealed class MovementBudgetResetFact : RuleFact
    {
        internal MovementBudgetResetFact(MovementBudgetState previousBudget)
        {
            PreviousBudget = previousBudget;
        }

        /// <summary>Gets the budget and diagonal phase that were cleared.</summary>
        public MovementBudgetState PreviousBudget { get; }
    }

    /// <summary>
    /// Records one path transition together with its movement cost and frozen provenance.
    /// </summary>
    /// <remarks>
    /// Ordinary steps commit individually. An authorized occupied entry and its immediate exit
    /// emit two ordered transition Facts from one atomic reduction, so both share the final
    /// post-exit observer snapshot while retaining their exact intermediate payloads.
    /// </remarks>
    public sealed class TokenMovedFact : RuleFact
    {
        internal TokenMovedFact(
            CreatureId mover,
            GridPosition from,
            GridPosition to,
            GridDistance cost,
            GridDistance remaining,
            DiagonalMovementPhase diagonalPhase,
            MovementBudgetId budgetId,
            OpId actionOpId,
            MovementTriggerId triggerId,
            MovementTriggerKind triggerKind
        )
        {
            Mover = mover;
            From = from;
            To = to;
            Cost = cost;
            Remaining = remaining;
            DiagonalPhase = diagonalPhase;
            BudgetId = budgetId;
            ActionOpId = actionOpId;
            TriggerId = triggerId;
            TriggerKind = triggerKind;
        }

        /// <summary>Gets the creature that moved.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the authoritative position before the step.</summary>
        public GridPosition From { get; }

        /// <summary>Gets the authoritative position after the step.</summary>
        public GridPosition To { get; }

        /// <summary>Gets the distance atomically spent by this step.</summary>
        public GridDistance Cost { get; }

        /// <summary>Gets the allowance remaining after this step.</summary>
        public GridDistance Remaining { get; }

        /// <summary>Gets the diagonal phase committed after this step.</summary>
        public DiagonalMovementPhase DiagonalPhase { get; }

        /// <summary>Gets the action-scoped budget identity.</summary>
        public MovementBudgetId BudgetId { get; }

        /// <summary>Gets the frozen originating action frame.</summary>
        public OpId ActionOpId { get; }

        /// <summary>Gets the stable pre-departure trigger identity.</summary>
        public MovementTriggerId TriggerId { get; }

        /// <summary>Gets the semantic trigger kind.</summary>
        public MovementTriggerKind TriggerKind { get; }
    }

    /// <summary>
    /// Records one authorized occupied-cell traversal after its entry-and-exit transaction commits.
    /// </summary>
    public sealed class OccupiedSpaceTraversedFact : RuleFact
    {
        internal OccupiedSpaceTraversedFact(
            CreatureId mover,
            CreatureId occupant,
            GridPosition occupiedPosition,
            MovementBudgetId budgetId,
            OpId actionOpId,
            MovementTriggerId triggerId,
            MovementPermissionPurpose purpose
        )
        {
            Mover = mover;
            Occupant = occupant;
            OccupiedPosition = occupiedPosition;
            BudgetId = budgetId;
            ActionOpId = actionOpId;
            TriggerId = triggerId;
            Purpose = purpose;
        }

        /// <summary>Gets the creature traversing the occupied cell.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the creature whose cell was traversed.</summary>
        public CreatureId Occupant { get; }

        /// <summary>Gets the occupied cell that the movement step entered.</summary>
        public GridPosition OccupiedPosition { get; }

        /// <summary>Gets the budget that paid for the step.</summary>
        public MovementBudgetId BudgetId { get; }

        /// <summary>Gets the originating action frame.</summary>
        public OpId ActionOpId { get; }

        /// <summary>Gets the pre-departure trigger identity for the committed step.</summary>
        public MovementTriggerId TriggerId { get; }

        /// <summary>Gets the intended use under which permission was issued.</summary>
        public MovementPermissionPurpose Purpose { get; }
    }

    /// <summary>
    /// Records an authoritative relocation that did not spend an action or movement allowance.
    /// </summary>
    public sealed class TokenRelocatedFact : RuleFact
    {
        internal TokenRelocatedFact(
            CreatureId mover,
            GridPosition from,
            GridPosition to,
            OpId originOpId,
            RelocationKind kind
        )
        {
            Mover = mover;
            From = from;
            To = to;
            OriginOpId = originOpId;
            Kind = kind;
        }

        /// <summary>Gets the creature whose position changed.</summary>
        public CreatureId Mover { get; }

        /// <summary>Gets the authoritative position before relocation.</summary>
        public GridPosition From { get; }

        /// <summary>Gets the authoritative position after relocation.</summary>
        public GridPosition To { get; }

        /// <summary>Gets the ancestor operation that caused relocation.</summary>
        public OpId OriginOpId { get; }

        /// <summary>Gets the mechanical relocation reason.</summary>
        public RelocationKind Kind { get; }
    }
}
