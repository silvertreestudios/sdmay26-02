using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Classifies the one action-economy cost paid by a PF2e action invocation.
    /// </summary>
    public enum ActionCostKind
    {
        /// <summary>
        /// The invocation does not participate in action-economy spending.
        /// </summary>
        None,

        /// <summary>
        /// The invocation spends one, two, or three actions.
        /// </summary>
        Actions,

        /// <summary>
        /// The invocation spends the actor's reaction for the current turn cycle.
        /// </summary>
        Reaction,

        /// <summary>
        /// The invocation is explicitly a free action and spends no action points.
        /// </summary>
        FreeAction
    }

    /// <summary>
    /// Describes the PF2e action-economy portion of an action profile.
    /// </summary>
    /// <remarks>
    /// This value deliberately distinguishes a free action from an operation with no action cost.
    /// Both spend zero action points, but the distinction remains useful to rules that refer to the
    /// type of action being taken. Additional consumables belong in <see cref="RuleCost"/> values.
    /// </remarks>
    public readonly struct ActionCost : IEquatable<ActionCost>
    {
        private ActionCost(ActionCostKind kind, int actionCount)
        {
            Kind = kind;
            ActionCount = actionCount;
        }

        /// <summary>
        /// Gets an action cost for an operation outside the PF2e action economy.
        /// </summary>
        public static ActionCost None { get; } = new ActionCost(ActionCostKind.None, 0);

        /// <summary>
        /// Gets a one-action cost.
        /// </summary>
        public static ActionCost One { get; } = new ActionCost(ActionCostKind.Actions, 1);

        /// <summary>
        /// Gets a two-action cost.
        /// </summary>
        public static ActionCost Two { get; } = new ActionCost(ActionCostKind.Actions, 2);

        /// <summary>
        /// Gets a three-action cost.
        /// </summary>
        public static ActionCost Three { get; } = new ActionCost(ActionCostKind.Actions, 3);

        /// <summary>
        /// Gets a reaction cost.
        /// </summary>
        public static ActionCost Reaction { get; } =
            new ActionCost(ActionCostKind.Reaction, 0);

        /// <summary>
        /// Gets a free-action cost.
        /// </summary>
        public static ActionCost FreeAction { get; } =
            new ActionCost(ActionCostKind.FreeAction, 0);

        /// <summary>
        /// Gets the semantic kind of action cost.
        /// </summary>
        public ActionCostKind Kind { get; }

        /// <summary>
        /// Gets the number of actions spent when <see cref="Kind"/> is
        /// <see cref="ActionCostKind.Actions"/>; otherwise, zero.
        /// </summary>
        public int ActionCount { get; }

        /// <summary>
        /// Returns the canonical cost for one, two, or three actions.
        /// </summary>
        /// <param name="actionCount">The number of actions to spend.</param>
        /// <returns>The matching canonical action cost.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="actionCount"/> is outside the supported one-to-three range.
        /// </exception>
        public static ActionCost FromActions(int actionCount)
        {
            switch (actionCount)
            {
                case 1:
                    return One;
                case 2:
                    return Two;
                case 3:
                    return Three;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(actionCount),
                        "An action cost must spend between one and three actions.");
            }
        }

        /// <inheritdoc/>
        public bool Equals(ActionCost other) =>
            Kind == other.Kind && ActionCount == other.ActionCount;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionCost other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Kind, ActionCount);

        /// <summary>
        /// Compares two action costs by kind and action count.
        /// </summary>
        public static bool operator ==(ActionCost left, ActionCost right) => left.Equals(right);

        /// <summary>
        /// Compares two action costs by kind and action count.
        /// </summary>
        public static bool operator !=(ActionCost left, ActionCost right) => !left.Equals(right);
    }

    /// <summary>
    /// Represents one immutable, non-action-economy resource cost.
    /// </summary>
    /// <remarks>
    /// The sealed structural cases prevent a cost from carrying unrelated nullable fields. Use the
    /// factory methods to create the exact resource cost required by an action profile.
    /// </remarks>
    public abstract class RuleCost : IEquatable<RuleCost>
    {
        private protected RuleCost()
        {
        }

        /// <summary>
        /// Creates a cost that spends uses from one spell-slot pool.
        /// </summary>
        /// <param name="pool">The authoritative pool to spend.</param>
        /// <param name="amount">The positive number of uses to spend.</param>
        /// <returns>An immutable spell-slot cost.</returns>
        public static SpellSlotRuleCost SpellSlot(SpellSlotPoolId pool, int amount = 1) =>
            new SpellSlotRuleCost(pool, amount);

        /// <summary>
        /// Creates a cost that spends the acting creature's Focus Points.
        /// </summary>
        /// <param name="amount">The positive number of Focus Points to spend.</param>
        /// <returns>An immutable Focus Point cost.</returns>
        public static FocusPointRuleCost FocusPoints(int amount = 1) =>
            new FocusPointRuleCost(amount);

        /// <summary>
        /// Creates a cost that spends ammunition owned by the acting creature.
        /// </summary>
        /// <param name="item">The stable ammunition item or pool identity.</param>
        /// <param name="amount">The positive amount of ammunition to spend.</param>
        /// <returns>An immutable ammunition cost.</returns>
        public static AmmunitionRuleCost Ammunition(ItemId item, int amount = 1) =>
            new AmmunitionRuleCost(item, amount);

        /// <summary>
        /// Creates a cost that spends one use from an active binding's once-per-round frequency.
        /// </summary>
        /// <param name="binding">The active binding authorized to spend the frequency.</param>
        /// <returns>An immutable once-per-round cost.</returns>
        public static OncePerRoundRuleCost OncePerRound(BindingId binding) =>
            new OncePerRoundRuleCost(binding);

        /// <inheritdoc/>
        public abstract bool Equals(RuleCost other);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RuleCost other && Equals(other);

        /// <inheritdoc/>
        public abstract override int GetHashCode();
    }

    /// <summary>
    /// Spends a positive number of uses from one spell-slot pool.
    /// </summary>
    public sealed class SpellSlotRuleCost : RuleCost
    {
        internal SpellSlotRuleCost(SpellSlotPoolId pool, int amount)
        {
            if (pool.IsEmpty)
                throw new ArgumentException("A spell-slot pool ID is required.", nameof(pool));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Pool = pool;
            Amount = amount;
        }

        /// <summary>
        /// Gets the pool whose available uses will be reduced.
        /// </summary>
        public SpellSlotPoolId Pool { get; }

        /// <summary>
        /// Gets the positive number of uses to spend.
        /// </summary>
        public int Amount { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is SpellSlotRuleCost cost && Pool == cost.Pool && Amount == cost.Amount;

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Pool, Amount);
    }

    /// <summary>
    /// Spends Focus Points from the creature taking the action.
    /// </summary>
    public sealed class FocusPointRuleCost : RuleCost
    {
        internal FocusPointRuleCost(int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Amount = amount;
        }

        /// <summary>
        /// Gets the positive number of Focus Points to spend.
        /// </summary>
        public int Amount { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is FocusPointRuleCost cost && Amount == cost.Amount;

        /// <inheritdoc/>
        public override int GetHashCode() => Amount;
    }

    /// <summary>
    /// Spends ammunition from one item or ammunition pool owned by the actor.
    /// </summary>
    public sealed class AmmunitionRuleCost : RuleCost
    {
        internal AmmunitionRuleCost(ItemId item, int amount)
        {
            if (item.IsEmpty)
                throw new ArgumentException("An ammunition item ID is required.", nameof(item));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Item = item;
            Amount = amount;
        }

        /// <summary>
        /// Gets the ammunition item or pool to spend.
        /// </summary>
        public ItemId Item { get; }

        /// <summary>
        /// Gets the positive amount of ammunition to spend.
        /// </summary>
        public int Amount { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is AmmunitionRuleCost cost && Item == cost.Item && Amount == cost.Amount;

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Item, Amount);
    }

    /// <summary>
    /// Spends the current round's one use for an active rule binding.
    /// </summary>
    public sealed class OncePerRoundRuleCost : RuleCost
    {
        internal OncePerRoundRuleCost(BindingId binding)
        {
            if (binding.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(binding));
            Binding = binding;
        }

        /// <summary>
        /// Gets the active binding whose frequency use will be spent.
        /// </summary>
        public BindingId Binding { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is OncePerRoundRuleCost cost && Binding == cost.Binding;

        /// <inheritdoc/>
        public override int GetHashCode() => Binding.GetHashCode();
    }

    /// <summary>
    /// Freezes all shared rules metadata used by one action invocation.
    /// </summary>
    /// <remarks>
    /// The dispatcher resolves exactly one effective profile before validation. Validators,
    /// cost commitment, lifecycle middleware, and the feature handler all observe this same
    /// instance even when nested rules commit later state changes.
    /// </remarks>
    public sealed class ActionProfile : IEquatable<ActionProfile>
    {
        private readonly IReadOnlyList<RuleCost> additionalCosts;
        private readonly IReadOnlyList<Trait> traits;

        /// <summary>
        /// Initializes an immutable effective action profile.
        /// </summary>
        /// <param name="cost">The action-economy cost.</param>
        /// <param name="additionalCosts">All additional resource costs in deterministic order.</param>
        /// <param name="traits">The action's open, data-backed trait set.</param>
        /// <param name="canTriggerReactions">
        /// Whether reaction rules may match this action. The lifecycle window still occurs when
        /// this value is <see langword="false"/>.
        /// </param>
        public ActionProfile(
            ActionCost cost,
            IEnumerable<RuleCost> additionalCosts,
            IEnumerable<Trait> traits,
            bool canTriggerReactions = true)
        {
            if (additionalCosts == null)
                throw new ArgumentNullException(nameof(additionalCosts));
            if (traits == null)
                throw new ArgumentNullException(nameof(traits));

            RuleCost[] copiedCosts = additionalCosts.ToArray();
            if (copiedCosts.Any(ruleCost => ruleCost == null))
                throw new ArgumentException("Additional costs cannot contain null.", nameof(additionalCosts));

            Trait[] copiedTraits = traits
                .Distinct()
                .OrderBy(trait => trait.Slug, StringComparer.Ordinal)
                .ToArray();
            if (copiedTraits.Any(trait => trait.IsEmpty))
                throw new ArgumentException("Action traits cannot contain an empty trait.", nameof(traits));

            Cost = cost;
            this.additionalCosts = new ReadOnlyCollection<RuleCost>(copiedCosts);
            this.traits = Array.AsReadOnly(copiedTraits);
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
            bool canTriggerReactions = true) =>
            new ActionProfile(cost, Array.Empty<RuleCost>(), traits, canTriggerReactions);

        /// <summary>
        /// Creates a one-action profile without additional resource costs.
        /// </summary>
        /// <param name="traits">The action's traits.</param>
        /// <param name="canTriggerReactions">Whether reaction rules may match the action.</param>
        /// <returns>An immutable one-action profile.</returns>
        public static ActionProfile OneAction(
            IEnumerable<Trait> traits,
            bool canTriggerReactions = true) =>
            Create(ActionCost.One, traits, canTriggerReactions);

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
            bool canTriggerReactions = true) =>
            new ActionProfile(ActionCost.One, additionalCosts, traits, canTriggerReactions);

        /// <inheritdoc/>
        public bool Equals(ActionProfile other) =>
            other != null &&
            Cost == other.Cost &&
            CanTriggerReactions == other.CanTriggerReactions &&
            additionalCosts.SequenceEqual(other.additionalCosts) &&
            traits.SequenceEqual(other.traits);

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
            string actionCost = Cost.Kind == ActionCostKind.Actions
                ? $"{Cost.ActionCount} action(s)"
                : Cost.Kind.ToString();
            string traitList = traits.Count == 0
                ? "no traits"
                : string.Join(",", traits.Select(trait => trait.Slug));
            return $"{actionCost}; {additionalCosts.Count} additional cost(s); {traitList}; " +
                $"can-trigger-reactions={CanTriggerReactions.ToString().ToLowerInvariant()}";
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
        ActionProfile GetBaseProfile(IActionCatalog catalog);
    }

    /// <summary>
    /// Defines an operation that represents a PF2e action, reaction, or free action.
    /// </summary>
    /// <typeparam name="TResult">The feature-specific result produced after the lifecycle succeeds.</typeparam>
    /// <remarks>
    /// The dispatcher recognizes this base type and owns profile resolution, pure validation,
    /// atomic cost commitment, and the action-begun timing window. Derived feature handlers run
    /// only after those shared steps complete.
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
                throw new ArgumentException("An action definition ID is required.", nameof(definitionId));
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
        /// Builds the state-independent profile for this concrete invocation.
        /// </summary>
        /// <param name="catalog">The immutable action-definition catalog.</param>
        /// <returns>The base profile that will be resolved against one authoritative snapshot.</returns>
        /// <remarks>
        /// The default implementation looks up <see cref="DefinitionId"/>. Override only when the
        /// operation contains immutable selected data, such as a weapon or spell variant, that must
        /// refine definition data without reading current rules state.
        /// </remarks>
        public virtual ActionProfile GetBaseProfile(IActionCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
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
            Type operationType)
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
            RulesSnapshot snapshot);
    }

    /// <summary>
    /// Represents the outcome of one pure action validator.
    /// </summary>
    /// <remarks>
    /// Structural valid and invalid cases prevent a successful validation from carrying a meaningless
    /// nullable rejection reason.
    /// </remarks>
    public abstract class ActionValidationResult
    {
        private ActionValidationResult()
        {
        }

        /// <summary>
        /// Gets the reusable successful validation result.
        /// </summary>
        public static ValidActionValidationResult Valid { get; } =
            new ValidActionValidationResult();

        /// <summary>
        /// Creates a validation result that stops the action before any cost or lifecycle window.
        /// </summary>
        /// <param name="reason">A non-empty caller-facing explanation.</param>
        /// <returns>An invalid structural result.</returns>
        public static InvalidActionValidationResult Invalid(string reason) =>
            new InvalidActionValidationResult(reason);

        /// <summary>
        /// Represents a successful action validation.
        /// </summary>
        public sealed class ValidActionValidationResult : ActionValidationResult
        {
            internal ValidActionValidationResult()
            {
            }
        }

        /// <summary>
        /// Represents an action that cannot legally begin.
        /// </summary>
        public sealed class InvalidActionValidationResult : ActionValidationResult
        {
            internal InvalidActionValidationResult(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                    throw new ArgumentException("An invalid action requires a reason.", nameof(reason));
                Reason = reason;
            }

            /// <summary>
            /// Gets the reason the action cannot begin.
            /// </summary>
            public string Reason { get; }
        }
    }

    /// <summary>
    /// Performs a side-effect-free legality check for one concrete action operation.
    /// </summary>
    /// <typeparam name="TOp">The concrete action type being validated.</typeparam>
    public interface IActionValidator<TOp>
        where TOp : IRuleOp
    {
        /// <summary>
        /// Validates the frozen action frame against its starting snapshot.
        /// </summary>
        /// <param name="frame">The action frame with its effective profile already frozen.</param>
        /// <param name="snapshot">The same authoritative snapshot used to resolve the profile.</param>
        /// <returns>A valid result or the first reason the action cannot legally begin.</returns>
        ActionValidationResult Validate(OpFrame<TOp> frame, RulesSnapshot snapshot);
    }

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
        Interrupted
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
    /// </remarks>
    public sealed class CommitActionCostsOp : IRuleOp<ActionCostsOutcome>
    {
        internal CommitActionCostsOp(
            OpId actionOpId,
            CreatureId actor,
            ActionProfile profile)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            ActionOpId = actionOpId;
            Actor = actor;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        /// <summary>
        /// Gets the parent action frame whose costs are being committed.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature paying the costs.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the engine-owned frozen profile from the parent action frame.
        /// </summary>
        public ActionProfile Profile { get; }
    }

    /// <summary>
    /// Proves that an action or reaction resource was spent for one action invocation.
    /// </summary>
    public sealed class ActionCostSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame whose cost was paid.</param>
        /// <param name="actor">The creature whose action economy changed.</param>
        /// <param name="cost">The actions or reaction that was spent.</param>
        public ActionCostSpentFact(OpId actionOpId, CreatureId actor, ActionCost cost)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (cost.Kind != ActionCostKind.Actions && cost.Kind != ActionCostKind.Reaction)
            {
                throw new ArgumentException(
                    "Only actions or a reaction produce an action-cost spending Fact.",
                    nameof(cost));
            }
            ActionOpId = actionOpId;
            Actor = actor;
            Cost = cost;
        }

        /// <summary>
        /// Gets the action frame whose cost was paid.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature whose action economy changed.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the exact action or reaction cost that committed.
        /// </summary>
        public ActionCost Cost { get; }
    }

    /// <summary>
    /// Proves that uses were spent from one spell-slot pool.
    /// </summary>
    public sealed class SpellSlotSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that paid the slot cost.</param>
        /// <param name="actor">The creature that owns the pool.</param>
        /// <param name="pool">The pool that changed.</param>
        /// <param name="amount">The positive number of uses spent.</param>
        /// <param name="remaining">The pool's remaining uses after the commit.</param>
        public SpellSlotSpentFact(
            OpId actionOpId,
            CreatureId actor,
            SpellSlotPoolId pool,
            int amount,
            int remaining)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (pool.IsEmpty)
                throw new ArgumentException("A spell-slot pool ID is required.", nameof(pool));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (remaining < 0)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            ActionOpId = actionOpId;
            Actor = actor;
            Pool = pool;
            Amount = amount;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the action frame that paid the slot cost.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature that owns the pool.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the pool that changed.
        /// </summary>
        public SpellSlotPoolId Pool { get; }

        /// <summary>
        /// Gets the number of uses spent.
        /// </summary>
        public int Amount { get; }

        /// <summary>
        /// Gets the remaining uses after the commit.
        /// </summary>
        public int Remaining { get; }
    }

    /// <summary>
    /// Proves that the acting creature spent Focus Points.
    /// </summary>
    public sealed class FocusPointsSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that paid the Focus Point cost.</param>
        /// <param name="actor">The creature whose Focus Points changed.</param>
        /// <param name="amount">The positive number of points spent.</param>
        /// <param name="remaining">The remaining points after the commit.</param>
        public FocusPointsSpentFact(
            OpId actionOpId,
            CreatureId actor,
            int amount,
            int remaining)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (remaining < 0)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            ActionOpId = actionOpId;
            Actor = actor;
            Amount = amount;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the action frame that paid the Focus Point cost.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature whose Focus Points changed.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the number of points spent.
        /// </summary>
        public int Amount { get; }

        /// <summary>
        /// Gets the remaining points after the commit.
        /// </summary>
        public int Remaining { get; }
    }

    /// <summary>
    /// Proves that ammunition was spent for one action invocation.
    /// </summary>
    public sealed class AmmunitionSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that paid the ammunition cost.</param>
        /// <param name="actor">The creature that owns the ammunition.</param>
        /// <param name="item">The ammunition item or pool that changed.</param>
        /// <param name="amount">The positive amount spent.</param>
        /// <param name="remaining">The remaining ammunition after the commit.</param>
        public AmmunitionSpentFact(
            OpId actionOpId,
            CreatureId actor,
            ItemId item,
            int amount,
            int remaining)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (item.IsEmpty)
                throw new ArgumentException("An ammunition item ID is required.", nameof(item));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (remaining < 0)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            ActionOpId = actionOpId;
            Actor = actor;
            Item = item;
            Amount = amount;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the action frame that paid the ammunition cost.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature that owns the ammunition.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the ammunition item or pool that changed.
        /// </summary>
        public ItemId Item { get; }

        /// <summary>
        /// Gets the amount spent.
        /// </summary>
        public int Amount { get; }

        /// <summary>
        /// Gets the remaining ammunition after the commit.
        /// </summary>
        public int Remaining { get; }
    }

    /// <summary>
    /// Proves that an active binding spent its once-per-round use.
    /// </summary>
    public sealed class BindingFrequencySpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that spent the frequency.</param>
        /// <param name="actor">The creature authorized by the binding.</param>
        /// <param name="binding">The active binding whose use changed.</param>
        /// <param name="round">The round marker retained by frequency state.</param>
        /// <param name="uses">The number of uses recorded after the commit.</param>
        public BindingFrequencySpentFact(
            OpId actionOpId,
            CreatureId actor,
            BindingId binding,
            int round,
            int uses)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (binding.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(binding));
            if (round < 0)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (uses <= 0)
                throw new ArgumentOutOfRangeException(nameof(uses));
            ActionOpId = actionOpId;
            Actor = actor;
            Binding = binding;
            Round = round;
            Uses = uses;
        }

        /// <summary>
        /// Gets the action frame that spent the frequency.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature authorized by the binding.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the active binding whose frequency changed.
        /// </summary>
        public BindingId Binding { get; }

        /// <summary>
        /// Gets the round marker retained by frequency state.
        /// </summary>
        public int Round { get; }

        /// <summary>
        /// Gets the recorded uses after the commit.
        /// </summary>
        public int Uses { get; }
    }

    internal abstract class FrameActionState
    {
        public static FrameActionState NonAction { get; } = new NonActionFrameState();

        public abstract bool IsAction { get; }

        public abstract ActionOpInfo RequireInfo();

        public abstract ActionProfile RequireProfile();

        public static FrameActionState Frozen(ActionOpInfo info, ActionProfile profile) =>
            new FrozenActionFrameState(info, profile);

        private sealed class NonActionFrameState : FrameActionState
        {
            public override bool IsAction => false;

            public override ActionOpInfo RequireInfo() =>
                throw new InvalidOperationException("This operation frame does not represent an action.");

            public override ActionProfile RequireProfile() =>
                throw new InvalidOperationException("This operation frame does not represent an action.");
        }

        private sealed class FrozenActionFrameState : FrameActionState
        {
            private readonly ActionOpInfo info;
            private readonly ActionProfile profile;

            public FrozenActionFrameState(ActionOpInfo info, ActionProfile profile)
            {
                this.info = info ?? throw new ArgumentNullException(nameof(info));
                this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            }

            public override bool IsAction => true;
            public override ActionOpInfo RequireInfo() => info;
            public override ActionProfile RequireProfile() => profile;
        }
    }

    internal interface IActionValidatorRegistration
    {
        Type OpType { get; }
        ActionValidationResult Validate(IFrameInvocation invocation);
    }

    internal sealed class ActionValidatorRegistration<TOp> : IActionValidatorRegistration
        where TOp : IRuleOp
    {
        private readonly IActionValidator<TOp> validator;

        public ActionValidatorRegistration(IActionValidator<TOp> validator) =>
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));

        public Type OpType => typeof(TOp);

        public ActionValidationResult Validate(IFrameInvocation invocation)
        {
            if (!(invocation is FrameInvocation<TOp> typed))
                throw new InvalidOperationException("An action validator received an impossible frame type.");

            ActionValidationResult result =
                validator.Validate(typed.Frame, typed.Frame.StartSnapshot);
            return result ?? throw new InvalidOperationException(
                $"Action validator {validator.GetType().Name} returned null.");
        }
    }

    internal abstract class ActionRuntime
    {
        public static ActionRuntime Disabled { get; } = new DisabledActionRuntime();

        public abstract FrameActionState CreateFrameState(
            OpId id,
            OpId rootId,
            OpId? parentId,
            OpId? causeId,
            InvocationPolicy invocationPolicy,
            IRuleOp op,
            RulesSnapshot snapshot);

        public abstract ActionValidationResult Validate(IFrameInvocation invocation);

        public static ActionRuntime Create(
            IActionCatalog catalog,
            IActionProfileResolver resolver,
            IDictionary<Type, List<IActionValidatorRegistration>> validators) =>
            new ConfiguredActionRuntime(catalog, resolver, validators);

        private sealed class DisabledActionRuntime : ActionRuntime
        {
            public override FrameActionState CreateFrameState(
                OpId id,
                OpId rootId,
                OpId? parentId,
                OpId? causeId,
                InvocationPolicy invocationPolicy,
                IRuleOp op,
                RulesSnapshot snapshot)
            {
                if (op is IActionOpMetadata)
                {
                    throw new InvalidOperationException(
                        $"Action lifecycle services are not configured for {op.GetType().Name}.");
                }
                return FrameActionState.NonAction;
            }

            public override ActionValidationResult Validate(IFrameInvocation invocation) =>
                throw new InvalidOperationException(
                    "A disabled action runtime cannot validate an action frame.");
        }

        private sealed class ConfiguredActionRuntime : ActionRuntime
        {
            private readonly IActionCatalog catalog;
            private readonly IActionProfileResolver resolver;
            private readonly IReadOnlyDictionary<Type, IReadOnlyList<IActionValidatorRegistration>>
                validators;

            public ConfiguredActionRuntime(
                IActionCatalog catalog,
                IActionProfileResolver resolver,
                IDictionary<Type, List<IActionValidatorRegistration>> validators)
            {
                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

                Dictionary<Type, IReadOnlyList<IActionValidatorRegistration>> copied =
                    new Dictionary<Type, IReadOnlyList<IActionValidatorRegistration>>();
                foreach (KeyValuePair<Type, List<IActionValidatorRegistration>> pair in validators)
                    copied.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
                this.validators =
                    new ReadOnlyDictionary<Type, IReadOnlyList<IActionValidatorRegistration>>(copied);
            }

            public override FrameActionState CreateFrameState(
                OpId id,
                OpId rootId,
                OpId? parentId,
                OpId? causeId,
                InvocationPolicy invocationPolicy,
                IRuleOp op,
                RulesSnapshot snapshot)
            {
                if (!(op is IActionOpMetadata action))
                    return FrameActionState.NonAction;

                ActionOpInfo info = new ActionOpInfo(
                    id,
                    rootId,
                    parentId,
                    causeId,
                    invocationPolicy,
                    action.Actor,
                    action.DefinitionId,
                    op.GetType());
                ActionProfile baseProfile = action.GetBaseProfile(catalog) ??
                    throw new InvalidOperationException(
                        $"Action {op.GetType().Name} returned a null base profile.");
                ActionProfile effective = resolver.Resolve(info, baseProfile, snapshot) ??
                    throw new InvalidOperationException(
                        $"Action profile resolver {resolver.GetType().Name} returned null.");
                return FrameActionState.Frozen(info, effective);
            }

            public override ActionValidationResult Validate(IFrameInvocation invocation)
            {
                if (!validators.TryGetValue(
                    invocation.FrameView.OpType,
                    out IReadOnlyList<IActionValidatorRegistration> selected))
                {
                    return ActionValidationResult.Valid;
                }

                foreach (IActionValidatorRegistration registration in selected)
                {
                    ActionValidationResult result = registration.Validate(invocation);
                    if (result is ActionValidationResult.InvalidActionValidationResult)
                        return result;
                }
                return ActionValidationResult.Valid;
            }
        }
    }

    internal sealed class IdentityActionProfileResolver : IActionProfileResolver
    {
        public static IdentityActionProfileResolver Instance { get; } =
            new IdentityActionProfileResolver();

        private IdentityActionProfileResolver()
        {
        }

        public ActionProfile Resolve(
            ActionOpInfo action,
            ActionProfile baseProfile,
            RulesSnapshot snapshot) => baseProfile;
    }

    internal sealed class UnconfiguredActionCatalog : IActionCatalog
    {
        public static UnconfiguredActionCatalog Instance { get; } =
            new UnconfiguredActionCatalog();

        private UnconfiguredActionCatalog()
        {
        }

        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
            throw new InvalidOperationException(
                "Action lifecycle services must be configured before resolving an action profile.");
    }

    internal sealed class ActionBegunHandler : IOpHandler<ActionBegunOp, ActionStartOutcome>
    {
        public ValueTask<ActionStartOutcome> Handle(
            OpFrame<ActionBegunOp> frame,
            OpHandlerContext context) =>
            new ValueTask<ActionStartOutcome>(ActionStartOutcome.Continue);
    }

    internal sealed class CommitActionCostsReducer :
        IOpReducer<CommitActionCostsOp, ActionCostsOutcome>
    {
        public ReductionResult<ActionCostsOutcome> Reduce(
            ReductionContext<CommitActionCostsOp> context,
            RulesStateDraft state,
            FactSink facts)
        {
            ActionValidationResult actionCost = SpendActionCost(context.Op, state, facts);
            if (actionCost is ActionValidationResult.InvalidActionValidationResult invalidActionCost)
                return ReductionResult<ActionCostsOutcome>.Reject(invalidActionCost.Reason);

            foreach (RuleCost cost in context.Op.Profile.AdditionalCosts)
            {
                ActionValidationResult additionalCost = SpendAdditionalCost(
                    context.Op,
                    cost,
                    state,
                    facts);
                if (additionalCost is ActionValidationResult.InvalidActionValidationResult invalid)
                    return ReductionResult<ActionCostsOutcome>.Reject(invalid.Reason);
            }

            return ReductionResult<ActionCostsOutcome>.Accept(default);
        }

        private static ActionValidationResult SpendActionCost(
            CommitActionCostsOp op,
            RulesStateDraft state,
            FactSink facts)
        {
            ActionCost cost = op.Profile.Cost;
            if (cost.Kind == ActionCostKind.None || cost.Kind == ActionCostKind.FreeAction)
                return ActionValidationResult.Valid;

            if (!state.ActionEconomy.TryGet(op.Actor, out ActionEconomyState economy))
            {
                return ActionValidationResult.Invalid(
                    $"{op.Actor.Value} has no authoritative action-economy state.");
            }

            if (cost.Kind == ActionCostKind.Actions)
            {
                if (economy.ActionsRemaining < cost.ActionCount)
                    return ActionValidationResult.Invalid("The actor has insufficient actions remaining.");

                state.ActionEconomy.Set(
                    op.Actor,
                    new ActionEconomyState(
                        economy.ActionsRemaining - cost.ActionCount,
                        economy.ReactionAvailable));
            }
            else if (cost.Kind == ActionCostKind.Reaction)
            {
                if (!economy.ReactionAvailable)
                    return ActionValidationResult.Invalid("The actor's reaction is not available.");

                state.ActionEconomy.Set(
                    op.Actor,
                    new ActionEconomyState(economy.ActionsRemaining, false));
            }
            else
            {
                throw new InvalidOperationException($"Unsupported action cost kind {cost.Kind}.");
            }

            facts.Stage(new ActionCostSpentFact(op.ActionOpId, op.Actor, cost));
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendAdditionalCost(
            CommitActionCostsOp op,
            RuleCost cost,
            RulesStateDraft state,
            FactSink facts)
        {
            if (cost is SpellSlotRuleCost spellSlot)
                return SpendSpellSlot(op, spellSlot, state, facts);
            if (cost is FocusPointRuleCost focusPoint)
                return SpendFocusPoints(op, focusPoint, state, facts);
            if (cost is AmmunitionRuleCost ammunition)
                return SpendAmmunition(op, ammunition, state, facts);
            if (cost is OncePerRoundRuleCost frequency)
                return SpendFrequency(op, frequency, state, facts);

            throw new InvalidOperationException(
                $"Unsupported rule cost type {cost.GetType().Name}.");
        }

        private static ActionValidationResult SpendSpellSlot(
            CommitActionCostsOp op,
            SpellSlotRuleCost cost,
            RulesStateDraft state,
            FactSink facts)
        {
            if (!state.SpellSlots.TryGet(cost.Pool, out SpellSlotState slot))
                return ActionValidationResult.Invalid("The required spell-slot pool is unavailable.");
            if (slot.Owner != op.Actor)
                return ActionValidationResult.Invalid("The acting creature does not own the spell-slot pool.");
            if (slot.Remaining < cost.Amount)
                return ActionValidationResult.Invalid("The spell-slot pool has insufficient uses remaining.");

            int remaining = slot.Remaining - cost.Amount;
            state.SpellSlots.Set(
                cost.Pool,
                new SpellSlotState(slot.Id, slot.Owner, remaining, slot.Maximum));
            facts.Stage(new SpellSlotSpentFact(
                op.ActionOpId,
                op.Actor,
                cost.Pool,
                cost.Amount,
                remaining));
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendFocusPoints(
            CommitActionCostsOp op,
            FocusPointRuleCost cost,
            RulesStateDraft state,
            FactSink facts)
        {
            if (!state.FocusPoints.TryGet(op.Actor, out FocusPointState focus))
                return ActionValidationResult.Invalid("The actor has no authoritative Focus Point pool.");
            if (focus.Current < cost.Amount)
                return ActionValidationResult.Invalid("The actor has insufficient Focus Points.");

            int remaining = focus.Current - cost.Amount;
            state.FocusPoints.Set(op.Actor, new FocusPointState(remaining, focus.Maximum));
            facts.Stage(new FocusPointsSpentFact(
                op.ActionOpId,
                op.Actor,
                cost.Amount,
                remaining));
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendAmmunition(
            CommitActionCostsOp op,
            AmmunitionRuleCost cost,
            RulesStateDraft state,
            FactSink facts)
        {
            if (!state.Ammunition.TryGet(cost.Item, out AmmunitionState ammunition))
                return ActionValidationResult.Invalid("The required ammunition is unavailable.");
            if (ammunition.Owner != op.Actor)
                return ActionValidationResult.Invalid("The acting creature does not own the ammunition.");
            if (ammunition.Remaining < cost.Amount)
                return ActionValidationResult.Invalid("There is insufficient ammunition remaining.");

            int remaining = ammunition.Remaining - cost.Amount;
            state.Ammunition.Set(
                cost.Item,
                new AmmunitionState(ammunition.Item, ammunition.Owner, remaining));
            facts.Stage(new AmmunitionSpentFact(
                op.ActionOpId,
                op.Actor,
                cost.Item,
                cost.Amount,
                remaining));
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendFrequency(
            CommitActionCostsOp op,
            OncePerRoundRuleCost cost,
            RulesStateDraft state,
            FactSink facts)
        {
            if (!state.RuleBindings.TryGet(cost.Binding, out ActiveRuleBinding binding) ||
                !binding.IsEnabled)
            {
                return ActionValidationResult.Invalid(
                    "The once-per-round rule binding is not active.");
            }
            if (binding.Owner != op.Actor)
            {
                return ActionValidationResult.Invalid(
                    "The acting creature is not authorized by the frequency binding.");
            }

            FrequencyState current = state.Frequencies.TryGet(
                cost.Binding,
                out FrequencyState existing)
                ? existing
                : new FrequencyState(0, 0);
            if (current.Uses >= 1)
                return ActionValidationResult.Invalid("The once-per-round use has already been spent.");

            FrequencyState spent = new FrequencyState(current.Round, current.Uses + 1);
            state.Frequencies.Set(cost.Binding, spent);
            facts.Stage(new BindingFrequencySpentFact(
                op.ActionOpId,
                op.Actor,
                cost.Binding,
                spent.Round,
                spent.Uses));
            return ActionValidationResult.Valid;
        }
    }

}
