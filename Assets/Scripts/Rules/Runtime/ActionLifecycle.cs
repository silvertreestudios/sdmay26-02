using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
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

    /// <summary>Provides type-erased routing data for an action lifecycle occurrence Fact.</summary>
    public interface IActionLifecycleFact
    {
        /// <summary>Gets the stable action definition selected for the invocation.</summary>
        ActionDefinitionId DefinitionId { get; }
    }

    /// <summary>
    /// Reports that one action passed validation, committed all costs, and completed its
    /// action-begun timing window immediately before feature-owned mechanics execute.
    /// </summary>
    /// <typeparam name="TResult">The action's feature-owned result type.</typeparam>
    /// <remarks>
    /// The occurrence carries the original immutable action rather than a presentation or history
    /// copy. Invalid, interrupted, and cancelled actions do not produce it.
    /// </remarks>
    public interface IActionBegunFact : IActionLifecycleFact { }

    /// <inheritdoc cref="IActionBegunFact"/>
    public sealed class ActionBegunFact<TResult> : RuleFact, IActionBegunFact
    {
        internal ActionBegunFact(ActionOpInfo actionInfo, ActionOp<TResult> action)
        {
            ActionInfo = actionInfo ?? throw new ArgumentNullException(nameof(actionInfo));
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        /// <summary>Gets dispatcher-owned identity and provenance for the begun action.</summary>
        public ActionOpInfo ActionInfo { get; }

        /// <summary>Gets the actual immutable action request and selection object.</summary>
        public ActionOp<TResult> Action { get; }

        ActionDefinitionId IActionLifecycleFact.DefinitionId => ActionInfo.DefinitionId;
    }

    /// <summary>Provides type-erased routing data for a structurally resolved action Fact.</summary>
    public interface IActionResolvedFact : IActionLifecycleFact { }

    /// <summary>
    /// Reports that one action structurally resolved after its handler and awaited child mechanics,
    /// carrying the handler's concrete feature-owned outcome.
    /// </summary>
    /// <typeparam name="TResult">The action's existing feature-owned outcome type.</typeparam>
    /// <remarks>
    /// This committed occurrence does not imply that state changed. A miss and another successful
    /// action outcome still resolve structurally. Invalid, interrupted, and cancelled actions do
    /// not produce this Fact. The action and outcome are the original immutable values; consumers
    /// should not create parallel invocation or result DTOs for presentation.
    /// </remarks>
    public sealed class ActionResolvedFact<TResult> : RuleFact, IActionResolvedFact
    {
        internal ActionResolvedFact(
            ActionOpInfo actionInfo,
            ActionOp<TResult> action,
            TResult outcome
        )
        {
            ActionInfo = actionInfo ?? throw new ArgumentNullException(nameof(actionInfo));
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Outcome = outcome;
        }

        /// <summary>Gets dispatcher-owned identity and provenance for the resolved action.</summary>
        public ActionOpInfo ActionInfo { get; }

        /// <summary>Gets the actual immutable action request and selection object.</summary>
        public ActionOp<TResult> Action { get; }

        /// <summary>Gets the existing feature-owned outcome returned by the action handler.</summary>
        public TResult Outcome { get; }

        ActionDefinitionId IActionLifecycleFact.DefinitionId => ActionInfo.DefinitionId;
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
