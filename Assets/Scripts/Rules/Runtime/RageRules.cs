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
        private static readonly ActionProfile RageProfile = ActionProfile.OneAction(RageTraits);
        private static readonly ActionProfile QuickTemperedProfile = ActionProfile.Create(
            ActionCost.FreeAction,
            RageTraits
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
        public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor)
        {
            ActionValidationResult validation = Validate(snapshot, actor, false);
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

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId == DefinitionId)
                return RageProfile;
            if (definitionId == QuickTemperedDefinitionId)
                return QuickTemperedProfile;
            throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
        }

        /// <summary>Creates the rules-owned Quick-Tempered combat-start operation.</summary>
        /// <param name="actor">The creature whose initiative was rolled.</param>
        /// <returns>
        /// An operation that preserves the internal Quick-Tempered action type while exposing only
        /// the common dispatch contract to host assemblies.
        /// </returns>
        public IRuleOp<RageStartOutcome> CreateQuickTemperedOp(CreatureId actor) =>
            new QuickTemperedRageActionOp(actor);

        internal RageActorState GetActorState(CreatureId actor) => actorStateProvider.Get(actor);

        internal ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            bool quickTempered
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (actor.IsEmpty)
                throw new ArgumentException("An actor is required.", nameof(actor));
            if (!snapshot.Creatures.Contains(actor))
                return ActionValidationResult.Invalid("The actor is not registered.");

            RageActorState state = actorStateProvider.Get(actor);
            if (!state.OwnsRage)
                return ActionValidationResult.Invalid("The actor does not own Rage.");
            if (RageRules.IsRaging(snapshot, actor))
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
                .RegisterHandler<EndRageOp, RageEndOutcome>(new EndRageHandler())
                .RegisterActionValidator(new RageActionValidator(definition))
                .RegisterActionValidator(new QuickTemperedRageActionValidator(definition));
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
