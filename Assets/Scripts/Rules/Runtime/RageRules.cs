using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Contains all non-effect facts used to validate and resolve Rage.</summary>
    public sealed class RageActorState
    {
        /// <summary>Initializes immutable Rage inputs for one creature.</summary>
        /// <param name="ownsRage">Whether the creature owns the Rage action.</param>
        /// <param name="ownsQuickTempered">Whether the creature owns Quick-Tempered.</param>
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
            WearsHeavyArmor = wearsHeavyArmor;
            HasInvulnerableRager = hasInvulnerableRager;
            Level = level;
            ConstitutionModifier = constitutionModifier;
        }

        /// <summary>Gets whether the creature owns the Rage action.</summary>
        public bool OwnsRage { get; }

        /// <summary>Gets whether the creature owns Quick-Tempered.</summary>
        public bool OwnsQuickTempered { get; }

        /// <summary>Gets whether the creature is wearing heavy armor.</summary>
        public bool WearsHeavyArmor { get; }

        /// <summary>Gets whether Quick-Tempered may ignore heavy armor.</summary>
        public bool HasInvulnerableRager { get; }

        /// <summary>Gets the creature level.</summary>
        public int Level { get; }

        /// <summary>Gets the creature Constitution modifier.</summary>
        public int ConstitutionModifier { get; }
    }

    internal enum RageStartPhase
    {
        /// <summary>The effect exists, but the authoritative THP offer or settlement is incomplete.</summary>
        Pending,

        /// <summary>The exact THP outcome and complete Rage start are durably recorded.</summary>
        Settled,
    }

    /// <summary>Exposes stable presentation state for one authoritative Rage effect.</summary>
    /// <remarks>
    /// Publicly constructed values are completed presentation markers. Rules-owned start
    /// workflows retain a private exact receipt that the engine compares independently from this
    /// type's public marker equality.
    /// </remarks>
    public sealed class RageEffectState
        : IEffectState,
            IEquatable<RageEffectState>,
            IExactEffectState
    {
        /// <summary>Initializes a completed Rage presentation marker.</summary>
        /// <param name="startedByQuickTempered">Whether Quick-Tempered began this Rage.</param>
        public RageEffectState(bool startedByQuickTempered)
        {
            StartedByQuickTempered = startedByQuickTempered;
            Phase = RageStartPhase.Settled;
        }

        private RageEffectState(
            CreatureId actor,
            ActiveEffectId effectId,
            BindingId bindingId,
            BindingId triggerBinding,
            OpId rootId,
            HealthChangeOriginId origin,
            int offeredTemporaryHitPoints,
            RageStartPhase phase,
            bool hasGrantOutcome,
            TemporaryHitPointsGrantOutcome grantOutcome,
            TemporaryHitPointsPoolState priorTemporaryHitPoints,
            TemporaryHitPointsPoolState committedTemporaryHitPoints
        )
        {
            HasWorkflowReceipt = true;
            Actor = actor;
            EffectId = effectId;
            BindingId = bindingId;
            TriggerBinding = triggerBinding;
            RootId = rootId;
            Origin = origin;
            OfferedTemporaryHitPoints = offeredTemporaryHitPoints;
            Phase = phase;
            HasGrantOutcome = hasGrantOutcome;
            GrantOutcome = grantOutcome;
            PriorTemporaryHitPoints = priorTemporaryHitPoints;
            CommittedTemporaryHitPoints = committedTemporaryHitPoints;
            StartedByQuickTempered = !triggerBinding.IsEmpty;
        }

        internal bool HasWorkflowReceipt { get; }

        internal CreatureId Actor { get; }

        internal ActiveEffectId EffectId { get; }

        internal BindingId BindingId { get; }

        internal BindingId TriggerBinding { get; }

        internal OpId RootId { get; }

        internal HealthChangeOriginId Origin { get; }

        internal int OfferedTemporaryHitPoints { get; }

        /// <summary>Gets whether Quick-Tempered began this Rage.</summary>
        public bool StartedByQuickTempered { get; }

        internal RageStartPhase Phase { get; }

        internal bool HasGrantOutcome { get; }

        internal TemporaryHitPointsGrantOutcome GrantOutcome { get; }

        internal TemporaryHitPointsPoolState PriorTemporaryHitPoints { get; }

        internal TemporaryHitPointsPoolState CommittedTemporaryHitPoints { get; }

        internal static RageEffectState CreatePending(
            CreatureId actor,
            BindingId triggerBinding,
            OpId rootId,
            int offeredTemporaryHitPoints
        )
        {
            ActiveEffectId effectId = CreateEffectId(rootId, actor);
            BindingId bindingId = CreateBindingId(rootId, actor);
            return new RageEffectState(
                actor,
                effectId,
                bindingId,
                triggerBinding,
                rootId,
                RageHandlerSupport.CreateHealthOrigin(rootId, actor),
                offeredTemporaryHitPoints,
                RageStartPhase.Pending,
                false,
                default,
                default,
                default
            );
        }

        internal static ActiveEffectId CreateEffectId(OpId rootId, CreatureId actor) =>
            new ActiveEffectId($"rage-effect-{rootId.Value}-{actor.Value}");

        internal static BindingId CreateBindingId(OpId rootId, CreatureId actor) =>
            new BindingId($"rage-binding-{rootId.Value}-{actor.Value}");

        internal RageEffectState WithGrantTransition(TemporaryHitPointsGrantTransition transition)
        {
            RequireWorkflowReceipt();
            return new RageEffectState(
                Actor,
                EffectId,
                BindingId,
                TriggerBinding,
                RootId,
                Origin,
                OfferedTemporaryHitPoints,
                RageStartPhase.Pending,
                true,
                transition.Outcome,
                transition.BeforePool,
                transition.AfterPool
            );
        }

        internal RageEffectState Settle()
        {
            RequireWorkflowReceipt();
            return new RageEffectState(
                Actor,
                EffectId,
                BindingId,
                TriggerBinding,
                RootId,
                Origin,
                OfferedTemporaryHitPoints,
                RageStartPhase.Settled,
                true,
                GrantOutcome,
                PriorTemporaryHitPoints,
                CommittedTemporaryHitPoints
            );
        }

        /// <inheritdoc/>
        public bool Equals(RageEffectState other) =>
            other != null && StartedByQuickTempered == other.StartedByQuickTempered;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RageEffectState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StartedByQuickTempered.GetHashCode();

        bool IExactEffectState.ExactEquals(IEffectState other) =>
            ExactEquals(other as RageEffectState);

        int IExactEffectState.GetExactHashCode() => GetExactReceiptHashCode();

        internal bool ExactEquals(RageEffectState other)
        {
            if (other == null || HasWorkflowReceipt != other.HasWorkflowReceipt)
                return false;
            if (!HasWorkflowReceipt)
                return StartedByQuickTempered == other.StartedByQuickTempered;
            return Actor == other.Actor
                && EffectId == other.EffectId
                && BindingId == other.BindingId
                && TriggerBinding == other.TriggerBinding
                && RootId == other.RootId
                && Origin == other.Origin
                && OfferedTemporaryHitPoints == other.OfferedTemporaryHitPoints
                && Phase == other.Phase
                && HasGrantOutcome == other.HasGrantOutcome
                && (
                    !HasGrantOutcome
                    || (
                        GrantOutcomesMatch(GrantOutcome, other.GrantOutcome)
                        && PriorTemporaryHitPoints.Equals(other.PriorTemporaryHitPoints)
                        && CommittedTemporaryHitPoints.Equals(other.CommittedTemporaryHitPoints)
                    )
                );
        }

        private int GetExactReceiptHashCode()
        {
            if (!HasWorkflowReceipt)
                return GetHashCode();
            unchecked
            {
                int hash = Actor.GetHashCode();
                hash = (hash * 397) ^ EffectId.GetHashCode();
                hash = (hash * 397) ^ BindingId.GetHashCode();
                hash = (hash * 397) ^ TriggerBinding.GetHashCode();
                hash = (hash * 397) ^ RootId.GetHashCode();
                hash = (hash * 397) ^ Origin.GetHashCode();
                hash = (hash * 397) ^ OfferedTemporaryHitPoints;
                hash = (hash * 397) ^ Phase.GetHashCode();
                hash = (hash * 397) ^ HasGrantOutcome.GetHashCode();
                if (HasGrantOutcome)
                {
                    hash = (hash * 397) ^ GrantOutcome.Granted.GetHashCode();
                    hash = (hash * 397) ^ GrantOutcome.Immune.GetHashCode();
                    hash = (hash * 397) ^ GrantOutcome.PreviousAmount;
                    hash = (hash * 397) ^ GrantOutcome.CurrentAmount;
                    hash = (hash * 397) ^ PriorTemporaryHitPoints.GetHashCode();
                    hash = (hash * 397) ^ CommittedTemporaryHitPoints.GetHashCode();
                }
                return hash;
            }
        }

        private void RequireWorkflowReceipt()
        {
            if (!HasWorkflowReceipt)
                throw new InvalidOperationException(
                    "A Rage presentation marker does not contain a workflow receipt."
                );
        }

        internal static bool GrantOutcomesMatch(
            TemporaryHitPointsGrantOutcome left,
            TemporaryHitPointsGrantOutcome right
        ) =>
            left.Granted == right.Granted
            && left.Immune == right.Immune
            && left.PreviousAmount == right.PreviousAmount
            && left.CurrentAmount == right.CurrentAmount;
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

        /// <summary>Gets Rage's stable action-definition identity.</summary>
        public static ActionDefinitionId DefinitionId { get; } = new ActionDefinitionId("rage");

        /// <summary>Gets the rule definition used by active Rage bindings.</summary>
        public static RuleDefinitionId EffectDefinitionId { get; } =
            new RuleDefinitionId("effect-rage");

        internal static ActionDefinitionId QuickTemperedDefinitionId { get; } =
            new ActionDefinitionId("quick-tempered-rage");

        /// <summary>Gets current ordinary Rage availability for presentation.</summary>
        /// <param name="snapshot">The authoritative rules snapshot.</param>
        /// <param name="actor">The creature considering Rage.</param>
        /// <returns>A typed availability result with a reason when unavailable.</returns>
        public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor) =>
            RageRules.GetAvailability(snapshot, actor, GetActorState(snapshot, actor));

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId == DefinitionId)
                return RageProfile;
            if (definitionId == QuickTemperedDefinitionId)
                return QuickTemperedProfile;
            throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
        }

        internal RageActorState GetActorState(RulesSnapshot snapshot, CreatureId actor) =>
            RageRules.GetActorState(snapshot, actor);

        internal ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            bool quickTempered
        ) => RageRules.Validate(snapshot, actor, GetActorState(snapshot, actor), quickTempered);
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
        public QuickTemperedRageActionOp(CreatureId actor, BindingId triggerBinding)
            : base(actor, RageActionDefinition.QuickTemperedDefinitionId)
        {
            if (triggerBinding.IsEmpty)
                throw new ArgumentException(
                    "A Quick-Tempered trigger binding is required.",
                    nameof(triggerBinding)
                );
            TriggerBinding = triggerBinding;
        }

        internal BindingId TriggerBinding { get; }
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

    internal sealed class RageStartGrantIntent : ITemporaryHitPointsGrantIntent
    {
        internal RageStartGrantIntent(RageEffectState receipt)
        {
            Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
            if (!receipt.HasWorkflowReceipt)
                throw new ArgumentException(
                    "A Rage start grant requires a workflow receipt.",
                    nameof(receipt)
                );
        }

        internal RageEffectState Receipt { get; }

        public IRuleOp<TemporaryHitPointsGrantOutcome> CreateCommitOperation(
            GrantTemporaryHitPointsOp grant
        ) => new CommitRageTemporaryHitPointsOp(grant, this);
    }

    internal sealed class CommitRageTemporaryHitPointsOp : IRuleOp<TemporaryHitPointsGrantOutcome>
    {
        internal CommitRageTemporaryHitPointsOp(
            GrantTemporaryHitPointsOp grant,
            RageStartGrantIntent intent
        )
        {
            Grant = grant ?? throw new ArgumentNullException(nameof(grant));
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        }

        internal GrantTemporaryHitPointsOp Grant { get; }
        internal RageStartGrantIntent Intent { get; }
    }

    internal sealed class SettleRageStartOp : IRuleOp<RageStartOutcome>
    {
        internal SettleRageStartOp(
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion expectedVersion
        )
        {
            if (effectId.IsEmpty || bindingId.IsEmpty)
                throw new ArgumentException("Rage settlement requires exact effect identity.");
            EffectId = effectId;
            BindingId = bindingId;
            ExpectedVersion = expectedVersion;
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal EffectStateVersion ExpectedVersion { get; }
    }

    internal sealed class AbortPendingRageStartOp : IRuleOp<ActiveEffectRemovalOutcome>
    {
        internal AbortPendingRageStartOp(
            RageEffectState receipt,
            EffectStateVersion expectedVersion
        )
        {
            Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
            if (!receipt.HasWorkflowReceipt || receipt.Phase != RageStartPhase.Pending)
                throw new ArgumentException(
                    "Pending Rage cleanup requires a pending receipt.",
                    nameof(receipt)
                );
            ExpectedVersion = expectedVersion;
        }

        internal RageEffectState Receipt { get; }
        internal EffectStateVersion ExpectedVersion { get; }
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

    /// <summary>Commits cleanup for one exact completed Rage lifecycle instance.</summary>
    /// <remarks>
    /// The operation carries every optimistic identity read by <see cref="EndRageHandler"/>. Its
    /// reducer validates those identities before it invokes shared reducers against one draft.
    /// </remarks>
    internal sealed class CommitRageEndOp : IRuleOp<RageEndOutcome>, IRuleSourcedOp
    {
        internal CommitRageEndOp(
            CreatureId actor,
            ActiveEffectId effectId,
            BindingId bindingId,
            EffectStateVersion expectedVersion,
            RageEffectState expectedState,
            HealthChangeOriginId cleanupOrigin
        )
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Rage actor is required.", nameof(actor));
            Actor = actor;
            EffectId = ActiveEffectOperationValidation.RequireEffect(effectId);
            BindingId = ActiveEffectOperationValidation.RequireBinding(bindingId);
            ExpectedVersion = expectedVersion;
            ExpectedState = expectedState ?? throw new ArgumentNullException(nameof(expectedState));
            CleanupOrigin = HealthOperationValidation.RequireOrigin(cleanupOrigin);
        }

        internal CreatureId Actor { get; }

        internal ActiveEffectId EffectId { get; }

        internal BindingId BindingId { get; }

        internal EffectStateVersion ExpectedVersion { get; }

        internal RageEffectState ExpectedState { get; }

        internal HealthChangeOriginId CleanupOrigin { get; }

        /// <inheritdoc/>
        public RuleSource Source => RageRules.Source;
    }

    /// <summary>Queries and composes the authoritative Rage rules slice.</summary>
    public static class RageRules
    {
        internal static readonly RuleSource Source = RuleSource.FromSlug("rage");
        internal static readonly RuleDefinitionId QuickTemperedRuleDefinitionId =
            new RuleDefinitionId("quick-tempered");
        internal static readonly RuleDefinitionId LifecycleRuleDefinitionId = new RuleDefinitionId(
            "rage-lifecycle"
        );
        internal static readonly RuleSource QuickTemperedSource = RuleSource.FromSlug(
            "quick-tempered"
        );
        internal static readonly RuleSource LifecycleSource = RuleSource.FromSlug("rage-lifecycle");
        private static readonly IReadOnlyList<ActiveRuleBinding> NoInitialBindings =
            Array.AsReadOnly(Array.Empty<ActiveRuleBinding>());

        /// <summary>Creates immutable Rage state from one combatant's prepared enrollment inputs.</summary>
        /// <param name="inputs">The authoritative prepared snapshot captured for the combatant.</param>
        /// <returns>The Rage ownership and derived values to install for the encounter.</returns>
        public static RageActorState CreateEnrollmentState(PreparedCreatureInputs inputs)
        {
            if (inputs == null)
                throw new ArgumentNullException(nameof(inputs));
            return CreateActorState(
                inputs,
                slug =>
                    inputs.StaticOptions.Contains(
                        $"item:owned:{slug}",
                        StringComparer.OrdinalIgnoreCase
                    ) || inputs.BoundOptions.Any(option => option.Option == $"item:owned:{slug}")
            );
        }

        internal static RageActorState GetActorState(RulesSnapshot snapshot, CreatureId actor)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.PreparedInputs.TryGet(actor, out PreparedCreatureInputs inputs))
                throw new InvalidOperationException(
                    $"Rage actor {actor.Value} has no prepared inputs."
                );
            bool Owns(string slug) =>
                inputs.StaticOptions.Contains(
                    $"item:owned:{slug}",
                    StringComparer.OrdinalIgnoreCase
                )
                || inputs.BoundOptions.Any(option =>
                    option.Option == $"item:owned:{slug}"
                    && snapshot.RuleBindings.Any(pair =>
                        pair.Value.Owner == actor
                        && pair.Value.DefinitionId == option.DefinitionId
                        && pair.Value.IsEnabled
                    )
                );
            return CreateActorState(inputs, Owns);
        }

        private static RageActorState CreateActorState(
            PreparedCreatureInputs inputs,
            Func<string, bool> owns
        ) =>
            new RageActorState(
                owns("rage"),
                owns("quick-tempered"),
                inputs.ArmorCategory == "heavy",
                owns("invulnerable-rager"),
                inputs.Level,
                inputs.Abilities.Constitution
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
            if (!state.OwnsRage)
                return NoInitialBindings;
            List<ActiveRuleBinding> bindings = new List<ActiveRuleBinding>
            {
                new ActiveRuleBinding(
                    new BindingId($"rage-lifecycle-{actor.Value}"),
                    LifecycleRuleDefinitionId,
                    actor,
                    default,
                    LifecycleSource,
                    0
                ),
            };
            if (state.OwnsQuickTempered)
            {
                bindings.Add(
                    new ActiveRuleBinding(
                        new BindingId($"quick-tempered-{actor.Value}"),
                        QuickTemperedRuleDefinitionId,
                        actor,
                        default,
                        QuickTemperedSource,
                        1
                    )
                );
            }
            return Array.AsReadOnly(bindings.ToArray());
        }

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
            if (!snapshot.Health.TryGet(actor, out HealthState health))
                return ActionValidationResult.Invalid("The actor has no authoritative health.");
            if (health.IsCommittedDefeated)
                return ActionValidationResult.Invalid("A defeated actor cannot Rage.");
            if (!state.OwnsRage)
                return ActionValidationResult.Invalid("The actor does not own Rage.");
            if (HasActiveStart(snapshot, actor))
                return ActionValidationResult.Invalid("The actor is already raging.");
            if (ConditionSelectors.HasMarker(snapshot, actor, ConditionRuleDefinitions.Fatigued))
                return ActionValidationResult.Invalid("The actor is fatigued.");

            if (!quickTempered)
                return ActionValidationResult.Valid;
            if (!state.OwnsQuickTempered)
                return ActionValidationResult.Invalid("The actor does not own Quick-Tempered.");
            if (ConditionSelectors.HasMarker(snapshot, actor, ConditionRuleDefinitions.Encumbered))
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
            builder.Define(RageActionDefinition.EffectDefinitionId);
            builder
                .Define(LifecycleRuleDefinitionId)
                .Middleware<TurnStartingOp, TurnStartContribution>(
                    RuleLifecyclePhase.Prevention,
                    new RetryExpiredRageCleanupMiddleware()
                )
                .Middleware<EndEncounterOp, EncounterEndOutcome>(
                    RuleLifecyclePhase.Prevention,
                    new EndRageBeforeEncounterEndMiddleware()
                )
                .Middleware<SuspendEncounterOp, EncounterSuspensionOutcome>(
                    RuleLifecyclePhase.Prevention,
                    new EndRageBeforeEncounterSuspendMiddleware()
                );
            builder
                .Define(QuickTemperedRuleDefinitionId)
                .FactListener(
                    RuleLifecyclePhase.Reaction,
                    new QuickTemperedInitiativeAssignedListener()
                );
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
                && pair.Value.Source == Source
                && pair.Value.Status == ActiveEffectStatus.Active
                && pair.Value.State is RageEffectState receipt
                && (!receipt.HasWorkflowReceipt || receipt.Phase == RageStartPhase.Settled)
            );
        }

        private static bool HasActiveStart(RulesSnapshot snapshot, CreatureId actor) =>
            snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                && pair.Value.SourceCreature == actor
                && pair.Value.Source == Source
            );

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
                .RegisterReducer<CommitRageEndOp, RageEndOutcome>(
                    new CommitRageEndReducer(),
                    Source
                )
                .RegisterReducer<AbortPendingRageStartOp, ActiveEffectRemovalOutcome>(
                    new AbortPendingRageStartReducer(),
                    Source,
                    InvocationPolicy.ExternalAllowed
                )
                .RegisterReducer<
                    ConsumeQuickTemperedTriggerOp,
                    QuickTemperedTriggerConsumedOutcome
                >(new ConsumeQuickTemperedTriggerReducer(), QuickTemperedSource)
                .RegisterReducer<CommitRageTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>(
                    new CommitRageTemporaryHitPointsReducer(),
                    Source
                )
                .RegisterReducer<SettleRageStartOp, RageStartOutcome>(
                    new SettleRageStartReducer(),
                    Source
                )
                .RegisterActionValidator(new RageActionValidator(definition))
                .RegisterActionValidator(new QuickTemperedRageActionValidator(definition));
        }
    }

    internal sealed class QuickTemperedInitiativeAssignedListener
        : IRuleFactListener<InitiativeAssignedFact>
    {
        public async ValueTask OnFactCommitted(InitiativeAssignedFact fact, FactContext context)
        {
            if (
                fact.Entry.Creature != context.Binding.Owner
                || !context.Snapshot.Encounters.TryGet(fact.Encounter, out EncounterState encounter)
                || encounter.Phase != EncounterPhase.Active
                || !encounter.Roster.Any(entry => entry.Creature == context.Binding.Owner)
            )
                return;
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(
                    new ResolveQuickTemperedTriggerOp(context.Binding.Owner, context.Binding.Id)
                )
            );
        }
    }

    /// <summary>
    /// Settles a participant's Rage before the encounter-end reducer can publish an irreversible
    /// outcome.
    /// </summary>
    /// <remarks>
    /// The persistent Rage lifecycle binding owns this boundary so a failed cleanup leaves the
    /// encounter active and retryable. Fact delivery is intentionally not used: a committed
    /// encounter outcome could otherwise be stranded when a later listener fails.
    /// </remarks>
    internal sealed class EndRageBeforeEncounterEndMiddleware
        : IOpMiddleware<EndEncounterOp, EncounterEndOutcome>
    {
        public async ValueTask<OpResult<EncounterEndOutcome>> Invoke(
            OpFrame<EndEncounterOp> frame,
            OpMiddlewareContext context,
            OpNext<EncounterEndOutcome> next
        )
        {
            await RageEncounterLifecycleCleanup.EndOwnedRage(
                frame.Op.Encounter,
                context.Binding.Owner,
                context.Snapshot,
                context.Dispatch
            );
            return await next();
        }
    }

    /// <summary>Ends Rage before a suspension can permanently retire its encounter clock.</summary>
    internal sealed class EndRageBeforeEncounterSuspendMiddleware
        : IOpMiddleware<SuspendEncounterOp, EncounterSuspensionOutcome>
    {
        public async ValueTask<OpResult<EncounterSuspensionOutcome>> Invoke(
            OpFrame<SuspendEncounterOp> frame,
            OpMiddlewareContext context,
            OpNext<EncounterSuspensionOutcome> next
        )
        {
            await RageEncounterLifecycleCleanup.EndOwnedRage(
                frame.Op.Encounter,
                context.Binding.Owner,
                context.Snapshot,
                context.Dispatch
            );
            return await next();
        }
    }

    internal static class RageEncounterLifecycleCleanup
    {
        internal static async ValueTask EndOwnedRage(
            EncounterId encounterId,
            CreatureId owner,
            RulesSnapshot snapshot,
            Func<EndRageOp, ValueTask<OpResult<RageEndOutcome>>> dispatch
        )
        {
            if (
                !snapshot.Encounters.TryGet(encounterId, out EncounterState encounter)
                || encounter.Phase != EncounterPhase.Active
                || !encounter.Roster.Any(entry => entry.Creature == owner)
                || !snapshot.ActiveEffects.Any(pair =>
                    pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && pair.Value.SourceCreature == owner
                    && pair.Value.Source == RageRules.Source
                )
            )
                return;
            await RageHandlerSupport.RequireResolved(dispatch(new EndRageOp(owner)));
        }
    }

    /// <summary>
    /// Settles an interrupted timed Rage cleanup before any turn-start adapter can observe the
    /// next initiative boundary.
    /// </summary>
    /// <remarks>
    /// The disabled expired Rage binding is the durable authorization for this retry. The
    /// persistent lifecycle binding owns the feature-level retry rather than asking encounter
    /// runtime to recognize Rage-specific tombstones.
    /// </remarks>
    internal sealed class RetryExpiredRageCleanupMiddleware
        : IOpMiddleware<TurnStartingOp, TurnStartContribution>
    {
        public async ValueTask<OpResult<TurnStartContribution>> Invoke(
            OpFrame<TurnStartingOp> frame,
            OpMiddlewareContext context,
            OpNext<TurnStartContribution> next
        )
        {
            if (
                context.Binding.Owner == frame.Op.Actor
                && HasExpiredRage(context.Snapshot, frame.Op.Actor)
            )
            {
                await RageHandlerSupport.RequireResolved(
                    context.Dispatch(new EndRageOp(frame.Op.Actor))
                );
            }
            return await next();
        }

        private static bool HasExpiredRage(RulesSnapshot snapshot, CreatureId actor) =>
            snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                && pair.Value.SourceCreature == actor
                && pair.Value.Source == RageRules.Source
                && pair.Value.Status == ActiveEffectStatus.Expired
            );
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

            ObserverFailureState recoveredFailures = ObserverFailureState.CreateEmpty(
                "Multiple Quick-Tempered workflow failures were preserved."
            );
            bool startedRage;
            try
            {
                OpResult<RageStartOutcome> rage = await context.Dispatch(
                    new QuickTemperedRageActionOp(frame.Op.Actor, frame.Op.Binding)
                );
                startedRage = rage is ResolvedOpResult<RageStartOutcome>;
            }
            catch (Exception failure)
            {
                if (
                    !RageStartReceipt.TryGetExact(
                        context.Snapshot,
                        frame.Op.Actor,
                        frame.RootId,
                        frame.Op.Binding,
                        RageStartPhase.Settled,
                        out _,
                        out _,
                        out _
                    )
                )
                    throw;
                recoveredFailures = recoveredFailures.Add(failure);
                startedRage = true;
            }

            if (
                !startedRage
                && (
                    !context.Snapshot.Health.TryGet(frame.Op.Actor, out HealthState health)
                    || health.IsCommittedDefeated
                )
            )
                return new QuickTemperedTriggerOutcome(false);

            try
            {
                await RageHandlerSupport.RequireResolved(
                    context.Dispatch(
                        new ConsumeQuickTemperedTriggerOp(frame.Op.Actor, frame.Op.Binding)
                    )
                );
            }
            catch (Exception failure)
            {
                recoveredFailures = recoveredFailures.Add(failure);
                if (!IsConsumed(context.Snapshot, frame.Op.Actor, frame.Op.Binding))
                {
                    recoveredFailures.ThrowIfAny();
                    throw;
                }
            }
            recoveredFailures.ThrowIfAny();
            return new QuickTemperedTriggerOutcome(startedRage);
        }

        private static bool IsConsumed(
            RulesSnapshot snapshot,
            CreatureId actor,
            BindingId bindingId
        ) =>
            snapshot.RuleBindings.TryGet(bindingId, out ActiveRuleBinding binding)
            && !binding.IsEnabled
            && binding.Owner == actor
            && binding.DefinitionId == RageRules.QuickTemperedRuleDefinitionId;
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

    internal sealed class CommitRageTemporaryHitPointsReducer
        : IOpReducer<CommitRageTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>
    {
        public ReductionResult<TemporaryHitPointsGrantOutcome> Reduce(
            ReductionContext<CommitRageTemporaryHitPointsOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            GrantTemporaryHitPointsOp grant = context.Op.Grant;
            RageEffectState receipt = context.Op.Intent.Receipt;
            if (
                !receipt.HasWorkflowReceipt
                || !ReferenceEquals(grant.Intent, context.Op.Intent)
                || grant.Source != RageRules.Source
                || grant.Target != receipt.Actor
                || grant.Origin != receipt.Origin
                || grant.Amount != receipt.OfferedTemporaryHitPoints
                || context.RootOpId != receipt.RootId
            )
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(
                    "Rage start intent does not match its THP offer."
                );
            if (
                !TemporaryHitPointsGrantReduction.TryPrepare(
                    state,
                    grant.Target,
                    grant.Amount,
                    grant.Source,
                    out TemporaryHitPointsGrantTransition transition,
                    out string rejection
                )
            )
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(rejection);
            if (
                !ActiveEffectReduction.TryGetCurrent(
                    state,
                    receipt.EffectId,
                    EffectStateVersion.Initial,
                    true,
                    out ActiveEffectInstance effect,
                    out rejection
                )
            )
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(rejection);
            if (
                !(effect.State is RageEffectState current)
                || !current.HasWorkflowReceipt
                || !current.ExactEquals(receipt)
                || current.Phase != RageStartPhase.Pending
                || current.HasGrantOutcome
            )
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(
                    "Rage THP settlement requires its exact pending receipt."
                );
            if (
                !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    receipt.BindingId,
                    true,
                    out _,
                    out rejection
                )
            )
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(rejection);

            transition = TemporaryHitPointsGrantReduction.Commit(
                state,
                facts,
                grant.Target,
                grant.Origin,
                grant.Source,
                transition
            );
            ActiveEffectReduction.CommitStateUpdate(
                state,
                effect,
                current.WithGrantTransition(transition),
                facts
            );
            return ReductionResult<TemporaryHitPointsGrantOutcome>.Accept(transition.Outcome);
        }
    }

    internal sealed class SettleRageStartReducer : IOpReducer<SettleRageStartOp, RageStartOutcome>
    {
        public ReductionResult<RageStartOutcome> Reduce(
            ReductionContext<SettleRageStartOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !ActiveEffectReduction.TryGetCurrent(
                    state,
                    context.Op.EffectId,
                    context.Op.ExpectedVersion,
                    true,
                    out ActiveEffectInstance effect,
                    out string rejection
                )
            )
                return ReductionResult<RageStartOutcome>.Reject(rejection);
            if (
                !(effect.State is RageEffectState receipt)
                || !receipt.HasWorkflowReceipt
                || receipt.EffectId != context.Op.EffectId
                || receipt.BindingId != context.Op.BindingId
                || receipt.RootId != context.RootOpId
                || receipt.Phase != RageStartPhase.Pending
                || !receipt.HasGrantOutcome
            )
                return ReductionResult<RageStartOutcome>.Reject(
                    "Rage settlement requires its exact THP-committed pending receipt."
                );
            if (
                !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    context.Op.BindingId,
                    true,
                    out _,
                    out rejection
                )
            )
                return ReductionResult<RageStartOutcome>.Reject(rejection);

            RageEffectState settled = receipt.Settle();
            ActiveEffectReduction.CommitStateUpdate(state, effect, settled, facts);
            return ReductionResult<RageStartOutcome>.Accept(CreateOutcome(settled));
        }

        internal static RageStartOutcome CreateOutcome(RageEffectState receipt)
        {
            if (receipt == null)
                throw new ArgumentNullException(nameof(receipt));
            if (
                !receipt.HasWorkflowReceipt
                || receipt.Phase != RageStartPhase.Settled
                || !receipt.HasGrantOutcome
            )
                throw new ArgumentException(
                    "A Rage outcome requires a settled workflow receipt.",
                    nameof(receipt)
                );
            return new RageStartOutcome(
                receipt.EffectId,
                receipt.GrantOutcome.Granted,
                receipt.GrantOutcome.CurrentAmount,
                receipt.StartedByQuickTempered
            );
        }
    }

    internal static class RageStartReceipt
    {
        internal static bool TryGetExact(
            RulesSnapshot snapshot,
            CreatureId actor,
            OpId rootId,
            BindingId triggerBinding,
            RageStartPhase phase,
            out ActiveEffectInstance effect,
            out ActiveRuleBinding binding,
            out RageEffectState receipt
        ) =>
            TryGetExact(
                snapshot,
                actor,
                RageEffectState.CreateEffectId(rootId, actor),
                RageEffectState.CreateBindingId(rootId, actor),
                RageHandlerSupport.CreateHealthOrigin(rootId, actor),
                rootId.Value,
                triggerBinding,
                phase,
                out effect,
                out binding,
                out receipt
            );

        private static bool TryGetExact(
            RulesSnapshot snapshot,
            CreatureId actor,
            ActiveEffectId effectId,
            BindingId bindingId,
            HealthChangeOriginId origin,
            long creationOrder,
            BindingId triggerBinding,
            RageStartPhase phase,
            out ActiveEffectInstance effect,
            out ActiveRuleBinding binding,
            out RageEffectState receipt
        )
        {
            effect = null;
            binding = null;
            receipt = null;
            return snapshot.ActiveEffects.TryGet(effectId, out effect)
                && effect.DefinitionId == RageActionDefinition.EffectDefinitionId
                && effect.SourceCreature == actor
                && effect.Source == RageRules.Source
                && effect.Status == ActiveEffectStatus.Active
                && effect.State is RageEffectState typed
                && (receipt = typed) != null
                && receipt.HasWorkflowReceipt
                && receipt.Actor == actor
                && receipt.EffectId == effectId
                && receipt.BindingId == bindingId
                && receipt.TriggerBinding == triggerBinding
                && receipt.RootId.Value == creationOrder
                && receipt.Origin == origin
                && receipt.Phase == phase
                && (phase != RageStartPhase.Settled || receipt.HasGrantOutcome)
                && snapshot.RuleBindings.TryGet(bindingId, out binding)
                && binding.IsEnabled
                && binding.Owner == actor
                && binding.EffectId.HasValue
                && binding.EffectId.Value == effectId
                && binding.DefinitionId == effect.DefinitionId
                && binding.Source == effect.Source
                && binding.CreationOrder == creationOrder;
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
        ) => RageHandlerSupport.Start(frame.Op.Actor, frame.RootId, default, definition, context);
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
        ) =>
            RageHandlerSupport.Start(
                frame.Op.Actor,
                frame.RootId,
                frame.Op.TriggerBinding,
                definition,
                context
            );
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
                    && effect.Source == RageRules.Source
                )
                .OrderBy(effect => effect.Id.Value, StringComparer.Ordinal)
                .ToArray();
            if (effects.Length == 0)
                return new RageEndOutcome(false);
            if (effects.Length != 1)
                throw new InvalidOperationException(
                    "A creature cannot have multiple Rage lifecycle instances."
                );

            ActiveEffectInstance effect = effects[0];
            if (!(effect.State is RageEffectState receipt))
                throw new InvalidOperationException(
                    "Completed Rage cleanup requires its settled feature receipt."
                );
            if (receipt.HasWorkflowReceipt && receipt.Phase == RageStartPhase.Pending)
            {
                await RageHandlerSupport.RequireResolved(
                    context.Dispatch(
                        new AbortPendingRageStartOp(receipt, effect.EffectStateVersion)
                    )
                );
                return new RageEndOutcome(true);
            }
            ActiveRuleBinding[] bindings = context
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .Where(binding => binding.EffectId.HasValue && binding.EffectId.Value == effect.Id)
                .ToArray();
            if (bindings.Length != 1)
                throw new InvalidOperationException(
                    "A Rage requires exactly one associated binding."
                );

            HealthChangeOriginId cleanupOrigin = receipt.HasWorkflowReceipt
                ? receipt.Origin
                : RageHandlerSupport.CreateHealthOrigin(frame.RootId, frame.Op.Actor);
            await RageHandlerSupport.RequireResolved(
                context.Dispatch(
                    new CommitRageEndOp(
                        frame.Op.Actor,
                        effect.Id,
                        bindings[0].Id,
                        effect.EffectStateVersion,
                        receipt,
                        cleanupOrigin
                    )
                )
            );
            return new RageEndOutcome(true);
        }
    }

    internal sealed class CommitRageEndReducer : IOpReducer<CommitRageEndOp, RageEndOutcome>
    {
        private readonly CommitTemporaryHitPointsRemovalReducer temporaryHitPointsRemoval =
            new CommitTemporaryHitPointsRemovalReducer();
        private readonly CommitTemporaryHitPointImmunityReducer temporaryHitPointImmunity =
            new CommitTemporaryHitPointImmunityReducer();
        private readonly RemoveActiveEffectReducer activeEffectRemoval =
            new RemoveActiveEffectReducer();

        public ReductionResult<RageEndOutcome> Reduce(
            ReductionContext<CommitRageEndOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !ActiveEffectReduction.TryGetCurrent(
                    state,
                    context.Op.EffectId,
                    context.Op.ExpectedVersion,
                    false,
                    out ActiveEffectInstance effect,
                    out string rejection
                )
                || effect.DefinitionId != RageActionDefinition.EffectDefinitionId
                || effect.SourceCreature != context.Op.Actor
                || effect.Source != RageRules.Source
                || !(effect.State is RageEffectState receipt)
                || !receipt.ExactEquals(context.Op.ExpectedState)
                || !TryValidateReceiptAndBinding(
                    context.Op,
                    context.RootOpId,
                    state,
                    effect,
                    receipt,
                    out rejection
                )
            )
                return ReductionResult<RageEndOutcome>.Reject(
                    rejection.Length > 0
                        ? rejection
                        : "Rage cleanup requires its exact lifecycle receipt."
                );

            ReductionResult<TemporaryHitPointsRemovalOutcome> removed =
                temporaryHitPointsRemoval.Reduce(
                    Translate(
                        context,
                        new CommitTemporaryHitPointsRemovalOp(
                            context.Op.Actor,
                            context.Op.CleanupOrigin,
                            RageRules.Source
                        )
                    ),
                    state,
                    facts
                );
            if (removed.IsRejected)
                return ReductionResult<RageEndOutcome>.Reject(removed.RejectionReason);

            ReductionResult<TemporaryHitPointImmunityOutcome> immune =
                temporaryHitPointImmunity.Reduce(
                    Translate(
                        context,
                        new CommitTemporaryHitPointImmunityOp(
                            context.Op.Actor,
                            context.Op.CleanupOrigin,
                            RageRules.Source
                        )
                    ),
                    state,
                    facts
                );
            if (immune.IsRejected)
                return ReductionResult<RageEndOutcome>.Reject(immune.RejectionReason);

            ReductionResult<ActiveEffectRemovalOutcome> effectRemoved = activeEffectRemoval.Reduce(
                Translate(
                    context,
                    new RemoveActiveEffectOp(
                        context.Op.EffectId,
                        context.Op.BindingId,
                        context.Op.ExpectedVersion,
                        RageRules.Source
                    )
                ),
                state,
                facts
            );
            if (effectRemoved.IsRejected)
                return ReductionResult<RageEndOutcome>.Reject(effectRemoved.RejectionReason);
            return ReductionResult<RageEndOutcome>.Accept(new RageEndOutcome(true));
        }

        private static bool TryValidateReceiptAndBinding(
            CommitRageEndOp operation,
            OpId rootId,
            RulesStateDraft state,
            ActiveEffectInstance effect,
            RageEffectState receipt,
            out string rejection
        )
        {
            bool active = effect.Status == ActiveEffectStatus.Active;
            if (
                !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    operation.BindingId,
                    active,
                    out ActiveRuleBinding binding,
                    out rejection
                )
                || binding.Owner != operation.Actor
                || binding.IsEnabled != active
            )
            {
                rejection =
                    rejection.Length > 0
                        ? rejection
                        : "Rage cleanup requires a lifecycle-consistent binding.";
                return false;
            }
            if (!receipt.HasWorkflowReceipt)
            {
                if (
                    operation.CleanupOrigin
                    != RageHandlerSupport.CreateHealthOrigin(rootId, operation.Actor)
                )
                {
                    rejection =
                        "Rage presentation cleanup requires this operation's health origin.";
                    return false;
                }
                rejection = string.Empty;
                return true;
            }
            EffectStateVersion settledVersion = EffectStateVersion.Initial.Next().Next();
            if (
                receipt.Phase != RageStartPhase.Settled
                || !receipt.HasGrantOutcome
                || receipt.Actor != operation.Actor
                || receipt.EffectId != operation.EffectId
                || receipt.BindingId != operation.BindingId
                || receipt.EffectId != RageEffectState.CreateEffectId(receipt.RootId, receipt.Actor)
                || receipt.BindingId
                    != RageEffectState.CreateBindingId(receipt.RootId, receipt.Actor)
                || receipt.Origin
                    != RageHandlerSupport.CreateHealthOrigin(receipt.RootId, receipt.Actor)
                || receipt.Origin != operation.CleanupOrigin
                || binding.CreationOrder != receipt.RootId.Value
                || !RageHandlerSupport.HasExactLifecycleCheckpoint(
                    effect,
                    binding,
                    settledVersion,
                    operation.ExpectedVersion
                )
            )
            {
                rejection = "Rage cleanup requires its exact settled lifecycle receipt.";
                return false;
            }
            rejection = string.Empty;
            return true;
        }

        private static ReductionContext<TChild> Translate<TChild>(
            ReductionContext<CommitRageEndOp> context,
            TChild operation
        ) =>
            new ReductionContext<TChild>(
                operation,
                context.SourceOpId,
                context.RootOpId,
                context.Source
            );
    }

    internal sealed class AbortPendingRageStartReducer
        : IOpReducer<AbortPendingRageStartOp, ActiveEffectRemovalOutcome>
    {
        public ReductionResult<ActiveEffectRemovalOutcome> Reduce(
            ReductionContext<AbortPendingRageStartOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            RageEffectState expected = context.Op.Receipt;
            if (!expected.HasWorkflowReceipt)
                return ReductionResult<ActiveEffectRemovalOutcome>.Reject(
                    "Pending Rage cleanup requires its exact effect and binding receipt."
                );
            EffectStateVersion expectedCheckpointVersion = expected.HasGrantOutcome
                ? EffectStateVersion.Initial.Next()
                : EffectStateVersion.Initial;
            if (
                expected.Phase != RageStartPhase.Pending
                || expected.EffectId
                    != RageEffectState.CreateEffectId(expected.RootId, expected.Actor)
                || expected.BindingId
                    != RageEffectState.CreateBindingId(expected.RootId, expected.Actor)
                || expected.Origin
                    != RageHandlerSupport.CreateHealthOrigin(expected.RootId, expected.Actor)
                || !ActiveEffectReduction.TryGetCurrent(
                    state,
                    expected.EffectId,
                    context.Op.ExpectedVersion,
                    false,
                    out ActiveEffectInstance effect,
                    out string rejection
                )
                || effect.SourceCreature != expected.Actor
                || effect.DefinitionId != RageActionDefinition.EffectDefinitionId
                || effect.Source != RageRules.Source
                || !(effect.State is RageEffectState receipt)
                || !receipt.HasWorkflowReceipt
                || !receipt.ExactEquals(expected)
                || !ActiveEffectReduction.TryGetAssociatedBinding(
                    state,
                    effect,
                    expected.BindingId,
                    false,
                    out ActiveRuleBinding binding,
                    out rejection
                )
                || binding.Owner != expected.Actor
                || binding.CreationOrder != expected.RootId.Value
                || !RageHandlerSupport.HasExactLifecycleCheckpoint(
                    effect,
                    binding,
                    expectedCheckpointVersion,
                    context.Op.ExpectedVersion
                )
            )
                return ReductionResult<ActiveEffectRemovalOutcome>.Reject(
                    "Pending Rage cleanup requires its exact effect and binding receipt."
                );

            if (
                receipt.HasGrantOutcome
                && !receipt.PriorTemporaryHitPoints.Equals(receipt.CommittedTemporaryHitPoints)
            )
            {
                if (
                    !TemporaryHitPointsGrantReduction.TryPrepareExactRestoration(
                        state,
                        receipt.Actor,
                        RageRules.Source,
                        receipt.CommittedTemporaryHitPoints,
                        receipt.PriorTemporaryHitPoints,
                        out TemporaryHitPointsPoolRestorationTransition restoration,
                        out rejection
                    )
                )
                    return ReductionResult<ActiveEffectRemovalOutcome>.Reject(rejection);
                TemporaryHitPointsGrantReduction.CommitExactRestoration(
                    state,
                    facts,
                    receipt.Actor,
                    receipt.Origin,
                    RageRules.Source,
                    restoration
                );
            }

            return ReductionResult<ActiveEffectRemovalOutcome>.Accept(
                ActiveEffectReduction.CommitRemoval(state, effect, binding, facts)
            );
        }
    }

    internal static class RageHandlerSupport
    {
        internal static bool HasExactLifecycleCheckpoint(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            EffectStateVersion receiptVersion,
            EffectStateVersion requestedVersion
        ) =>
            (
                effect.Status == ActiveEffectStatus.Active
                && binding.IsEnabled
                && requestedVersion == receiptVersion
            )
            || (
                effect.Status == ActiveEffectStatus.Expired
                && !binding.IsEnabled
                && requestedVersion == receiptVersion.Next()
            );

        public static async ValueTask<RageStartOutcome> Start(
            CreatureId actor,
            OpId rootId,
            BindingId quickTemperedTrigger,
            RageActionDefinition definition,
            OpHandlerContext context
        )
        {
            RageActorState actorState = definition.GetActorState(context.Snapshot, actor);
            int offeredTemporaryHitPoints = Math.Max(
                0,
                actorState.Level + actorState.ConstitutionModifier
            );
            RageEffectState pendingReceipt = RageEffectState.CreatePending(
                actor,
                quickTemperedTrigger,
                rootId,
                offeredTemporaryHitPoints
            );
            ActiveEffectId effectId = pendingReceipt.EffectId;
            BindingId bindingId = pendingReceipt.BindingId;
            ActiveEffectInstance effect = CreateEffect(pendingReceipt);
            ActiveRuleBinding binding = CreateBinding(pendingReceipt);
            RageStartGrantIntent intent = new RageStartGrantIntent(pendingReceipt);
            GrantTemporaryHitPointsOp grant = new GrantTemporaryHitPointsOp(
                actor,
                offeredTemporaryHitPoints,
                CreateHealthOrigin(rootId, actor),
                RageRules.Source,
                intent
            );
            ObserverFailureState recoveredFailures = ObserverFailureState.CreateEmpty(
                "Multiple Rage workflow failures were preserved."
            );

            try
            {
                await RequireResolved(context.Dispatch(new CreateActiveEffectOp(effect, binding)));
            }
            catch (Exception failure)
            {
                if (!IsExactPendingCreation(context.Snapshot, effect, binding))
                    throw;
                recoveredFailures = recoveredFailures.Add(failure);
            }

            recoveredFailures = await DispatchGrantOnce(
                context,
                grant,
                actor,
                rootId,
                quickTemperedTrigger,
                recoveredFailures
            );
            RageSettlementAttempt settlement = await DispatchSettlementOnce(
                context,
                new SettleRageStartOp(effectId, bindingId, EffectStateVersion.Initial.Next()),
                actor,
                rootId,
                quickTemperedTrigger,
                recoveredFailures
            );
            RageStartOutcome outcome = settlement.Outcome;
            recoveredFailures = settlement.RecoveredFailures;
            recoveredFailures.ThrowIfAny();
            return outcome;
        }

        private static async ValueTask<ObserverFailureState> DispatchGrantOnce(
            OpHandlerContext context,
            GrantTemporaryHitPointsOp grant,
            CreatureId actor,
            OpId rootId,
            BindingId quickTemperedTrigger,
            ObserverFailureState recoveredFailures
        )
        {
            OpResult<TemporaryHitPointsGrantOutcome> result;
            try
            {
                result = await context.Dispatch(grant);
            }
            catch (Exception failure)
            {
                recoveredFailures = recoveredFailures.Add(failure);
                if (!HasGrantCheckpoint(context.Snapshot, actor, rootId, quickTemperedTrigger))
                {
                    recoveredFailures = await AbortPendingStart(
                        context,
                        actor,
                        rootId,
                        quickTemperedTrigger,
                        recoveredFailures
                    );
                    recoveredFailures.ThrowIfAny();
                    throw;
                }
                return recoveredFailures;
            }

            if (result is ResolvedOpResult<TemporaryHitPointsGrantOutcome>)
                return recoveredFailures;

            // A structural result is authoritative pipeline behavior, not an uncertain commit.
            // In particular, an Invalid result must never bypass the public operation by invoking
            // Rage's private reducer directly, even if unrelated state resembles a completed grant.
            recoveredFailures = recoveredFailures.Add(
                CreateUnresolvedOperationFailure(result, "Rage temporary Hit Point grant")
            );
            recoveredFailures = await AbortPendingStart(
                context,
                actor,
                rootId,
                quickTemperedTrigger,
                recoveredFailures
            );
            recoveredFailures.ThrowIfAny();
            throw new InvalidOperationException("The Rage grant failure was not propagated.");
        }

        private static async ValueTask<RageSettlementAttempt> DispatchSettlementOnce(
            OpHandlerContext context,
            SettleRageStartOp settlement,
            CreatureId actor,
            OpId rootId,
            BindingId quickTemperedTrigger,
            ObserverFailureState recoveredFailures
        )
        {
            OpResult<RageStartOutcome> result;
            try
            {
                result = await context.Dispatch(settlement);
            }
            catch (Exception failure)
            {
                recoveredFailures = recoveredFailures.Add(failure);
                if (
                    RageStartReceipt.TryGetExact(
                        context.Snapshot,
                        actor,
                        rootId,
                        quickTemperedTrigger,
                        RageStartPhase.Settled,
                        out _,
                        out _,
                        out RageEffectState receipt
                    )
                )
                {
                    return new RageSettlementAttempt(
                        SettleRageStartReducer.CreateOutcome(receipt),
                        recoveredFailures
                    );
                }
                recoveredFailures = await AbortPendingStart(
                    context,
                    actor,
                    rootId,
                    quickTemperedTrigger,
                    recoveredFailures
                );
                recoveredFailures.ThrowIfAny();
                throw;
            }

            if (result is ResolvedOpResult<RageStartOutcome> resolved)
                return new RageSettlementAttempt(resolved.Value, recoveredFailures);

            // Settlement is dispatched once. Invalid or interrupted pipeline results cannot be
            // reclassified as thrown post-commit delivery failures and therefore cannot retry.
            recoveredFailures = recoveredFailures.Add(
                CreateUnresolvedOperationFailure(result, "Rage start settlement")
            );
            recoveredFailures = await AbortPendingStart(
                context,
                actor,
                rootId,
                quickTemperedTrigger,
                recoveredFailures
            );
            recoveredFailures.ThrowIfAny();
            throw new InvalidOperationException("The Rage settlement failure was not propagated.");
        }

        private static async ValueTask<ObserverFailureState> AbortPendingStart(
            OpHandlerContext context,
            CreatureId actor,
            OpId rootId,
            BindingId quickTemperedTrigger,
            ObserverFailureState recoveredFailures
        )
        {
            if (
                !RageStartReceipt.TryGetExact(
                    context.Snapshot,
                    actor,
                    rootId,
                    quickTemperedTrigger,
                    RageStartPhase.Pending,
                    out ActiveEffectInstance effect,
                    out _,
                    out RageEffectState receipt
                )
            )
                return recoveredFailures;

            OpResult<ActiveEffectRemovalOutcome> result;
            try
            {
                result = await context.Dispatch(
                    new AbortPendingRageStartOp(receipt, effect.EffectStateVersion)
                );
            }
            catch (Exception cleanupFailure)
            {
                return recoveredFailures.Add(cleanupFailure);
            }

            if (result is ResolvedOpResult<ActiveEffectRemovalOutcome>)
                return recoveredFailures;
            return recoveredFailures.Add(
                CreateUnresolvedOperationFailure(result, "Pending Rage cleanup")
            );
        }

        private static Exception CreateUnresolvedOperationFailure<TResult>(
            OpResult<TResult> result,
            string operationName
        )
        {
            if (result is InvalidOpResult<TResult> invalid)
                return new InvalidOperationException(invalid.Reason);
            if (result is InterruptedOpResult<TResult>)
                return new InvalidOperationException($"{operationName} was interrupted.");
            return new InvalidOperationException($"{operationName} did not resolve.");
        }

        private readonly struct RageSettlementAttempt
        {
            public RageSettlementAttempt(
                RageStartOutcome outcome,
                ObserverFailureState recoveredFailures
            )
            {
                Outcome = outcome;
                RecoveredFailures = recoveredFailures;
            }

            public RageStartOutcome Outcome { get; }

            public ObserverFailureState RecoveredFailures { get; }
        }

        private static ActiveEffectInstance CreateEffect(RageEffectState receipt) =>
            new ActiveEffectInstance(
                receipt.EffectId,
                RageActionDefinition.EffectDefinitionId,
                receipt.Actor,
                RageRules.Source,
                EffectDuration.OneMinute,
                receipt
            );

        private static ActiveRuleBinding CreateBinding(RageEffectState receipt) =>
            new ActiveRuleBinding(
                receipt.BindingId,
                RageActionDefinition.EffectDefinitionId,
                receipt.Actor,
                receipt.EffectId,
                RageRules.Source,
                receipt.RootId.Value
            );

        private static bool IsExactPendingCreation(
            RulesSnapshot snapshot,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding
        ) =>
            snapshot.ActiveEffects.TryGet(effect.Id, out ActiveEffectInstance committedEffect)
            && ActiveEffectInstanceExactEquality.Equals(committedEffect, effect)
            && snapshot.RuleBindings.TryGet(binding.Id, out ActiveRuleBinding committedBinding)
            && committedBinding.Equals(binding);

        private static bool HasGrantCheckpoint(
            RulesSnapshot snapshot,
            CreatureId actor,
            OpId rootId,
            BindingId quickTemperedTrigger
        ) =>
            RageStartReceipt.TryGetExact(
                snapshot,
                actor,
                rootId,
                quickTemperedTrigger,
                RageStartPhase.Pending,
                out _,
                out _,
                out RageEffectState receipt
            ) && receipt.HasGrantOutcome;

        public static HealthChangeOriginId CreateHealthOrigin(OpId rootId, CreatureId actor) =>
            new HealthChangeOriginId($"rage-root-{rootId.Value}-{actor.Value}");

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
