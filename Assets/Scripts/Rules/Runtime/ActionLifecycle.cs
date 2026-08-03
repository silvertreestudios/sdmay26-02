using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Identifies one stable caller-owned action invocation across exact retries.</summary>
    public readonly struct ActionInvocationId : IEquatable<ActionInvocationId>
    {
        /// <summary>Creates a non-empty stable invocation identity.</summary>
        public ActionInvocationId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An action invocation ID is required.", nameof(value));
            Value = value.Trim();
        }

        /// <summary>Gets the stable identity value.</summary>
        public string Value { get; }

        /// <summary>Gets whether this value is uninitialized.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <inheritdoc/>
        public bool Equals(ActionInvocationId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionInvocationId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        /// <summary>Compares two invocation identities.</summary>
        public static bool operator ==(ActionInvocationId left, ActionInvocationId right) =>
            left.Equals(right);

        /// <summary>Compares two invocation identities.</summary>
        public static bool operator !=(ActionInvocationId left, ActionInvocationId right) =>
            !left.Equals(right);
    }

    internal interface IReceiptedActionOp : IRuleOp
    {
        ActionInvocationId InvocationId { get; }
        CreatureId Actor { get; }
        ActionDefinitionId DefinitionId { get; }
        bool HasSameIntent(IReceiptedActionOp other);
    }

    internal abstract class ActionInvocationReceipt
    {
        protected ActionInvocationReceipt(IReceiptedActionOp operation, ActionProfile frozenProfile)
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            FrozenProfile = frozenProfile ?? throw new ArgumentNullException(nameof(frozenProfile));
        }

        internal IReceiptedActionOp Operation { get; }
        internal ActionProfile FrozenProfile { get; }
    }

    internal sealed class CostsCommittedActionReceipt : ActionInvocationReceipt
    {
        internal CostsCommittedActionReceipt(
            IReceiptedActionOp operation,
            ActionProfile frozenProfile
        )
            : base(operation, frozenProfile) { }
    }

    internal sealed class ResolvedActionReceipt : ActionInvocationReceipt
    {
        internal ResolvedActionReceipt(
            IReceiptedActionOp operation,
            ActionProfile frozenProfile,
            object outcome
        )
            : base(operation, frozenProfile)
        {
            Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        }

        internal object Outcome { get; }
    }

    internal sealed class InterruptedActionReceipt : ActionInvocationReceipt
    {
        internal InterruptedActionReceipt(IReceiptedActionOp operation, ActionProfile frozenProfile)
            : base(operation, frozenProfile) { }
    }

    /// <summary>Reports that a receipted action committed its frozen cost checkpoint.</summary>
    /// <remarks>
    /// Generic observers can identify the acting creature and action definition without receiving
    /// the caller-owned invocation identity, stored operation, selected targets, or outcome payload.
    /// Those private values remain available only to the dispatcher for exact replay validation.
    /// </remarks>
    public sealed class ActionCostsCommittedFact : RuleFact
    {
        /// <summary>Gets the creature whose action costs were checkpointed.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the definition of the action whose costs were checkpointed.</summary>
        public ActionDefinitionId DefinitionId { get; }

        internal ActionCostsCommittedFact(CreatureId actor, ActionDefinitionId definitionId)
        {
            Actor = actor;
            DefinitionId = definitionId;
        }
    }

    /// <summary>Reports that a receipted action committed its interrupted transition.</summary>
    /// <remarks>
    /// Generic observers can identify the acting creature and action definition without receiving
    /// the caller-owned invocation identity, stored operation, selected targets, or outcome payload.
    /// Those private values remain available only to the dispatcher for exact replay validation.
    /// </remarks>
    public sealed class ActionInterruptedFact : RuleFact
    {
        /// <summary>Gets the creature whose action was interrupted.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the definition of the action that was interrupted.</summary>
        public ActionDefinitionId DefinitionId { get; }

        internal ActionInterruptedFact(CreatureId actor, ActionDefinitionId definitionId)
        {
            Actor = actor;
            DefinitionId = definitionId;
        }
    }

    /// <summary>Reports that a receipted action committed its successful final resolution.</summary>
    /// <remarks>
    /// Generic observers can identify the acting creature and action definition without receiving
    /// the caller-owned invocation identity, stored operation, selected targets, or outcome payload.
    /// Those private values remain available only to the dispatcher for exact replay validation.
    /// </remarks>
    public sealed class ActionReceiptCommittedFact : RuleFact
    {
        /// <summary>Gets the creature whose action receipt was committed.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the definition of the action whose receipt was committed.</summary>
        public ActionDefinitionId DefinitionId { get; }

        internal ActionReceiptCommittedFact(CreatureId actor, ActionDefinitionId definitionId)
        {
            Actor = actor;
            DefinitionId = definitionId;
        }
    }

    internal static class ActionReceiptReduction
    {
        internal const string ConflictingIntentReason =
            "The action invocation ID belongs to a different intent.";
        internal const string AlreadyCheckpointedReason =
            "The action invocation already has a lifecycle checkpoint.";
        internal const string NotPendingReason =
            "The action invocation does not have an exact pending cost checkpoint.";

        internal static bool TryCheckpointCosts(
            RulesStateDraft state,
            FactSink facts,
            IReceiptedActionOp operation,
            ActionProfile frozenProfile
        )
        {
            if (state.ActionReceipts.Contains(operation.InvocationId))
                return false;
            state.ActionReceipts.Set(
                operation.InvocationId,
                new CostsCommittedActionReceipt(operation, frozenProfile)
            );
            facts.Stage(new ActionCostsCommittedFact(operation.Actor, operation.DefinitionId));
            return true;
        }

        internal static bool TryResolve(
            RulesStateDraft state,
            FactSink facts,
            IReceiptedActionOp operation,
            object outcome
        )
        {
            if (!TryGetExactPending(state, operation, out CostsCommittedActionReceipt pending))
                return false;
            state.ActionReceipts.Set(
                operation.InvocationId,
                new ResolvedActionReceipt(pending.Operation, pending.FrozenProfile, outcome)
            );
            facts.Stage(new ActionReceiptCommittedFact(operation.Actor, operation.DefinitionId));
            return true;
        }

        internal static bool TryInterrupt(
            RulesStateDraft state,
            FactSink facts,
            IReceiptedActionOp operation
        )
        {
            if (!TryGetExactPending(state, operation, out CostsCommittedActionReceipt pending))
                return false;
            state.ActionReceipts.Set(
                operation.InvocationId,
                new InterruptedActionReceipt(pending.Operation, pending.FrozenProfile)
            );
            facts.Stage(new ActionInterruptedFact(operation.Actor, operation.DefinitionId));
            return true;
        }

        internal static bool TryGetExactPending(
            RulesStateDraft state,
            IReceiptedActionOp operation,
            out CostsCommittedActionReceipt pending
        )
        {
            if (
                state.ActionReceipts.TryGet(
                    operation.InvocationId,
                    out ActionInvocationReceipt receipt
                ) && TryMatchExactPending(receipt, operation, out pending)
            )
                return true;

            pending = null;
            return false;
        }

        /// <summary>
        /// Checks whether an authoritative snapshot contains a dispatcher-replayable lifecycle
        /// receipt for the exact action intent.
        /// </summary>
        internal static bool HasExactReplayableReceipt(
            RulesSnapshot snapshot,
            IReceiptedActionOp operation
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            return snapshot.ActionReceipts.TryGet(
                    operation.InvocationId,
                    out ActionInvocationReceipt receipt
                ) && IsExactReplayableReceipt(receipt, operation);
        }

        private static bool TryMatchExactPending(
            ActionInvocationReceipt receipt,
            IReceiptedActionOp operation,
            out CostsCommittedActionReceipt pending
        )
        {
            if (
                receipt is CostsCommittedActionReceipt costsCommitted
                && IsExactReplayableReceipt(receipt, operation)
            )
            {
                pending = costsCommitted;
                return true;
            }

            pending = null;
            return false;
        }

        private static bool IsExactReplayableReceipt(
            ActionInvocationReceipt receipt,
            IReceiptedActionOp operation
        ) =>
            (
                receipt is CostsCommittedActionReceipt
                || receipt is ResolvedActionReceipt
                || receipt is InterruptedActionReceipt
            ) && operation.HasSameIntent(receipt.Operation);
    }

    /// <summary>
    /// Freezes all shared rules metadata used by one action invocation.
    /// </summary>
    /// <remarks>
    /// The dispatcher builds and resolves exactly one effective profile from the action's captured
    /// start snapshot before validation. Validators, cost commitment, lifecycle middleware, and
    /// the feature handler all observe this same instance even when nested rules commit later
    /// state changes.
    /// </remarks>
    public sealed class ActionProfile : IEquatable<ActionProfile>
    {
        private readonly IReadOnlyList<RuleCost> additionalCosts;
        private readonly IReadOnlyList<Trait> traits;
        private readonly HashSet<Trait> traitLookup;

        /// <summary>
        /// Initializes an immutable effective action profile.
        /// </summary>
        /// <param name="cost">The action-economy cost.</param>
        /// <param name="additionalCosts">All additional resource costs in deterministic order.</param>
        /// <param name="traits">The action's open, data-backed trait set.</param>
        /// <param name="canTriggerReactions">
        /// Whether reaction rules may match this action. The lifecycle window still occurs when
        /// this value is <see langword="false"/>, so lifecycle listeners are responsible for
        /// checking this flag before matching or prompting a reaction.
        /// </param>
        /// <remarks>
        /// The constructor accepts definition-data sequences and takes one defensive snapshot of
        /// each. Callers can therefore build profiles from catalogs and DTOs without sharing mutable
        /// collection ownership with the frozen invocation. Use <see cref="HasTrait"/> for membership
        /// checks instead of scanning <see cref="Traits"/>.
        /// </remarks>
        public ActionProfile(
            ActionCost cost,
            IEnumerable<RuleCost> additionalCosts,
            IEnumerable<Trait> traits,
            bool canTriggerReactions = true
        )
        {
            if (additionalCosts == null)
                throw new ArgumentNullException(nameof(additionalCosts));
            if (traits == null)
                throw new ArgumentNullException(nameof(traits));

            RuleCost[] copiedCosts = additionalCosts.ToArray();
            if (copiedCosts.Any(ruleCost => ruleCost == null))
                throw new ArgumentException(
                    "Additional costs cannot contain null.",
                    nameof(additionalCosts)
                );

            Trait[] copiedTraits = traits
                .Distinct()
                .OrderBy(trait => trait.Slug, StringComparer.Ordinal)
                .ToArray();
            if (copiedTraits.Any(trait => trait.IsEmpty))
                throw new ArgumentException(
                    "Action traits cannot contain an empty trait.",
                    nameof(traits)
                );

            Cost = cost;
            this.additionalCosts = new ReadOnlyCollection<RuleCost>(copiedCosts);
            this.traits = Array.AsReadOnly(copiedTraits);
            traitLookup = new HashSet<Trait>(copiedTraits);
            CanTriggerReactions = canTriggerReactions;
        }

        /// <summary>
        /// Gets the action-economy cost.
        /// </summary>
        public ActionCost Cost { get; }

        /// <summary>
        /// Gets the additional consumable costs in deterministic commitment order.
        /// </summary>
        public IReadOnlyList<RuleCost> AdditionalCosts => additionalCosts;

        /// <summary>
        /// Gets the canonical, ordinally ordered action traits.
        /// </summary>
        public IReadOnlyList<Trait> Traits => traits;

        /// <summary>
        /// Determines whether this profile contains an exact action trait.
        /// </summary>
        /// <param name="trait">The canonical trait value to find.</param>
        /// <returns><see langword="true"/> when the frozen trait set contains the value.</returns>
        public bool HasTrait(Trait trait) => traitLookup.Contains(trait);

        /// <summary>
        /// Gets whether reaction rules may match this invocation.
        /// </summary>
        public bool CanTriggerReactions { get; }

        /// <summary>
        /// Creates a profile without additional resource costs.
        /// </summary>
        /// <param name="cost">The action-economy cost.</param>
        /// <param name="traits">The action's traits.</param>
        /// <param name="canTriggerReactions">Whether reaction rules may match the action.</param>
        /// <returns>An immutable action profile.</returns>
        public static ActionProfile Create(
            ActionCost cost,
            IEnumerable<Trait> traits,
            bool canTriggerReactions = true
        ) => new ActionProfile(cost, Array.Empty<RuleCost>(), traits, canTriggerReactions);

        /// <summary>
        /// Creates a one-action profile without additional resource costs.
        /// </summary>
        /// <param name="traits">The action's traits.</param>
        /// <param name="canTriggerReactions">Whether reaction rules may match the action.</param>
        /// <returns>An immutable one-action profile.</returns>
        public static ActionProfile OneAction(
            IEnumerable<Trait> traits,
            bool canTriggerReactions = true
        ) => Create(ActionCost.One, traits, canTriggerReactions);

        /// <summary>
        /// Creates a one-action profile with additional resource costs.
        /// </summary>
        /// <param name="traits">The action's traits.</param>
        /// <param name="additionalCosts">The additional costs in commitment order.</param>
        /// <param name="canTriggerReactions">Whether reaction rules may match the action.</param>
        /// <returns>An immutable one-action profile.</returns>
        public static ActionProfile OneAction(
            IEnumerable<Trait> traits,
            IEnumerable<RuleCost> additionalCosts,
            bool canTriggerReactions = true
        ) => new ActionProfile(ActionCost.One, additionalCosts, traits, canTriggerReactions);

        /// <inheritdoc/>
        public bool Equals(ActionProfile other) =>
            other != null
            && Cost == other.Cost
            && CanTriggerReactions == other.CanTriggerReactions
            && additionalCosts.SequenceEqual(other.additionalCosts)
            && traits.SequenceEqual(other.traits);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionProfile other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int hash = HashCode.Combine(Cost, CanTriggerReactions);
            foreach (RuleCost cost in additionalCosts)
                hash = HashCode.Combine(hash, cost);
            foreach (Trait trait in traits)
                hash = HashCode.Combine(hash, trait);
            return hash;
        }

        internal string ToDiagnosticString()
        {
            string actionCost =
                Cost.Kind == ActionCostKind.Actions
                    ? $"{Cost.Amount} action(s)"
                    : Cost.Kind.ToString();
            string traitList =
                traits.Count == 0
                    ? "no traits"
                    : string.Join(",", traits.Select(trait => trait.Slug));
            return $"{actionCost}; {additionalCosts.Count} additional cost(s); {traitList}; "
                + $"can-trigger-reactions={CanTriggerReactions.ToString().ToLowerInvariant()}";
        }
    }

    /// <summary>
    /// Supplies immutable base profiles by stable action definition ID.
    /// </summary>
    /// <remarks>
    /// Catalog implementations read definition data only. Live combat state belongs in
    /// <see cref="IActionProfileResolver"/>, which runs after the base profile is created.
    /// </remarks>
    public interface IActionCatalog
    {
        /// <summary>
        /// Gets the immutable base profile declared by an action definition.
        /// </summary>
        /// <param name="definitionId">The action definition to resolve.</param>
        /// <returns>A complete base profile that does not depend on live combat state.</returns>
        ActionProfile GetBaseProfile(ActionDefinitionId definitionId);
    }

    internal interface IActionOpMetadata
    {
        CreatureId Actor { get; }
        ActionDefinitionId DefinitionId { get; }
        ActionProfile GetBaseProfile(IActionCatalog catalog, RulesSnapshot snapshot);
    }

    /// <summary>
    /// Defines an operation that represents a PF2e action, reaction, or free action.
    /// </summary>
    /// <typeparam name="TResult">The feature-specific result produced after the lifecycle succeeds.</typeparam>
    /// <remarks>
    /// The dispatcher recognizes this base type and owns profile resolution, pure validation,
    /// atomic cost commitment, and the action-begun timing window. Derived feature handlers run
    /// only after those shared steps complete. The base profile receives the same captured start
    /// snapshot as the resolver and validators. Receipted actions additionally checkpoint their
    /// exact intent and frozen profile with costs, so an in-process retry spends costs at most once
    /// and resumes after that checkpoint without rebuilding the profile. Work before a final
    /// receipt is not taped and may reroll.
    /// </remarks>
    public abstract class ActionOp<TResult> : IRuleOp<TResult>, IActionOpMetadata
    {
        /// <summary>
        /// Initializes the stable actor and action-definition identity.
        /// </summary>
        /// <param name="actor">The creature attempting the action.</param>
        /// <param name="definitionId">The immutable action definition being invoked.</param>
        protected ActionOp(CreatureId actor, ActionDefinitionId definitionId)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "An action definition ID is required.",
                    nameof(definitionId)
                );
            Actor = actor;
            DefinitionId = definitionId;
        }

        /// <summary>
        /// Gets the creature attempting this action.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the immutable action definition selected for this invocation.
        /// </summary>
        public ActionDefinitionId DefinitionId { get; }

        /// <summary>
        /// Builds the base profile for this concrete invocation from its captured start state.
        /// </summary>
        /// <param name="catalog">The composed action-definition catalog.</param>
        /// <param name="snapshot">
        /// The authoritative snapshot captured when the dispatcher began this operation.
        /// </param>
        /// <returns>The base profile that will be resolved against one authoritative snapshot.</returns>
        /// <remarks>
        /// The default implementation looks up <see cref="DefinitionId"/>. Override only when the
        /// operation contains immutable selected data, such as a weapon or spell variant, that must
        /// refine definition data. Snapshot-aware overrides must use only <paramref name="snapshot"/>
        /// for rules-state decisions; receipted retries restore their previously frozen profile
        /// without invoking this method again.
        /// </remarks>
        public virtual ActionProfile GetBaseProfile(IActionCatalog catalog, RulesSnapshot snapshot)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            return catalog.GetBaseProfile(DefinitionId);
        }
    }

    /// <summary>
    /// Describes the trusted identity of one action invocation before its profile is frozen.
    /// </summary>
    /// <remarks>
    /// This value contains no caller-selected privilege and no mutable state. The dispatcher creates
    /// it from the same IDs and ancestry used by the operation frame.
    /// </remarks>
    public sealed class ActionOpInfo
    {
        internal ActionOpInfo(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            InvocationPolicy invocationPolicy,
            CreatureId actor,
            ActionDefinitionId definitionId,
            Type operationType
        )
        {
            Id = id;
            RootId = rootId;
            ParentId = parentId;
            CauseId = causeId;
            InvocationPolicy = invocationPolicy;
            Actor = actor;
            DefinitionId = definitionId;
            OperationType = operationType ?? throw new ArgumentNullException(nameof(operationType));
        }

        /// <summary>
        /// Gets the unique operation-frame identity.
        /// </summary>
        public OpId Id { get; }

        /// <summary>
        /// Gets the root resolution identity.
        /// </summary>
        public OpId RootId { get; }

        /// <summary>
        /// Gets the immediate parent frame, or no value for a root action.
        /// </summary>
        public OpId? ParentId { get; }

        /// <summary>
        /// Gets the causal frame, or no value when the root has no prior mechanical cause.
        /// </summary>
        public OpId? CauseId { get; }

        /// <summary>
        /// Gets the invocation policy assigned by the action's handler registration.
        /// </summary>
        public InvocationPolicy InvocationPolicy { get; }

        /// <summary>
        /// Gets the creature attempting the action.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the action definition selected for the invocation.
        /// </summary>
        public ActionDefinitionId DefinitionId { get; }

        /// <summary>
        /// Gets the concrete operation type without calling operation-defined formatting code.
        /// </summary>
        public Type OperationType { get; }
    }

    /// <summary>
    /// Resolves live state-dependent changes into one effective action profile.
    /// </summary>
    public interface IActionProfileResolver
    {
        /// <summary>
        /// Resolves the profile that the complete action lifecycle will share.
        /// </summary>
        /// <param name="action">Trusted invocation identity and provenance.</param>
        /// <param name="baseProfile">The immutable, definition-backed base profile.</param>
        /// <param name="snapshot">The authoritative snapshot captured when the action frame begins.</param>
        /// <returns>A complete non-null effective profile.</returns>
        ActionProfile Resolve(
            ActionOpInfo action,
            ActionProfile baseProfile,
            RulesSnapshot snapshot
        );
    }
}
