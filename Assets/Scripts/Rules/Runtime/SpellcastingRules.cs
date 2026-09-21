using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Stores immutable player-selected creature identities for a rules-native spell cast.
    /// </summary>
    /// <remarks>
    /// Light is self-targeted by definition metadata and therefore uses <see cref="Empty"/>.
    /// This payload reserves the stable-ID boundary required by future targeted spell migrations.
    /// </remarks>
    public sealed class SpellCastSelection : IEquatable<SpellCastSelection>
    {
        private readonly IReadOnlyList<CreatureId> creatures;
        private readonly SpellAreaDirection areaDirection;

        /// <summary>Creates an immutable selection from stable creature identities.</summary>
        /// <param name="creatures">Selected creature IDs in player-declared order.</param>
        public SpellCastSelection(IEnumerable<CreatureId> creatures)
            : this(creatures, false, default) { }

        /// <summary>Creates an immutable directed-area selection from stable creature identities.</summary>
        /// <param name="creatures">Selected creature IDs in player-declared order.</param>
        /// <param name="areaDirection">The direction used to place the selected area.</param>
        public SpellCastSelection(
            IEnumerable<CreatureId> creatures,
            SpellAreaDirection areaDirection
        )
            : this(creatures, true, areaDirection) { }

        private SpellCastSelection(
            IEnumerable<CreatureId> creatures,
            bool hasAreaDirection,
            SpellAreaDirection areaDirection
        )
        {
            if (creatures == null)
                throw new ArgumentNullException(nameof(creatures));
            if (!Enum.IsDefined(typeof(SpellAreaDirection), areaDirection))
                throw new ArgumentOutOfRangeException(nameof(areaDirection));
            CreatureId[] copied = creatures.ToArray();
            if (copied.Any(creature => creature.IsEmpty))
                throw new ArgumentException(
                    "Selected creature IDs cannot be empty.",
                    nameof(creatures)
                );
            this.creatures = Array.AsReadOnly(copied);
            HasAreaDirection = hasAreaDirection;
            this.areaDirection = areaDirection;
        }

        /// <summary>Gets the shared selection for spells requiring no player-selected creatures.</summary>
        public static SpellCastSelection Empty { get; } =
            new SpellCastSelection(Array.Empty<CreatureId>());

        /// <summary>Gets player-selected creature IDs in their declared order.</summary>
        public IReadOnlyList<CreatureId> Creatures => creatures;

        /// <summary>Gets whether the selection retains a directed-area placement.</summary>
        public bool HasAreaDirection { get; }

        /// <summary>Gets the directed-area placement.</summary>
        /// <exception cref="InvalidOperationException">This is not a directed-area selection.</exception>
        public SpellAreaDirection AreaDirection =>
            HasAreaDirection
                ? areaDirection
                : throw new InvalidOperationException(
                    "The spell selection has no directed-area placement."
                );

        /// <inheritdoc/>
        public bool Equals(SpellCastSelection other) =>
            other != null
            && HasAreaDirection == other.HasAreaDirection
            && (!HasAreaDirection || areaDirection == other.areaDirection)
            && creatures.SequenceEqual(other.creatures);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellCastSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int hash = 17;
            foreach (CreatureId creature in creatures)
                hash = HashCode.Combine(hash, creature);
            return HashCode.Combine(hash, HasAreaDirection, areaDirection);
        }
    }

    /// <summary>Requests one generic, definition-backed Cast a Spell action.</summary>
    public sealed class CastSpellActionOp : ActionOp<CastSpellOutcome>
    {
        /// <summary>Creates an immutable root request without caller-computed authorization.</summary>
        /// <param name="actor">The creature attempting the cast.</param>
        /// <param name="spell">The exact spell identity and requested rank.</param>
        /// <param name="variant">The definition-owned action-cost variant.</param>
        /// <param name="selection">Immutable player-selected creature IDs.</param>
        public CastSpellActionOp(
            CreatureId actor,
            SpellReference spell,
            SpellActionVariant variant,
            SpellCastSelection selection
        )
            : base(actor, CastSpellActionDefinition.DefinitionId)
        {
            Spell = spell;
            Variant = variant;
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        }

        /// <summary>Gets the exact spell and rank requested by the caster.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the selected definition-owned action variant.</summary>
        public SpellActionVariant Variant { get; }

        /// <summary>Gets immutable player-selected target identities.</summary>
        public SpellCastSelection Selection { get; }

        /// <inheritdoc/>
        public override ActionProfile GetBaseProfile(IActionCatalog catalog)
        {
            if (catalog is not ISpellActionCatalog spells)
                throw new InvalidOperationException(
                    "Cast a Spell requires a catalog with spell definitions and spellbooks."
                );
            return new CastSpellActionDefinition(spells).CreateProfile(Actor, Spell, Variant);
        }
    }

    /// <summary>
    /// Owns availability, validation, resource binding, and profile construction for Cast a Spell.
    /// </summary>
    public sealed class CastSpellActionDefinition
    {
        private readonly ISpellActionCatalog catalog;

        /// <summary>Gets the stable rules action identity shared by every migrated spell.</summary>
        public static ActionDefinitionId DefinitionId { get; } =
            new ActionDefinitionId("cast-spell");

        /// <summary>Creates a definition over one encounter's spell catalog and spellbooks.</summary>
        /// <param name="catalog">Definitions and immutable prepared spellbooks for the encounter.</param>
        public CastSpellActionDefinition(ISpellActionCatalog catalog) =>
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        /// <summary>Gets current rules-owned availability for one exact spell variant.</summary>
        /// <param name="snapshot">The authoritative encounter snapshot.</param>
        /// <param name="actor">The prospective caster.</param>
        /// <param name="spell">The exact prospective spell and rank.</param>
        /// <param name="variant">The proposed action-cost variant.</param>
        /// <returns>Available or the first rules-owned reason the cast cannot begin.</returns>
        public ActionAvailability GetAvailability(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellReference spell,
            SpellActionVariant variant
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.Creatures.Contains(actor))
                return ActionAvailability.Unavailable("The caster is not registered.");
            if (!snapshot.Health.IsAlive(actor))
                return ActionAvailability.Unavailable("The caster cannot act.");
            if (!catalog.TryGetSpell(spell, out SpellDefinition definition))
                return ActionAvailability.Unavailable("The spell reference is unknown.");
            if (!definition.Variants.Contains(variant))
                return ActionAvailability.Unavailable("The spell action variant is unavailable.");
            if (!snapshot.ActionEconomy.CanSpendActions(actor, variant.Actions))
                return ActionAvailability.Unavailable("The caster does not have enough actions.");
            ISpellBook book = catalog.GetSpellBook(actor);
            SpellCastAuthorization binding = book.BindResource(actor, spell);
            if (!binding.IsAuthorized)
                return ActionAvailability.Unavailable(binding.Reason);
            SpellCastAuthorization authorization = book.Authorize(
                actor,
                spell,
                new SnapshotSpellSlotStateReader(snapshot)
            );
            if (!authorization.IsAuthorized)
                return ActionAvailability.Unavailable(authorization.Reason);
            return binding.Equals(authorization)
                ? ActionAvailability.Available
                : ActionAvailability.Unavailable(
                    "The spell's prepared resource binding does not match live authorization."
                );
        }

        /// <summary>
        /// Validates one complete request through the same decisions used by availability.
        /// </summary>
        /// <param name="snapshot">The authoritative snapshot immediately before costs.</param>
        /// <param name="operation">The complete immutable root request.</param>
        /// <returns>A valid result or the first structural rejection reason.</returns>
        public ActionValidationResult Validate(RulesSnapshot snapshot, CastSpellActionOp operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            ActionAvailability availability = GetAvailability(
                snapshot,
                operation.Actor,
                operation.Spell,
                operation.Variant
            );
            return availability is UnavailableActionAvailability unavailable
                ? ActionValidationResult.Invalid(unavailable.Reason)
                : ActionValidationResult.Valid;
        }

        /// <summary>
        /// Builds the immutable profile and definition-derived cantrip or slot cost.
        /// </summary>
        /// <param name="actor">The creature whose preparation binds the resource.</param>
        /// <param name="spell">The exact spell and rank being profiled.</param>
        /// <param name="variant">The definition-owned action-cost variant.</param>
        /// <returns>The frozen base profile consumed by the action lifecycle.</returns>
        public ActionProfile CreateProfile(
            CreatureId actor,
            SpellReference spell,
            SpellActionVariant variant
        )
        {
            if (!catalog.TryGetSpell(spell, out SpellDefinition definition))
                return ActionProfile.Create(
                    ActionCost.FromActions(variant.Actions),
                    Array.Empty<Trait>()
                );
            SpellCastAuthorization binding = catalog.GetSpellBook(actor).BindResource(actor, spell);
            RuleCost[] costs =
                binding.Kind == SpellCastResourceKind.SpellSlot
                    ? new[] { RuleCost.SpellSlot(binding.Pool) }
                    : Array.Empty<RuleCost>();
            return new ActionProfile(
                ActionCost.FromActions(variant.Actions),
                costs,
                definition.Traits
            );
        }

        /// <summary>Creates the immutable root operation for a completed Unity or AI selection.</summary>
        /// <param name="actor">The casting creature.</param>
        /// <param name="spell">The exact spell and rank.</param>
        /// <param name="variant">The definition-owned action-cost variant.</param>
        /// <param name="selection">The completed immutable target selection.</param>
        /// <returns>A caller-unprivileged root operation.</returns>
        public CastSpellActionOp CreateOp(
            CreatureId actor,
            SpellReference spell,
            SpellActionVariant variant,
            SpellCastSelection selection
        ) => new CastSpellActionOp(actor, spell, variant, selection);
    }

    /// <summary>Reports the effects and spell attacks produced by one resolved spell cast.</summary>
    public sealed class CastSpellOutcome
    {
        /// <summary>Creates the structural result of a resolved generic spell cast.</summary>
        /// <param name="actor">The caster that resolved the action.</param>
        /// <param name="spell">The exact spell identity and cast rank.</param>
        /// <param name="createdEffects">All active effects committed by the cast.</param>
        /// <param name="attackResolutions">The actual spell-attack outcomes produced by the cast.</param>
        public CastSpellOutcome(
            CreatureId actor,
            SpellReference spell,
            IEnumerable<ActiveEffectId> createdEffects,
            IEnumerable<SpellAttackResolution> attackResolutions
        )
            : this(
                actor,
                spell,
                createdEffects,
                attackResolutions,
                Array.Empty<SpellTargetResolution>()
            ) { }

        /// <summary>Creates a result that also carries feature-owned per-target outcomes.</summary>
        /// <param name="actor">The caster that resolved the action.</param>
        /// <param name="spell">The exact spell identity and cast rank.</param>
        /// <param name="createdEffects">All active effects committed by the cast.</param>
        /// <param name="attackResolutions">The actual spell-attack outcomes produced by the cast.</param>
        /// <param name="targetResolutions">Feature-owned per-target outcomes.</param>
        public CastSpellOutcome(
            CreatureId actor,
            SpellReference spell,
            IEnumerable<ActiveEffectId> createdEffects,
            IEnumerable<SpellAttackResolution> attackResolutions,
            IEnumerable<SpellTargetResolution> targetResolutions
        )
        {
            Actor = actor;
            Spell = spell;
            CreatedEffects = new ReadOnlyCollection<ActiveEffectId>(
                (
                    createdEffects ?? throw new ArgumentNullException(nameof(createdEffects))
                ).ToArray()
            );
            AttackResolutions = new ReadOnlyCollection<SpellAttackResolution>(
                (
                    attackResolutions ?? throw new ArgumentNullException(nameof(attackResolutions))
                ).ToArray()
            );
            TargetResolutions = new ReadOnlyCollection<SpellTargetResolution>(
                (
                    targetResolutions ?? throw new ArgumentNullException(nameof(targetResolutions))
                ).ToArray()
            );
        }

        /// <summary>Gets the creature that cast the spell.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the exact spell and rank that resolved.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the active effects created by the cast.</summary>
        public IReadOnlyList<ActiveEffectId> CreatedEffects { get; }

        /// <summary>Gets spell-attack outcomes produced during this cast in definition order.</summary>
        public IReadOnlyList<SpellAttackResolution> AttackResolutions { get; }

        /// <summary>Gets feature-owned save, damage, healing, and condition outcomes.</summary>
        public IReadOnlyList<SpellTargetResolution> TargetResolutions { get; }
    }

    /// <summary>Registers generic spell validation and active-effect creation.</summary>
    public static class SpellcastingRuleDispatcherExtensions
    {
        /// <summary>Adds generic Cast a Spell validation and resolution to a dispatcher.</summary>
        /// <param name="builder">The dispatcher composition being configured.</param>
        /// <param name="catalog">Encounter spell definitions and prepared spellbooks.</param>
        /// <returns>The same builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseSpellcastingRules(
            this RuleDispatcherBuilder builder,
            ISpellActionCatalog catalog
        ) =>
            UseSpellcastingRules(
                builder,
                catalog,
                UnsupportedSpellAttackResolutionDataProvider.Instance,
                UnsupportedSpellTargetingDataProvider.Instance
            );

        /// <summary>Adds Cast a Spell with an explicit spell-attack Unity or test adapter.</summary>
        /// <param name="builder">The dispatcher composition being configured.</param>
        /// <param name="catalog">Encounter spell definitions and prepared spellbooks.</param>
        /// <param name="resolutionData">Current target and spell-attack resolution data.</param>
        /// <returns>The same builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseSpellcastingRules(
            this RuleDispatcherBuilder builder,
            ISpellActionCatalog catalog,
            ISpellAttackResolutionDataProvider resolutionData
        ) =>
            UseSpellcastingRules(
                builder,
                catalog,
                resolutionData,
                resolutionData as ISpellTargetingDataProvider
                    ?? UnsupportedSpellTargetingDataProvider.Instance
            );

        /// <summary>Adds Cast a Spell with explicit attack and feature-targeting adapters.</summary>
        /// <param name="builder">The dispatcher composition being configured.</param>
        /// <param name="catalog">Encounter spell definitions and prepared spellbooks.</param>
        /// <param name="resolutionData">Current spell-attack resolution data.</param>
        /// <param name="targetingData">Current feature-spell geometry and membership data.</param>
        /// <returns>The same builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseSpellcastingRules(
            this RuleDispatcherBuilder builder,
            ISpellActionCatalog catalog,
            ISpellAttackResolutionDataProvider resolutionData,
            ISpellTargetingDataProvider targetingData
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (resolutionData == null)
                throw new ArgumentNullException(nameof(resolutionData));
            if (targetingData == null)
                throw new ArgumentNullException(nameof(targetingData));
            CastSpellActionDefinition definition = new CastSpellActionDefinition(catalog);
            return builder
                .RegisterActionValidator(
                    new CastSpellActionValidator(definition, catalog, resolutionData, targetingData)
                )
                .RegisterHandler<CastSpellActionOp, CastSpellOutcome>(
                    new CastSpellActionHandler(catalog)
                )
                .RegisterHandler<ResolveSpellAttackOp, SpellAttackResolution>(
                    new ResolveSpellAttackHandler(catalog, resolutionData),
                    InvocationPolicy.NestedOnly
                );
        }
    }

    internal sealed class CastSpellActionValidator : IActionValidator<CastSpellActionOp>
    {
        private readonly CastSpellActionDefinition definition;
        private readonly ISpellDefinitionCatalog catalog;
        private readonly ISpellAttackResolutionDataProvider resolutionData;
        private readonly ISpellTargetingDataProvider targetingData;

        public CastSpellActionValidator(
            CastSpellActionDefinition definition,
            ISpellDefinitionCatalog catalog,
            ISpellAttackResolutionDataProvider resolutionData,
            ISpellTargetingDataProvider targetingData
        )
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.resolutionData =
                resolutionData ?? throw new ArgumentNullException(nameof(resolutionData));
            this.targetingData =
                targetingData ?? throw new ArgumentNullException(nameof(targetingData));
        }

        public ActionValidationResult Validate(
            OpFrame<CastSpellActionOp> frame,
            RulesSnapshot snapshot
        )
        {
            ActionValidationResult common = definition.Validate(snapshot, frame.Op);
            if (common is not ActionValidationResult.ValidActionValidationResult)
                return common;
            if (
                catalog is ISpellActionCatalog actions
                && actions.TryGetCastRule(frame.Op.Spell.Spell, out ISpellCastRule rule)
            )
            {
                ActionValidationResult feature = rule.Validate(snapshot, frame.Op);
                return feature is ActionValidationResult.ValidActionValidationResult
                    ? targetingData.ValidateSelection(
                        snapshot,
                        frame.Op.Actor,
                        rule.GetSelection(frame.Op.Variant),
                        frame.Op.Selection
                    )
                    : feature;
            }
            if (!catalog.TryGetSpell(frame.Op.Spell, out SpellDefinition spell))
                return ActionValidationResult.Invalid("The spell reference is unknown.");
            if (spell.Attacks.Count == 0)
                return ActionValidationResult.Valid;
            if (!snapshot.MultipleAttackPenalty.Contains(frame.Op.Actor))
                return ActionValidationResult.Invalid(
                    "The caster has no multiple-attack-penalty state."
                );
            if (
                spell.Attacks.Count != 1
                || spell.Attacks[0].Target is not OneCreatureSpellAttackTarget
            )
                return ActionValidationResult.Invalid(
                    "The spell attack target structure is unsupported."
                );
            if (frame.Op.Selection.Creatures.Count != 1)
                return ActionValidationResult.Invalid(
                    "The spell attack requires exactly one creature target."
                );
            CreatureId target = frame.Op.Selection.Creatures[0];
            if (!snapshot.Creatures.Contains(target))
                return ActionValidationResult.Invalid("The selected creature is not registered.");
            if (!snapshot.Health.TryGet(target, out HealthState health) || health.Current == 0)
                return ActionValidationResult.Invalid("The selected creature is not alive.");
            return resolutionData.Validate(snapshot, frame.Op.Actor, spell.Attacks[0], target);
        }
    }

    internal sealed class UnsupportedSpellTargetingDataProvider : ISpellTargetingDataProvider
    {
        public static UnsupportedSpellTargetingDataProvider Instance { get; } = new();

        private UnsupportedSpellTargetingDataProvider() { }

        public ActionValidationResult ValidateSelection(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellSelectionProfile profile,
            SpellCastSelection selection
        ) => ActionValidationResult.Invalid("Feature spell targeting is not configured.");
    }

    internal sealed class CastSpellActionHandler : IOpHandler<CastSpellActionOp, CastSpellOutcome>
    {
        private readonly ISpellActionCatalog catalog;

        public CastSpellActionHandler(ISpellActionCatalog catalog) =>
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        public async ValueTask<CastSpellOutcome> Handle(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context
        )
        {
            if (!catalog.TryGetSpell(frame.Op.Spell, out SpellDefinition definition))
                throw new InvalidOperationException("A validated spell definition disappeared.");

            if (catalog.TryGetCastRule(frame.Op.Spell.Spell, out ISpellCastRule feature))
            {
                SpellFeatureOutcome featureOutcome = await feature.Resolve(frame, context, catalog);
                return new CastSpellOutcome(
                    frame.Op.Actor,
                    frame.Op.Spell,
                    featureOutcome.CreatedEffects,
                    Array.Empty<SpellAttackResolution>(),
                    featureOutcome.Targets
                );
            }

            List<ActiveEffectId> created = new();
            List<SpellAttackResolution> attacks = new();
            for (int index = 0; index < definition.Effects.Count; index++)
            {
                SpellEffectDirective directive = definition.Effects[index];
                CreatureId target = ResolveTarget(directive, frame.Op);
                string instanceKey = string.Concat(
                    frame.Id.Value.ToString(CultureInfo.InvariantCulture),
                    ":",
                    index.ToString(CultureInfo.InvariantCulture)
                );
                var identity = context.CreateActiveEffectIdentity("spell", instanceKey);
                ActiveEffectId effectId = identity.EffectId;
                BindingId bindingId = identity.BindingId;
                RuleSource source = RuleSource.FromSlug(frame.Op.Spell.Spell.Value);
                ActiveEffectInstance effect = new ActiveEffectInstance(
                    effectId,
                    directive.DefinitionId,
                    frame.Op.Actor,
                    source,
                    directive.Duration,
                    new SpellEffectState(frame.Op.Spell, target)
                );
                ActiveRuleBinding binding = new ActiveRuleBinding(
                    bindingId,
                    directive.DefinitionId,
                    frame.Op.Actor,
                    effectId,
                    source,
                    frame.Id.Value
                );
                OpResult<ActiveEffectCreationOutcome> result = await context.Dispatch(
                    new CreateActiveEffectOp(effect, binding)
                );
                if (result is not ResolvedOpResult<ActiveEffectCreationOutcome>)
                    throw new InvalidOperationException(
                        "Spell active-effect creation did not resolve."
                    );
                created.Add(effectId);
            }
            if (definition.Attacks.Count > 0)
            {
                CreatureId target = frame.Op.Selection.Creatures.Single();
                OpResult<SpellAttackResolution> result = await context.Dispatch(
                    new ResolveSpellAttackOp(frame.Op.Actor, frame.Op.Spell, target)
                );
                if (result is not ResolvedOpResult<SpellAttackResolution> resolved)
                    throw new InvalidOperationException("Spell attack resolution did not resolve.");
                SpellAttackResolution resolution = resolved.Value;
                attacks.Add(resolution);
                if (resolution.Hit && resolution.FinalDamage > 0)
                {
                    OpResult<DamageOutcome> damage = await context.Dispatch(
                        new ApplyDamageOp(
                            target,
                            resolution.FinalDamage,
                            new HealthChangeOriginId($"spell-{frame.RootId.Value}"),
                            RuleSource.FromSlug(frame.Op.Spell.Spell.Value)
                        )
                    );
                    if (damage is not ResolvedOpResult<DamageOutcome>)
                        throw new InvalidOperationException("Spell attack damage did not resolve.");
                }
                OpResult<MultipleAttackPenaltyState> advanced = await context.Dispatch(
                    new AdvanceMultipleAttackPenaltyOp(frame.Op.Actor)
                );
                if (advanced is not ResolvedOpResult<MultipleAttackPenaltyState>)
                    throw new InvalidOperationException(
                        "Spell attack MAP advancement did not resolve."
                    );
            }
            return new CastSpellOutcome(frame.Op.Actor, frame.Op.Spell, created, attacks);
        }

        private static CreatureId ResolveTarget(
            SpellEffectDirective directive,
            CastSpellActionOp operation
        ) =>
            string.Equals(directive.Target, "self", StringComparison.Ordinal)
                ? operation.Actor
                : throw new InvalidOperationException(
                    $"Unsupported spell-effect target '{directive.Target}'."
                );
    }
}
