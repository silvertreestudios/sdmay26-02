using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Describes whether action-begun middleware permits the feature handler to run.
    /// </summary>
    public enum ActionStartDecision
    {
        /// <summary>
        /// Continue to the action's feature handler.
        /// </summary>
        Continue,

        /// <summary>
        /// Stop after costs have committed because a rule disrupted the action.
        /// </summary>
        Interrupted,
    }

    /// <summary>
    /// Carries the typed decision returned by the action-begun lifecycle window.
    /// </summary>
    public readonly struct ActionStartOutcome : IEquatable<ActionStartOutcome>
    {
        private ActionStartOutcome(ActionStartDecision decision) => Decision = decision;

        /// <summary>
        /// Gets the normal continue outcome.
        /// </summary>
        public static ActionStartOutcome Continue { get; } =
            new ActionStartOutcome(ActionStartDecision.Continue);

        /// <summary>
        /// Gets the disrupted outcome. Already committed costs remain spent.
        /// </summary>
        public static ActionStartOutcome Interrupted { get; } =
            new ActionStartOutcome(ActionStartDecision.Interrupted);

        /// <summary>
        /// Gets the lifecycle decision.
        /// </summary>
        public ActionStartDecision Decision { get; }

        /// <inheritdoc/>
        public bool Equals(ActionStartOutcome other) => Decision == other.Decision;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionStartOutcome other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (int)Decision;

        /// <summary>
        /// Compares two outcomes by decision.
        /// </summary>
        public static bool operator ==(ActionStartOutcome left, ActionStartOutcome right) =>
            left.Equals(right);

        /// <summary>
        /// Compares two outcomes by decision.
        /// </summary>
        public static bool operator !=(ActionStartOutcome left, ActionStartOutcome right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Confirms that all costs in a frozen action profile were rechecked and accepted atomically.
    /// </summary>
    public readonly struct ActionCostsOutcome : IEquatable<ActionCostsOutcome>
    {
        /// <inheritdoc/>
        public bool Equals(ActionCostsOutcome other) => true;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionCostsOutcome;

        /// <inheritdoc/>
        public override int GetHashCode() => 0;

        /// <summary>
        /// Compares two successful cost outcomes.
        /// </summary>
        public static bool operator ==(ActionCostsOutcome left, ActionCostsOutcome right) => true;

        /// <summary>
        /// Compares two successful cost outcomes.
        /// </summary>
        public static bool operator !=(ActionCostsOutcome left, ActionCostsOutcome right) => false;
    }

    /// <summary>
    /// Opens the mandatory post-cost, pre-handler action lifecycle window.
    /// </summary>
    /// <remarks>
    /// The operation carries only trusted action-frame identity. Middleware follows
    /// <see cref="ActionOpId"/> through <see cref="ResolutionTrace"/> to inspect the originating
    /// action and its frozen profile.
    /// </remarks>
    public sealed class ActionBegunOp : IRuleOp<ActionStartOutcome>
    {
        internal ActionBegunOp(OpId actionOpId)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            ActionOpId = actionOpId;
        }

        /// <summary>
        /// Gets the originating action frame identity.
        /// </summary>
        public OpId ActionOpId { get; }
    }

    /// <summary>
    /// Rechecks and commits the complete frozen cost set for one action.
    /// </summary>
    /// <remarks>
    /// Only the dispatcher can construct this nested-only operation. The profile is the exact
    /// instance frozen on the parent action frame, so feature code cannot substitute cheaper costs.
    /// The engine registers this resolver with middleware disabled because profile resolution is
    /// the supported cost-adjustment seam and mandatory commitment cannot depend on middleware
    /// calling its continuation. The rule registry rejects middleware configured for any resolver
    /// whose generic registration policy disables it.
    /// </remarks>
    public sealed class CommitActionCostsOp : IRuleOp<ActionCostsOutcome>
    {
        internal CommitActionCostsOp(
            OpId actionOpId,
            CreatureId actor,
            ActionDefinitionId definitionId,
            ActionProfile profile
        )
            : this(actionOpId, actor, definitionId, profile, ActionCostReceiptCheckpoint.None) { }

        internal CommitActionCostsOp(
            OpId actionOpId,
            CreatureId actor,
            ActionDefinitionId definitionId,
            ActionProfile profile,
            IReceiptedActionOp receiptedAction
        )
            : this(
                actionOpId,
                actor,
                definitionId,
                profile,
                ActionCostReceiptCheckpoint.For(receiptedAction)
            ) { }

        private CommitActionCostsOp(
            OpId actionOpId,
            CreatureId actor,
            ActionDefinitionId definitionId,
            ActionProfile profile,
            ActionCostReceiptCheckpoint receiptCheckpoint
        )
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "An action definition is required.",
                    nameof(definitionId)
                );
            ActionOpId = actionOpId;
            Actor = actor;
            DefinitionId = definitionId;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            ReceiptCheckpoint =
                receiptCheckpoint ?? throw new ArgumentNullException(nameof(receiptCheckpoint));
        }

        /// <summary>
        /// Gets the parent action frame whose costs are being committed.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature paying the costs.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the trusted top-level action definition from the parent frame.</summary>
        public ActionDefinitionId DefinitionId { get; }

        /// <summary>
        /// Gets the engine-owned frozen profile from the parent action frame.
        /// </summary>
        public ActionProfile Profile { get; }

        internal ActionCostReceiptCheckpoint ReceiptCheckpoint { get; }
    }

    internal sealed class ActionCostReceiptCheckpoint
    {
        internal static ActionCostReceiptCheckpoint None { get; } =
            new ActionCostReceiptCheckpoint(null);

        private readonly IReceiptedActionOp operation;

        private ActionCostReceiptCheckpoint(IReceiptedActionOp operation) =>
            this.operation = operation;

        internal static ActionCostReceiptCheckpoint For(IReceiptedActionOp operation) =>
            new ActionCostReceiptCheckpoint(
                operation ?? throw new ArgumentNullException(nameof(operation))
            );

        internal bool TryGetOperation(out IReceiptedActionOp value)
        {
            value = operation;
            return operation != null;
        }
    }

    internal sealed class InterruptReceiptedActionOp : IRuleOp<ActionStartOutcome>
    {
        internal InterruptReceiptedActionOp(IReceiptedActionOp operation) =>
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));

        internal IReceiptedActionOp Operation { get; }
    }
}
