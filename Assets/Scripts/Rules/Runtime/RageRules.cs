using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Supplies immutable creature facts needed by the Rage workflow.</summary>
    /// <remarks>
    /// The rules implementation depends on this narrow boundary instead of Unity components or
    /// prepared-character objects. Hosts may read those systems while constructing the value, but
    /// every Rage decision is made from <see cref="RageActorState"/>.
    /// </remarks>
    public interface IRageActorStateProvider
    {
        /// <summary>Gets the current immutable Rage facts for a registered creature.</summary>
        /// <param name="actor">The creature whose eligibility is being evaluated.</param>
        /// <returns>The complete Rage input state for that creature.</returns>
        RageActorState Get(CreatureId actor);
    }

    /// <summary>Contains all non-effect facts used to validate and resolve Rage.</summary>
    public sealed class RageActorState
    {
        /// <summary>Initializes immutable Rage inputs for one creature.</summary>
        /// <param name="ownsRage">Whether the creature owns the Rage action.</param>
        /// <param name="ownsQuickTempered">Whether the creature owns Quick-Tempered.</param>
        /// <param name="isFatigued">Whether Fatigued currently prevents Rage.</param>
        /// <param name="isEncumbered">Whether Encumbered prevents Quick-Tempered.</param>
        /// <param name="wearsHeavyArmor">Whether heavy armor prevents Quick-Tempered.</param>
        /// <param name="hasInvulnerableRager">
        /// Whether the creature has the feature that permits Quick-Tempered in heavy armor.
        /// </param>
        /// <param name="level">The creature level used by Rage temporary Hit Points.</param>
        /// <param name="constitutionModifier">
        /// The Constitution modifier used by Rage temporary Hit Points.
        /// </param>
        public RageActorState(
            bool ownsRage,
            bool ownsQuickTempered,
            bool isFatigued,
            bool isEncumbered,
            bool wearsHeavyArmor,
            bool hasInvulnerableRager,
            int level,
            int constitutionModifier
        )
        {
            if (level < 0)
                throw new ArgumentOutOfRangeException(nameof(level));
            OwnsRage = ownsRage;
            OwnsQuickTempered = ownsQuickTempered;
            IsFatigued = isFatigued;
            IsEncumbered = isEncumbered;
            WearsHeavyArmor = wearsHeavyArmor;
            HasInvulnerableRager = hasInvulnerableRager;
            Level = level;
            ConstitutionModifier = constitutionModifier;
        }

        /// <summary>Gets whether the creature owns the Rage action.</summary>
        public bool OwnsRage { get; }

        /// <summary>Gets whether the creature owns Quick-Tempered.</summary>
        public bool OwnsQuickTempered { get; }

        /// <summary>Gets whether the creature is Fatigued.</summary>
        public bool IsFatigued { get; }

        /// <summary>Gets whether the creature is Encumbered.</summary>
        public bool IsEncumbered { get; }

        /// <summary>Gets whether the creature is wearing heavy armor.</summary>
        public bool WearsHeavyArmor { get; }

        /// <summary>Gets whether Quick-Tempered may ignore heavy armor.</summary>
        public bool HasInvulnerableRager { get; }

        /// <summary>Gets the creature level.</summary>
        public int Level { get; }

        /// <summary>Gets the creature Constitution modifier.</summary>
        public int ConstitutionModifier { get; }
    }

    /// <summary>Stores immutable instance data for an active Rage effect.</summary>
    public sealed class RageEffectState : IEffectState, IEquatable<RageEffectState>
    {
        /// <summary>Initializes the effect state.</summary>
        /// <param name="startedByQuickTempered">
        /// Whether Quick-Tempered, rather than the normal action, began this Rage.
        /// </param>
        public RageEffectState(bool startedByQuickTempered) =>
            StartedByQuickTempered = startedByQuickTempered;

        /// <summary>Gets whether Quick-Tempered began this Rage.</summary>
        public bool StartedByQuickTempered { get; }

        /// <inheritdoc/>
        public bool Equals(RageEffectState other) =>
            other != null && StartedByQuickTempered == other.StartedByQuickTempered;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RageEffectState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StartedByQuickTempered.GetHashCode();
    }

    /// <summary>Describes a successfully started Rage.</summary>
    public readonly struct RageStartOutcome
    {
        /// <summary>Initializes a resolved Rage result.</summary>
        /// <param name="effectId">The authoritative active-effect identity.</param>
        /// <param name="temporaryHitPointsGranted">
        /// Whether the Rage offer replaced the current temporary Hit Point pool.
        /// </param>
        /// <param name="temporaryHitPoints">The temporary Hit Point pool after the offer.</param>
        /// <param name="startedByQuickTempered">Whether Quick-Tempered started the Rage.</param>
        public RageStartOutcome(
            ActiveEffectId effectId,
            bool temporaryHitPointsGranted,
            int temporaryHitPoints,
            bool startedByQuickTempered
        )
        {
            if (effectId.IsEmpty)
                throw new ArgumentException("A Rage effect ID is required.", nameof(effectId));
            if (temporaryHitPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(temporaryHitPoints));
            EffectId = effectId;
            TemporaryHitPointsGranted = temporaryHitPointsGranted;
            TemporaryHitPoints = temporaryHitPoints;
            StartedByQuickTempered = startedByQuickTempered;
        }

        /// <summary>Gets the created Rage effect.</summary>
        public ActiveEffectId EffectId { get; }

        /// <summary>Gets whether Rage replaced the current temporary Hit Point pool.</summary>
        public bool TemporaryHitPointsGranted { get; }

        /// <summary>Gets the temporary Hit Point pool after Rage resolved.</summary>
        public int TemporaryHitPoints { get; }

        /// <summary>Gets whether Quick-Tempered started the Rage.</summary>
        public bool StartedByQuickTempered { get; }
    }

    /// <summary>Describes whether an end request found and removed an active Rage.</summary>
    public readonly struct RageEndOutcome
    {
        /// <summary>Initializes a Rage cleanup result.</summary>
        /// <param name="ended">Whether an active Rage was removed.</param>
        public RageEndOutcome(bool ended) => Ended = ended;

        /// <summary>Gets whether Rage was active and is now removed.</summary>
        public bool Ended { get; }
    }

    /// <summary>Defines ordinary Rage and its Quick-Tempered combat-start variant.</summary>
    public sealed class RageActionDefinition : IActionCatalog
    {
        private static readonly Trait[] RageTraits =
        {
            Trait.FromSlug("barbarian"),
            Trait.FromSlug("concentrate"),
            Trait.FromSlug("emotion"),
            Trait.FromSlug("mental"),
        };
        private static readonly Trait[] QuickTemperedTraits = { Trait.FromSlug("barbarian") };
        private static readonly ActionProfile RageProfile = ActionProfile.OneAction(RageTraits);
        private static readonly ActionProfile QuickTemperedProfile = ActionProfile.Create(
            ActionCost.FreeAction,
            QuickTemperedTraits
        );
        private readonly IRageActorStateProvider actorStateProvider;

        /// <summary>Gets Rage's stable action-definition identity.</summary>
        public static ActionDefinitionId DefinitionId { get; } = new ActionDefinitionId("rage");

        /// <summary>Gets the rule definition used by active Rage bindings.</summary>
        public static RuleDefinitionId EffectDefinitionId { get; } =
            new RuleDefinitionId("effect-rage");

        internal static ActionDefinitionId QuickTemperedDefinitionId { get; } =
            new ActionDefinitionId("quick-tempered-rage");

        /// <summary>Creates the Rage definition against an immutable-facts provider.</summary>
        /// <param name="actorStateProvider">
        /// The boundary used to capture current ownership, condition, armor, and statistic facts.
        /// </param>
        public RageActionDefinition(IRageActorStateProvider actorStateProvider) =>
            this.actorStateProvider =
                actorStateProvider ?? throw new ArgumentNullException(nameof(actorStateProvider));

        /// <summary>Gets current ordinary Rage availability for presentation.</summary>
        /// <param name="snapshot">The authoritative rules snapshot.</param>
        /// <param name="actor">The creature considering Rage.</param>
        /// <returns>A typed availability result with a reason when unavailable.</returns>
        public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor) =>
            RageRules.GetAvailability(snapshot, actor, actorStateProvider.Get(actor));

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId == DefinitionId)
                return RageProfile;
            if (definitionId == QuickTemperedDefinitionId)
                return QuickTemperedProfile;
            throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
        }

        internal RageActorState GetActorState(CreatureId actor) => actorStateProvider.Get(actor);

        internal ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            bool quickTempered
        ) => RageRules.Validate(snapshot, actor, actorStateProvider.Get(actor), quickTempered);
    }

    /// <summary>Requests the complete ordinary one-action Rage workflow.</summary>
    public sealed class RageActionOp : ActionOp<RageStartOutcome>
    {
        /// <summary>Creates an ordinary Rage attempt.</summary>
        /// <param name="actor">The creature attempting to Rage.</param>
        public RageActionOp(CreatureId actor)
            : base(actor, RageActionDefinition.DefinitionId) { }
    }

    internal sealed class QuickTemperedRageActionOp : ActionOp<RageStartOutcome>
    {
        public QuickTemperedRageActionOp(CreatureId actor)
            : base(actor, RageActionDefinition.QuickTemperedDefinitionId) { }
    }

    internal sealed class ResolveQuickTemperedTriggerOp : IRuleOp<QuickTemperedTriggerOutcome>
    {
        public ResolveQuickTemperedTriggerOp(CreatureId actor, BindingId binding)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Quick-Tempered actor is required.", nameof(actor));
            if (binding.IsEmpty)
                throw new ArgumentException(
                    "A Quick-Tempered binding is required.",
                    nameof(binding)
                );
            Actor = actor;
            Binding = binding;
        }

        public CreatureId Actor { get; }

        public BindingId Binding { get; }
    }

    internal readonly struct QuickTemperedTriggerOutcome
    {
        public QuickTemperedTriggerOutcome(bool startedRage) => StartedRage = startedRage;

        public bool StartedRage { get; }
    }

    internal sealed class ConsumeQuickTemperedTriggerOp
        : IRuleOp<QuickTemperedTriggerConsumedOutcome>
    {
        public ConsumeQuickTemperedTriggerOp(CreatureId actor, BindingId binding)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Quick-Tempered actor is required.", nameof(actor));
            if (binding.IsEmpty)
                throw new ArgumentException(
                    "A Quick-Tempered binding is required.",
                    nameof(binding)
                );
            Actor = actor;
            Binding = binding;
        }

        public CreatureId Actor { get; }

        public BindingId Binding { get; }
    }

    internal readonly struct QuickTemperedTriggerConsumedOutcome
    {
        public QuickTemperedTriggerConsumedOutcome(BindingId binding) => Binding = binding;

        public BindingId Binding { get; }
    }

    internal sealed class QuickTemperedTriggerConsumedFact : RuleFact
    {
        public QuickTemperedTriggerConsumedFact(CreatureId actor, BindingId binding)
        {
            Actor = actor;
            Binding = binding;
        }

        public CreatureId Actor { get; }

        public BindingId Binding { get; }
    }

    /// <summary>Requests Rage cleanup without exposing active-effect mutation to a host layer.</summary>
    public sealed class EndRageOp : IRuleOp<RageEndOutcome>
    {
        /// <summary>Initializes a Rage cleanup request.</summary>
        /// <param name="actor">The creature whose active Rage should end.</param>
        public EndRageOp(CreatureId actor)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Rage actor is required.", nameof(actor));
            Actor = actor;
        }

        /// <summary>Gets the creature whose Rage should end.</summary>
        public CreatureId Actor { get; }
    }

    /// <summary>Queries and composes the authoritative Rage rules slice.</summary>
    public static class RageRules
    {
        internal static readonly RuleSource Source = RuleSource.FromSlug("rage");
        internal static readonly RuleDefinitionId QuickTemperedRuleDefinitionId =
            new RuleDefinitionId("quick-tempered");
        internal static readonly RuleSource QuickTemperedSource = RuleSource.FromSlug(
            "quick-tempered"
        );
        private static readonly IReadOnlyList<ActiveRuleBinding> NoInitialBindings =
            Array.AsReadOnly(Array.Empty<ActiveRuleBinding>());
        private static readonly IReadOnlyList<string> ActiveRollOptions = Array.AsReadOnly(
            new[] { "self:effect:rage", "self:effect:effect-rage" }
        );

        /// <summary>Gets ordinary Rage availability from authoritative and immutable actor state.</summary>
        /// <param name="snapshot">The authoritative rules snapshot.</param>
        /// <param name="actor">The creature considering Rage.</param>
        /// <param name="state">The creature's current immutable Rage inputs.</param>
        /// <returns>A typed available or unavailable preview state.</returns>
        public static ActionAvailability GetAvailability(
            RulesSnapshot snapshot,
            CreatureId actor,
            RageActorState state
        )
        {
            ActionValidationResult validation = Validate(snapshot, actor, state, false);
            if (validation is ActionValidationResult.InvalidActionValidationResult invalid)
                return ActionAvailability.Unavailable(invalid.Reason);
            if (
                !snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                || economy.ActionsRemaining < ActionCost.One.Amount
            )
            {
                return ActionAvailability.Unavailable("The actor does not have an action.");
            }
            return ActionAvailability.Available;
        }

        /// <summary>Creates Rage-owned persistent bindings present when an actor registers.</summary>
        /// <param name="actor">The registering creature.</param>
        /// <param name="state">The creature's immutable Rage inputs.</param>
        /// <returns>The actor's initial Rage-owned bindings, or an empty collection.</returns>
        public static IReadOnlyList<ActiveRuleBinding> CreateInitialBindings(
            CreatureId actor,
            RageActorState state
        )
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Rage actor is required.", nameof(actor));
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (!state.OwnsQuickTempered)
                return NoInitialBindings;
            return Array.AsReadOnly(
                new[]
                {
                    new ActiveRuleBinding(
                        new BindingId($"quick-tempered-{actor.Value}"),
                        QuickTemperedRuleDefinitionId,
                        actor,
                        default,
                        QuickTemperedSource,
                        0
                    ),
                }
            );
        }

        /// <summary>Gets roll options contributed by the actor's active Rage.</summary>
        /// <param name="snapshot">The authoritative rules snapshot.</param>
        /// <param name="actor">The creature whose options are requested.</param>
        /// <returns>Rage roll options while active, otherwise an empty collection.</returns>
        public static IReadOnlyList<string> GetActiveRollOptions(
            RulesSnapshot snapshot,
            CreatureId actor
        ) => IsRaging(snapshot, actor) ? ActiveRollOptions : Array.Empty<string>();

        internal static ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            RageActorState state,
            bool quickTempered
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (actor.IsEmpty)
                throw new ArgumentException("An actor is required.", nameof(actor));
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (!snapshot.Creatures.Contains(actor))
                return ActionValidationResult.Invalid("The actor is not registered.");
            if (!state.OwnsRage)
                return ActionValidationResult.Invalid("The actor does not own Rage.");
            if (IsRaging(snapshot, actor))
                return ActionValidationResult.Invalid("The actor is already raging.");
            if (state.IsFatigued)
                return ActionValidationResult.Invalid("The actor is fatigued.");

            if (!quickTempered)
                return ActionValidationResult.Valid;
            if (!state.OwnsQuickTempered)
                return ActionValidationResult.Invalid("The actor does not own Quick-Tempered.");
            if (state.IsEncumbered)
                return ActionValidationResult.Invalid("The actor is encumbered.");
            if (state.WearsHeavyArmor && !state.HasInvulnerableRager)
                return ActionValidationResult.Invalid("The actor is wearing heavy armor.");
            return ActionValidationResult.Valid;
        }

        /// <summary>Adds Rage and Quick-Tempered binding listeners to a rule registry.</summary>
        /// <param name="builder">The shared registry builder being composed.</param>
        /// <returns>The supplied builder for continued composition.</returns>
        public static RuleRegistryBuilder DefineRuleBindings(RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder
                .Define(RageActionDefinition.EffectDefinitionId)
                .FactListener(RuleLifecyclePhase.Reaction, new EndRageOnEncounterEndListener());
            builder
                .Define(QuickTemperedRuleDefinitionId)
                .FactListener(RuleLifecyclePhase.Reaction, new QuickTemperedInitiativeListener());
            return builder;
        }

        /// <summary>Checks whether a creature has an active Rage effect in rules state.</summary>
        /// <param name="snapshot">The authoritative rules snapshot.</param>
        /// <param name="actor">The creature to inspect.</param>
        /// <returns>Whether an active Rage effect belongs to the creature.</returns>
        public static bool IsRaging(RulesSnapshot snapshot, CreatureId actor)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (actor.IsEmpty)
                throw new ArgumentException("A Rage actor is required.", nameof(actor));
            return snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                && pair.Value.SourceCreature == actor
                && pair.Value.Status == ActiveEffectStatus.Active
            );
        }

        /// <summary>
        /// Normalizes health restored outside an active encounter when Rage owned the saved
        /// temporary Hit Point pool.
        /// </summary>
        /// <param name="health">The validated saved health state.</param>
        /// <param name="rageWasActive">
        /// Whether the discarded encounter rules store reported an active Rage.
        /// </param>
        /// <returns>
        /// Health with an orphaned Rage pool removed and Rage immunity applied, or the unchanged
        /// state when Rage was inactive and another source owns the pool.
        /// </returns>
        /// <remarks>
        /// Dungeon saves do not resume an active encounter or its rules store. Restoring
        /// Rage-owned temporary Hit Points without the matching active effect would create a
        /// second, ownerless source of truth, so restoration resolves the same health cleanup as
        /// ending Rage.
        /// </remarks>
        public static HealthState NormalizeRestoredHealth(HealthState health, bool rageWasActive)
        {
            bool rageOwnsTemporaryHitPoints = health.TemporarySource == Source;
            if (!rageWasActive && !rageOwnsTemporaryHitPoints)
                return health;

            RuleSource[] immunities = health
                .TemporaryHitPointImmunities.Append(Source)
                .Distinct()
                .ToArray();
            return new HealthState(
                health.Current,
                health.Maximum,
                rageOwnsTemporaryHitPoints ? 0 : health.Temporary,
                rageOwnsTemporaryHitPoints ? default : health.TemporarySource,
                immunities
            );
        }

        /// <summary>Adds ordinary Rage, Quick-Tempered Rage, and cleanup handlers.</summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <param name="definition">The shared Rage definition and actor-facts boundary.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseRageRules(
            this RuleDispatcherBuilder builder,
            RageActionDefinition definition
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            return builder
                .RegisterHandler<RageActionOp, RageStartOutcome>(new RageActionHandler(definition))
                .RegisterHandler<QuickTemperedRageActionOp, RageStartOutcome>(
                    new QuickTemperedRageActionHandler(definition)
                )
                .RegisterHandler<ResolveQuickTemperedTriggerOp, QuickTemperedTriggerOutcome>(
                    new ResolveQuickTemperedTriggerHandler()
                )
                .RegisterHandler<EndRageOp, RageEndOutcome>(new EndRageHandler())
                .RegisterReducer<
                    ConsumeQuickTemperedTriggerOp,
                    QuickTemperedTriggerConsumedOutcome
                >(new ConsumeQuickTemperedTriggerReducer(), QuickTemperedSource)
                .RegisterActionValidator(new RageActionValidator(definition))
                .RegisterActionValidator(new QuickTemperedRageActionValidator(definition));
        }
    }

    internal sealed class QuickTemperedInitiativeListener : IRuleFactListener<InitiativeRolledFact>
    {
        public async ValueTask OnFactCommitted(InitiativeRolledFact fact, FactContext context)
        {
            if (fact.Creature != context.Binding.Owner)
                return;
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(
                    new ResolveQuickTemperedTriggerOp(fact.Creature, context.Binding.Id)
                )
            );
        }
    }

    internal sealed class EndRageOnEncounterEndListener : IRuleFactListener<EncounterEndedFact>
    {
        public async ValueTask OnFactCommitted(EncounterEndedFact fact, FactContext context)
        {
            if (fact.Creature != context.Binding.Owner)
                return;
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(new EndRageOp(fact.Creature))
            );
        }
    }

    internal sealed class ResolveQuickTemperedTriggerHandler
        : IOpHandler<ResolveQuickTemperedTriggerOp, QuickTemperedTriggerOutcome>
    {
        public async ValueTask<QuickTemperedTriggerOutcome> Handle(
            OpFrame<ResolveQuickTemperedTriggerOp> frame,
            OpHandlerContext context
        )
        {
            if (
                !context.Snapshot.RuleBindings.TryGet(
                    frame.Op.Binding,
                    out ActiveRuleBinding binding
                )
                || !binding.IsEnabled
                || binding.Owner != frame.Op.Actor
                || binding.DefinitionId != RageRules.QuickTemperedRuleDefinitionId
            )
            {
                throw new InvalidOperationException(
                    "Quick-Tempered resolution requires its active actor-owned binding."
                );
            }

            OpResult<RageStartOutcome> rage = await context.Dispatch(
                new QuickTemperedRageActionOp(frame.Op.Actor)
            );
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(
                    new ConsumeQuickTemperedTriggerOp(frame.Op.Actor, frame.Op.Binding)
                )
            );
            return new QuickTemperedTriggerOutcome(rage is ResolvedOpResult<RageStartOutcome>);
        }
    }

    internal sealed class ConsumeQuickTemperedTriggerReducer
        : IOpReducer<ConsumeQuickTemperedTriggerOp, QuickTemperedTriggerConsumedOutcome>
    {
        public ReductionResult<QuickTemperedTriggerConsumedOutcome> Reduce(
            ReductionContext<ConsumeQuickTemperedTriggerOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !state.RuleBindings.TryGet(context.Op.Binding, out ActiveRuleBinding binding)
                || !binding.IsEnabled
                || binding.Owner != context.Op.Actor
                || binding.DefinitionId != RageRules.QuickTemperedRuleDefinitionId
            )
            {
                return ReductionResult<QuickTemperedTriggerConsumedOutcome>.Reject(
                    "The Quick-Tempered opportunity is not active."
                );
            }

            state.RuleBindings.Set(binding.Id, binding.WithEnabled(false));
            facts.Stage(new QuickTemperedTriggerConsumedFact(context.Op.Actor, binding.Id));
            return ReductionResult<QuickTemperedTriggerConsumedOutcome>.Accept(
                new QuickTemperedTriggerConsumedOutcome(binding.Id)
            );
        }
    }

    internal sealed class RageActionValidator : IActionValidator<RageActionOp>
    {
        private readonly RageActionDefinition definition;

        public RageActionValidator(RageActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        public ActionValidationResult Validate(
            OpFrame<RageActionOp> frame,
            RulesSnapshot snapshot
        ) => definition.Validate(snapshot, frame.Op.Actor, false);
    }

    internal sealed class QuickTemperedRageActionValidator
        : IActionValidator<QuickTemperedRageActionOp>
    {
        private readonly RageActionDefinition definition;

        public QuickTemperedRageActionValidator(RageActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        public ActionValidationResult Validate(
            OpFrame<QuickTemperedRageActionOp> frame,
            RulesSnapshot snapshot
        ) => definition.Validate(snapshot, frame.Op.Actor, true);
    }

    internal sealed class RageActionHandler : IOpHandler<RageActionOp, RageStartOutcome>
    {
        private readonly RageActionDefinition definition;

        public RageActionHandler(RageActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        public ValueTask<RageStartOutcome> Handle(
            OpFrame<RageActionOp> frame,
            OpHandlerContext context
        ) => RageHandlerSupport.Start(frame.Op.Actor, frame.RootId, false, definition, context);
    }

    internal sealed class QuickTemperedRageActionHandler
        : IOpHandler<QuickTemperedRageActionOp, RageStartOutcome>
    {
        private readonly RageActionDefinition definition;

        public QuickTemperedRageActionHandler(RageActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        public ValueTask<RageStartOutcome> Handle(
            OpFrame<QuickTemperedRageActionOp> frame,
            OpHandlerContext context
        ) => RageHandlerSupport.Start(frame.Op.Actor, frame.RootId, true, definition, context);
    }

    internal sealed class EndRageHandler : IOpHandler<EndRageOp, RageEndOutcome>
    {
        public async ValueTask<RageEndOutcome> Handle(
            OpFrame<EndRageOp> frame,
            OpHandlerContext context
        )
        {
            ActiveEffectInstance[] effects = context
                .Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Where(effect =>
                    effect.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && effect.SourceCreature == frame.Op.Actor
                    && effect.Status == ActiveEffectStatus.Active
                )
                .ToArray();
            if (effects.Length == 0)
                return new RageEndOutcome(false);
            if (effects.Length != 1)
                throw new InvalidOperationException(
                    "A creature cannot have multiple active Rages."
                );

            ActiveEffectInstance effect = effects[0];
            ActiveRuleBinding[] bindings = context
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .Where(binding =>
                    binding.IsEnabled
                    && binding.EffectId.HasValue
                    && binding.EffectId.Value == effect.Id
                )
                .ToArray();
            if (bindings.Length != 1)
                throw new InvalidOperationException(
                    "An active Rage requires exactly one active binding."
                );

            HealthChangeOriginId origin = RageHandlerSupport.CreateHealthOrigin(frame.RootId);
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(
                    new RemoveTemporaryHitPointsOp(frame.Op.Actor, origin, RageRules.Source)
                )
            );
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(
                    new AddTemporaryHitPointImmunityOp(frame.Op.Actor, origin, RageRules.Source)
                )
            );
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(
                    new RemoveActiveEffectOp(
                        effect.Id,
                        bindings[0].Id,
                        effect.EffectStateVersion,
                        RageRules.Source
                    )
                )
            );
            return new RageEndOutcome(true);
        }
    }

    internal static class RageHandlerSupport
    {
        public static async ValueTask<RageStartOutcome> Start(
            CreatureId actor,
            OpId rootId,
            bool startedByQuickTempered,
            RageActionDefinition definition,
            OpHandlerContext context
        )
        {
            RageActorState actorState = definition.GetActorState(actor);
            ActiveEffectId effectId = new ActiveEffectId($"rage-effect-{rootId.Value}");
            BindingId bindingId = new BindingId($"rage-binding-{rootId.Value}");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                effectId,
                RageActionDefinition.EffectDefinitionId,
                actor,
                RageRules.Source,
                EffectDuration.OneMinute,
                new RageEffectState(startedByQuickTempered)
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                bindingId,
                RageActionDefinition.EffectDefinitionId,
                actor,
                effectId,
                RageRules.Source,
                rootId.Value
            );
            await RequireResolved(context.Dispatch(new CreateActiveEffectOp(effect, binding)));

            int offeredTemporaryHitPoints = Math.Max(
                0,
                actorState.Level + actorState.ConstitutionModifier
            );
            TemporaryHitPointsGrantOutcome grant = await RequireResolved(
                context.Dispatch(
                    new GrantTemporaryHitPointsOp(
                        actor,
                        offeredTemporaryHitPoints,
                        CreateHealthOrigin(rootId),
                        RageRules.Source
                    )
                )
            );
            return new RageStartOutcome(
                effectId,
                grant.Granted,
                grant.CurrentAmount,
                startedByQuickTempered
            );
        }

        public static HealthChangeOriginId CreateHealthOrigin(OpId rootId) =>
            new HealthChangeOriginId($"rage-root-{rootId.Value}");

        public static async ValueTask<TResult> RequireResolved<TResult>(
            ValueTask<OpResult<TResult>> pending
        )
        {
            OpResult<TResult> result = await pending;
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException("A nested Rage operation did not resolve.");
        }
    }
}
