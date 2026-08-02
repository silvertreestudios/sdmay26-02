using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly SpellAreaPlacement areaPlacement;

        /// <summary>Creates an immutable selection from stable creature identities.</summary>
        /// <param name="creatures">Selected creature IDs in player-declared order.</param>
        public SpellCastSelection(IEnumerable<CreatureId> creatures)
            : this(creatures, default, hasAreaPlacement: false) { }

        /// <summary>Creates an immutable exact area placement and affected-creature selection.</summary>
        public SpellCastSelection(
            SpellAreaPlacement areaPlacement,
            IEnumerable<CreatureId> creatures
        )
            : this(creatures, areaPlacement, hasAreaPlacement: true) { }

        private SpellCastSelection(
            IEnumerable<CreatureId> creatures,
            SpellAreaPlacement areaPlacement,
            bool hasAreaPlacement
        )
        {
            if (creatures == null)
                throw new ArgumentNullException(nameof(creatures));
            CreatureId[] copied = creatures.ToArray();
            if (copied.Any(creature => creature.IsEmpty))
                throw new ArgumentException(
                    "Selected creature IDs cannot be empty.",
                    nameof(creatures)
                );
            this.creatures = Array.AsReadOnly(copied);
            this.areaPlacement = areaPlacement;
            HasAreaPlacement = hasAreaPlacement;
        }

        /// <summary>Gets the shared selection for spells requiring no player-selected creatures.</summary>
        public static SpellCastSelection Empty { get; } =
            new SpellCastSelection(Array.Empty<CreatureId>());

        /// <summary>Gets player-selected creature IDs in their declared order.</summary>
        public IReadOnlyList<CreatureId> Creatures => creatures;

        /// <summary>Gets whether the selection contains an authored area placement.</summary>
        public bool HasAreaPlacement { get; }

        /// <summary>Gets the exact authored area placement.</summary>
        /// <exception cref="InvalidOperationException">This is not an area selection.</exception>
        public SpellAreaPlacement AreaPlacement =>
            HasAreaPlacement
                ? areaPlacement
                : throw new InvalidOperationException(
                    "This spell selection does not contain an area placement."
                );

        /// <inheritdoc/>
        public bool Equals(SpellCastSelection other) =>
            other != null
            && HasAreaPlacement == other.HasAreaPlacement
            && (!HasAreaPlacement || areaPlacement.Equals(other.areaPlacement))
            && creatures.SequenceEqual(other.creatures);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellCastSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int hash = HashCode.Combine(17, HasAreaPlacement, areaPlacement);
            foreach (CreatureId creature in creatures)
                hash = HashCode.Combine(hash, creature);
            return hash;
        }
    }

    /// <summary>Requests one generic, definition-backed Cast a Spell action.</summary>
    public sealed class CastSpellActionOp : ActionOp<CastSpellOutcome>, IReceiptedActionOp
    {
        /// <summary>Creates an immutable root request without caller-computed authorization.</summary>
        /// <param name="actor">The creature attempting the cast.</param>
        /// <param name="spell">The exact spell identity and requested rank.</param>
        /// <param name="variant">The definition-owned action-cost variant.</param>
        /// <param name="selection">Immutable player-selected creature IDs.</param>
        public CastSpellActionOp(
            ActionInvocationId invocationId,
            CreatureId actor,
            SpellReference spell,
            SpellActionVariant variant,
            SpellCastSelection selection
        )
            : base(actor, CastSpellActionDefinition.DefinitionId)
        {
            if (invocationId.IsEmpty)
                throw new ArgumentException(
                    "A cast invocation identity is required.",
                    nameof(invocationId)
                );
            InvocationId = invocationId;
            Spell = spell;
            Variant = variant;
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        }

        /// <summary>Gets the stable identity used to recover an exact committed retry.</summary>
        public ActionInvocationId InvocationId { get; }

        /// <summary>Gets the exact spell and rank requested by the caster.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the selected definition-owned action variant.</summary>
        public SpellActionVariant Variant { get; }

        /// <summary>Gets immutable player-selected target identities.</summary>
        public SpellCastSelection Selection { get; }

        bool IReceiptedActionOp.HasSameIntent(IReceiptedActionOp other) =>
            other is CastSpellActionOp cast
            && Actor == cast.Actor
            && Spell.Equals(cast.Spell)
            && Variant.Equals(cast.Variant)
            && Selection.Equals(cast.Selection);

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
            ActionInvocationId invocationId,
            CreatureId actor,
            SpellReference spell,
            SpellActionVariant variant,
            SpellCastSelection selection
        ) => new CastSpellActionOp(invocationId, actor, spell, variant, selection);
    }

    /// <summary>Reports the final effects, saves, or attack committed by one resolved spell cast.</summary>
    public sealed class CastSpellOutcome
    {
        private readonly IReadOnlyList<SpellSaveResolution> saves;
        private readonly IReadOnlyList<SpellAttackResolution> attacks;

        /// <summary>Creates the structural result of a resolved generic spell cast.</summary>
        /// <param name="actor">The caster that resolved the action.</param>
        /// <param name="spell">The exact spell identity and cast rank.</param>
        /// <param name="createdEffects">All active effects committed by the cast.</param>
        /// <param name="saves">All per-target save resolutions committed by the cast.</param>
        /// <param name="attacks">The optional single attack resolution committed by the cast.</param>
        public CastSpellOutcome(
            CreatureId actor,
            SpellReference spell,
            IEnumerable<ActiveEffectId> createdEffects,
            IEnumerable<SpellSaveResolution> saves,
            IEnumerable<SpellAttackResolution> attacks
        )
        {
            Actor = actor;
            Spell = spell;
            CreatedEffects = new ReadOnlyCollection<ActiveEffectId>(
                (
                    createdEffects ?? throw new ArgumentNullException(nameof(createdEffects))
                ).ToArray()
            );
            this.saves = new ReadOnlyCollection<SpellSaveResolution>(
                (saves ?? throw new ArgumentNullException(nameof(saves))).ToArray()
            );
            if (this.saves.Any(save => save == null))
                throw new ArgumentException("Save resolutions cannot contain null.", nameof(saves));
            this.attacks = new ReadOnlyCollection<SpellAttackResolution>(
                (attacks ?? throw new ArgumentNullException(nameof(attacks))).ToArray()
            );
            if (this.attacks.Any(attack => attack == null) || this.attacks.Count > 1)
                throw new ArgumentException(
                    "A cast outcome can contain at most one non-null spell attack.",
                    nameof(attacks)
                );
        }

        /// <summary>Gets the creature that cast the spell.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the exact spell and rank that resolved.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the active effects created by the cast.</summary>
        public IReadOnlyList<ActiveEffectId> CreatedEffects { get; }

        /// <summary>Gets per-target saving-throw, damage, and secondary-effect results.</summary>
        public IReadOnlyList<SpellSaveResolution> Saves => saves;

        /// <summary>Gets the final spell-attack resolution carried to root presentation.</summary>
        public IReadOnlyList<SpellAttackResolution> Attacks => attacks;
    }

    /// <summary>Reports one definition-owned secondary condition application.</summary>
    public sealed class SpellConditionResolution
    {
        /// <summary>Creates a condition result from the authoritative application outcome.</summary>
        public SpellConditionResolution(
            RuleDefinitionId definitionId,
            ConditionApplicationOutcome application
        )
        {
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A secondary condition definition is required.",
                    nameof(definitionId)
                );
            DefinitionId = definitionId;
            Application = application ?? throw new ArgumentNullException(nameof(application));
        }

        /// <summary>Gets the canonical condition definition.</summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>Gets whether the condition was applied or legally blocked.</summary>
        public ConditionApplicationOutcome Application { get; }
    }

    /// <summary>Reports one selected target's resolved save, final damage, and conditions.</summary>
    public sealed class SpellSaveResolution
    {
        private readonly IReadOnlyList<TypedDamagePart> damage;
        private readonly IReadOnlyList<SpellConditionResolution> conditions;

        /// <summary>Creates one complete per-target save result.</summary>
        public SpellSaveResolution(
            CreatureId target,
            CheckOutcome check,
            IEnumerable<TypedDamagePart> damage,
            DamageOutcome damageOutcome,
            IEnumerable<SpellConditionResolution> conditions
        )
        {
            if (target.IsEmpty)
                throw new ArgumentException("A spell-save target is required.", nameof(target));
            Target = target;
            Check = check ?? throw new ArgumentNullException(nameof(check));
            this.damage = new ReadOnlyCollection<TypedDamagePart>(
                (damage ?? throw new ArgumentNullException(nameof(damage))).ToArray()
            );
            this.conditions = new ReadOnlyCollection<SpellConditionResolution>(
                (conditions ?? throw new ArgumentNullException(nameof(conditions))).ToArray()
            );
            if (damageOutcome.Requested != this.damage.Sum(part => part.Amount))
                throw new ArgumentException(
                    "The committed damage request must match the typed damage parts.",
                    nameof(damageOutcome)
                );
            DamageOutcome = damageOutcome;
        }

        /// <summary>Gets the creature that attempted the save.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the authoritative saving-throw outcome.</summary>
        public CheckOutcome Check { get; }

        /// <summary>Gets typed requested damage parts after basic-save scaling and defenses.</summary>
        public IReadOnlyList<TypedDamagePart> Damage => damage;

        /// <summary>Gets requested typed damage before current-HP and temporary-HP clamping.</summary>
        public int RequestedDamage => DamageOutcome.Requested;

        /// <summary>Gets the exact authoritative health commitment.</summary>
        public DamageOutcome DamageOutcome { get; }

        /// <summary>Gets damage actually applied to current HP and temporary HP.</summary>
        public int FinalDamage => DamageOutcome.Applied;

        /// <summary>Gets condition applications selected by the exact save degree.</summary>
        public IReadOnlyList<SpellConditionResolution> Conditions => conditions;
    }

    /// <summary>Registers generic spell validation and active-effect creation.</summary>
    public static class SpellcastingRuleDispatcherExtensions
    {
        /// <summary>Adds generic Cast a Spell validation and resolution to a dispatcher.</summary>
        /// <param name="builder">The dispatcher composition being configured.</param>
        /// <param name="catalog">Encounter spell definitions and prepared spellbooks.</param>
        /// <param name="registry">The encounter's immutable active-rule definition authority.</param>
        /// <returns>The same builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseSpellcastingRules(
            this RuleDispatcherBuilder builder,
            ISpellActionCatalog catalog,
            RuleRegistry registry
        ) =>
            UseSpellcastingRules(
                builder,
                catalog,
                registry,
                UnsupportedSpellAttackResolutionDataProvider.Instance,
                UnsupportedSpellSaveTargetingProvider.Instance
            );

        /// <summary>Adds Cast a Spell with an explicit spell-attack Unity or test adapter.</summary>
        /// <param name="builder">The dispatcher composition being configured.</param>
        /// <param name="catalog">Encounter spell definitions and prepared spellbooks.</param>
        /// <param name="registry">The encounter's immutable active-rule definition authority.</param>
        /// <param name="resolutionData">Current target and spell-attack resolution data.</param>
        /// <returns>The same builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseSpellcastingRules(
            this RuleDispatcherBuilder builder,
            ISpellActionCatalog catalog,
            RuleRegistry registry,
            ISpellAttackResolutionDataProvider resolutionData
        ) =>
            UseSpellcastingRules(
                builder,
                catalog,
                registry,
                resolutionData,
                resolutionData as ISpellSaveTargetingProvider
                    ?? UnsupportedSpellSaveTargetingProvider.Instance
            );

        /// <summary>Adds Cast a Spell with explicit attack and area-save targeting providers.</summary>
        public static RuleDispatcherBuilder UseSpellcastingRules(
            this RuleDispatcherBuilder builder,
            ISpellActionCatalog catalog,
            RuleRegistry registry,
            ISpellAttackResolutionDataProvider resolutionData,
            ISpellSaveTargetingProvider saveTargeting
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (resolutionData == null)
                throw new ArgumentNullException(nameof(resolutionData));
            if (saveTargeting == null)
                throw new ArgumentNullException(nameof(saveTargeting));
            CastSpellActionDefinition definition = new CastSpellActionDefinition(catalog);
            return builder
                .RegisterActionValidator(
                    new CastSpellActionValidator(
                        definition,
                        catalog,
                        registry,
                        resolutionData,
                        saveTargeting
                    )
                )
                .RegisterHandler<CastSpellActionOp, CastSpellOutcome>(
                    new CastSpellActionHandler(catalog)
                )
                .RegisterHandler<ResolveSpellAttackOp, SpellAttackResolution>(
                    new ResolveSpellAttackHandler(catalog, resolutionData),
                    InvocationPolicy.NestedOnly
                )
                .RegisterReducer<CommitPreparedSpellCastOp, CastSpellOutcome>(
                    new CommitPreparedSpellCastReducer(registry),
                    RuleSource.FromSlug("cast-spell"),
                    InvocationPolicy.NestedOnly
                );
        }
    }

    internal sealed class CastSpellActionValidator : IActionValidator<CastSpellActionOp>
    {
        private readonly CastSpellActionDefinition definition;
        private readonly ISpellDefinitionCatalog catalog;
        private readonly RuleRegistry registry;
        private readonly ISpellAttackResolutionDataProvider resolutionData;
        private readonly ISpellSaveTargetingProvider saveTargeting;

        public CastSpellActionValidator(
            CastSpellActionDefinition definition,
            ISpellDefinitionCatalog catalog,
            RuleRegistry registry,
            ISpellAttackResolutionDataProvider resolutionData,
            ISpellSaveTargetingProvider saveTargeting
        )
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.resolutionData =
                resolutionData ?? throw new ArgumentNullException(nameof(resolutionData));
            this.saveTargeting =
                saveTargeting ?? throw new ArgumentNullException(nameof(saveTargeting));
        }

        public ActionValidationResult Validate(
            OpFrame<CastSpellActionOp> frame,
            RulesSnapshot snapshot
        )
        {
            if (!snapshot.Statistics.Contains(frame.Op.Actor))
                return ActionValidationResult.Invalid(
                    "The caster has no authoritative statistics state."
                );
            ActionValidationResult common = definition.Validate(snapshot, frame.Op);
            if (common is not ActionValidationResult.ValidActionValidationResult)
                return common;
            if (!catalog.TryGetSpell(frame.Op.Spell, out SpellDefinition spell))
                return ActionValidationResult.Invalid("The spell reference is unknown.");
            foreach (RuleDefinitionId effect in spell.Effects.Select(value => value.DefinitionId))
            {
                if (!registry.ContainsDefinition(effect))
                    throw new InvalidOperationException(
                        $"Spell effect definition {effect.Value} is absent from the encounter registry."
                    );
            }
            if (spell.Saves.Count > 0)
            {
                foreach (
                    RuleDefinitionId condition in spell
                        .Saves.SelectMany(save => save.Conditions)
                        .Select(directive => directive.DefinitionId)
                        .Distinct()
                )
                {
                    if (!registry.ContainsDefinition(condition))
                        throw new InvalidOperationException(
                            $"Spell condition definition {condition.Value} is absent from the encounter registry."
                        );
                }
                if (spell.Saves.Count != 1)
                    return ActionValidationResult.Invalid(
                        "The spell save target structure is unsupported."
                    );
                if (!frame.Op.Selection.HasAreaPlacement)
                    return ActionValidationResult.Invalid(
                        "The spell save requires an authoritative area placement."
                    );
                if (
                    frame.Op.Selection.Creatures.Distinct().Count()
                    != frame.Op.Selection.Creatures.Count
                )
                    return ActionValidationResult.Invalid(
                        "A spell save selection cannot contain duplicate creatures."
                    );
                foreach (CreatureId saveTarget in frame.Op.Selection.Creatures)
                {
                    if (!snapshot.Creatures.Contains(saveTarget))
                        return ActionValidationResult.Invalid(
                            "A selected spell-save creature is not registered."
                        );
                    if (
                        !snapshot.Health.TryGet(saveTarget, out HealthState saveHealth)
                        || saveHealth.Current == 0
                    )
                        return ActionValidationResult.Invalid(
                            "A selected spell-save creature is not alive."
                        );
                    if (!snapshot.PreparedInputs.Contains(saveTarget))
                        return ActionValidationResult.Invalid(
                            "A selected spell-save creature has no prepared inputs."
                        );
                    if (!snapshot.Statistics.Contains(saveTarget))
                        return ActionValidationResult.Invalid(
                            "A selected spell-save creature has no statistics state."
                        );
                }
                return saveTargeting.Validate(
                    snapshot,
                    frame.Op.Actor,
                    spell.Saves[0],
                    frame.Op.Selection.AreaPlacement,
                    frame.Op.Selection.Creatures
                );
            }
            if (spell.Effects.Count > 0)
            {
                if (frame.Op.Selection.HasAreaPlacement)
                    return ActionValidationResult.Invalid(
                        "A definition-owned self target cannot carry an area placement."
                    );
                return frame.Op.Selection.Creatures.Count == 0
                    ? ActionValidationResult.Valid
                    : ActionValidationResult.Invalid(
                        "A definition-owned self target cannot carry selected creatures."
                    );
            }
            if (frame.Op.Selection.HasAreaPlacement)
                return ActionValidationResult.Invalid(
                    "A spell attack cannot carry an area placement."
                );
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

    internal sealed class UnsupportedSpellSaveTargetingProvider : ISpellSaveTargetingProvider
    {
        internal static UnsupportedSpellSaveTargetingProvider Instance { get; } = new();

        private UnsupportedSpellSaveTargetingProvider() { }

        public ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellSaveDefinition save,
            SpellAreaPlacement placement,
            IReadOnlyList<CreatureId> selectedCreatures
        ) =>
            ActionValidationResult.Invalid(
                "Authoritative area targeting is unavailable for this spell cast."
            );
    }

    internal abstract class PreparedSpellCast
    {
        protected PreparedSpellCast(CastSpellActionOp operation) =>
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));

        internal CastSpellActionOp Operation { get; }
    }

    internal sealed class PreparedSpellAttackCast : PreparedSpellCast
    {
        internal PreparedSpellAttackCast(
            CastSpellActionOp operation,
            SpellAttackResolution resolution
        )
            : base(operation)
        {
            Resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
            if (
                resolution.Actor != operation.Actor
                || resolution.Spell != operation.Spell
                || operation.Selection.Creatures.Count != 1
                || resolution.Target != operation.Selection.Creatures[0]
            )
                throw new ArgumentException(
                    "The spell-attack resolution must match the exact cast intent.",
                    nameof(resolution)
                );
        }

        internal SpellAttackResolution Resolution { get; }
    }

    internal sealed class PreparedSpellEffectCast : PreparedSpellCast
    {
        private readonly IReadOnlyList<ActiveEffectRegistration> registrations;

        internal PreparedSpellEffectCast(
            CastSpellActionOp operation,
            IEnumerable<ActiveEffectRegistration> registrations
        )
            : base(operation)
        {
            this.registrations = Array.AsReadOnly(
                (registrations ?? throw new ArgumentNullException(nameof(registrations))).ToArray()
            );
        }

        internal IReadOnlyList<ActiveEffectRegistration> Registrations => registrations;
    }

    internal sealed class PreparedSpellSaveTarget
    {
        private readonly IReadOnlyList<ApplyConditionOp> conditionOperations;

        internal PreparedSpellSaveTarget(
            CreatureId target,
            CheckOutcome check,
            IReadOnlyList<TypedDamagePart> damage,
            int finalDamage,
            HealthChangeOriginId origin,
            RuleSource source,
            IEnumerable<ApplyConditionOp> conditionOperations
        )
        {
            if (target.IsEmpty)
                throw new ArgumentException("A spell-save target is required.", nameof(target));
            if (finalDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(finalDamage));
            if (origin.IsEmpty)
                throw new ArgumentException("A damage origin is required.", nameof(origin));
            if (source.IsEmpty)
                throw new ArgumentException("A rules source is required.", nameof(source));
            Target = target;
            Check = check ?? throw new ArgumentNullException(nameof(check));
            Damage = damage ?? throw new ArgumentNullException(nameof(damage));
            ApplyConditionOp[] copiedOperations =
                conditionOperations?.ToArray()
                ?? throw new ArgumentNullException(nameof(conditionOperations));
            if (
                copiedOperations.Any(operation =>
                    operation == null || operation.Target != target || operation.Source != source
                )
            )
                throw new ArgumentException(
                    "Prepared spell-save conditions must share the target and source.",
                    nameof(conditionOperations)
                );
            FinalDamage = finalDamage;
            Origin = origin;
            this.conditionOperations = Array.AsReadOnly(copiedOperations);
        }

        internal CreatureId Target { get; }
        internal CheckOutcome Check { get; }
        internal IReadOnlyList<TypedDamagePart> Damage { get; }
        internal int FinalDamage { get; }
        internal HealthChangeOriginId Origin { get; }
        internal IReadOnlyList<ApplyConditionOp> ConditionOperations => conditionOperations;
    }

    internal sealed class PreparedSpellSaveCast : PreparedSpellCast
    {
        private readonly IReadOnlyList<PreparedSpellSaveTarget> targets;

        internal PreparedSpellSaveCast(
            CastSpellActionOp operation,
            IEnumerable<PreparedSpellSaveTarget> targets
        )
            : base(operation)
        {
            this.targets = Array.AsReadOnly(
                (targets ?? throw new ArgumentNullException(nameof(targets))).ToArray()
            );
        }

        internal IReadOnlyList<PreparedSpellSaveTarget> Targets => targets;
    }

    internal sealed class CommitPreparedSpellCastOp : IRuleOp<CastSpellOutcome>, IRuleSourcedOp
    {
        internal CommitPreparedSpellCastOp(PreparedSpellCast prepared) =>
            Prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));

        internal PreparedSpellCast Prepared { get; }
        public RuleSource Source => RuleSource.FromSlug(Prepared.Operation.Spell.Spell.Value);
    }

    internal sealed class CommitPreparedSpellCastReducer
        : IOpReducer<CommitPreparedSpellCastOp, CastSpellOutcome>
    {
        private readonly RuleRegistry registry;

        internal CommitPreparedSpellCastReducer(RuleRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        public ReductionResult<CastSpellOutcome> Reduce(
            ReductionContext<CommitPreparedSpellCastOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            CastSpellActionOp operation = context.Op.Prepared.Operation;
            if (!ActionReceiptReduction.TryGetExactPending(state, operation, out _))
                return ReductionResult<CastSpellOutcome>.Reject(
                    ActionReceiptReduction.NotPendingReason
                );

            ReductionResult<CastSpellOutcome> prepared = context.Op.Prepared switch
            {
                PreparedSpellAttackCast attack => CommitAttack(context, state, facts, attack),
                PreparedSpellEffectCast effects => CommitEffects(state, facts, effects),
                PreparedSpellSaveCast saves => CommitSaves(context, state, facts, saves),
                _ => throw new InvalidOperationException(
                    "The prepared spell cast has an unknown resolution variant."
                ),
            };
            if (prepared.IsRejected)
                return prepared;
            CastSpellOutcome outcome = prepared.Value;
            if (!ActionReceiptReduction.TryResolve(state, facts, operation, outcome))
                return ReductionResult<CastSpellOutcome>.Reject(
                    ActionReceiptReduction.NotPendingReason
                );
            return ReductionResult<CastSpellOutcome>.Accept(outcome);
        }

        private static ReductionResult<CastSpellOutcome> CommitAttack(
            ReductionContext<CommitPreparedSpellCastOp> context,
            RulesStateDraft state,
            FactSink facts,
            PreparedSpellAttackCast prepared
        )
        {
            ReductionResult<DamageOutcome> damage = DamageReduction.Commit(
                state,
                facts,
                prepared.Resolution.Target,
                prepared.Resolution.FinalDamage,
                new HealthChangeOriginId($"spell-{context.RootOpId.Value}")
            );
            if (damage.IsRejected)
                return ReductionResult<CastSpellOutcome>.Reject(damage.RejectionReason);
            ReductionResult<MultipleAttackPenaltyState> map =
                MultipleAttackPenaltyReduction.Advance(state, facts, prepared.Operation.Actor);
            if (map.IsRejected)
                return ReductionResult<CastSpellOutcome>.Reject(map.RejectionReason);
            return ReductionResult<CastSpellOutcome>.Accept(
                new CastSpellOutcome(
                    prepared.Operation.Actor,
                    prepared.Operation.Spell,
                    Array.Empty<ActiveEffectId>(),
                    Array.Empty<SpellSaveResolution>(),
                    new[] { prepared.Resolution }
                )
            );
        }

        private ReductionResult<CastSpellOutcome> CommitEffects(
            RulesStateDraft state,
            FactSink facts,
            PreparedSpellEffectCast prepared
        )
        {
            List<ActiveEffectId> created = new();
            foreach (ActiveEffectRegistration registration in prepared.Registrations)
            {
                if (
                    !ActiveEffectCreationReduction.TryCreate(
                        registry,
                        state,
                        facts,
                        registration.Effect,
                        registration.Binding,
                        out ActiveEffectCreationOutcome outcome,
                        out _,
                        out string rejection
                    )
                )
                    return ReductionResult<CastSpellOutcome>.Reject(rejection);
                created.Add(outcome.EffectId);
            }
            return ReductionResult<CastSpellOutcome>.Accept(
                new CastSpellOutcome(
                    prepared.Operation.Actor,
                    prepared.Operation.Spell,
                    created,
                    Array.Empty<SpellSaveResolution>(),
                    Array.Empty<SpellAttackResolution>()
                )
            );
        }

        private ReductionResult<CastSpellOutcome> CommitSaves(
            ReductionContext<CommitPreparedSpellCastOp> context,
            RulesStateDraft state,
            FactSink facts,
            PreparedSpellSaveCast prepared
        )
        {
            List<ActiveEffectId> created = new();
            List<SpellSaveResolution> saves = new();
            foreach (PreparedSpellSaveTarget target in prepared.Targets)
            {
                ReductionResult<DamageOutcome> damage = DamageReduction.Commit(
                    state,
                    facts,
                    target.Target,
                    target.FinalDamage,
                    target.Origin
                );
                if (damage.IsRejected)
                    return ReductionResult<CastSpellOutcome>.Reject(damage.RejectionReason);

                List<SpellConditionResolution> conditions = new();
                foreach (ApplyConditionOp conditionOperation in target.ConditionOperations)
                {
                    if (
                        !ConditionApplicationReduction.TryApply(
                            registry,
                            state,
                            facts,
                            context.SourceOpId,
                            conditionOperation,
                            out ConditionApplicationOutcome application,
                            out string rejection
                        )
                    )
                        return ReductionResult<CastSpellOutcome>.Reject(rejection);
                    conditions.Add(
                        new SpellConditionResolution(conditionOperation.DefinitionId, application)
                    );
                    if (application.Status == ConditionApplicationStatus.Applied)
                        created.Add(application.EffectId);
                }
                saves.Add(
                    new SpellSaveResolution(
                        target.Target,
                        target.Check,
                        target.Damage,
                        damage.Value,
                        conditions
                    )
                );
            }

            CastSpellOutcome outcome = new CastSpellOutcome(
                prepared.Operation.Actor,
                prepared.Operation.Spell,
                created,
                saves,
                Array.Empty<SpellAttackResolution>()
            );
            return ReductionResult<CastSpellOutcome>.Accept(outcome);
        }
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

            List<ActiveEffectRegistration> preparedEffects = new();
            for (int index = 0; index < definition.Effects.Count; index++)
            {
                SpellEffectDirective directive = definition.Effects[index];
                CreatureId target = ResolveTarget(directive, frame.Op);
                string instanceKey = $"{frame.Op.InvocationId.Value}-{index}";
                ActiveEffectId effectId = new ActiveEffectId($"spell-effect-{instanceKey}");
                BindingId bindingId = new BindingId($"spell-binding-{instanceKey}");
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
                    index
                );
                preparedEffects.Add(new ActiveEffectRegistration(effect, binding));
            }
            if (preparedEffects.Count > 0)
                return await CommitPreparedSpellCast(
                    context,
                    new PreparedSpellEffectCast(frame.Op, preparedEffects)
                );
            List<PreparedSpellSaveTarget> preparedSaves = new();
            for (int saveIndex = 0; saveIndex < definition.Saves.Count; saveIndex++)
            {
                SpellSaveDefinition save = definition.Saves[saveIndex];
                List<CheckOutcome> checks = new();
                foreach (CreatureId target in frame.Op.Selection.Creatures)
                {
                    OpResult<CheckOutcome> checkedSave = await context.Dispatch(
                        new SavingThrowOp(
                            target,
                            save.Save,
                            catalog.GetSpellBook(frame.Op.Actor).SpellDc,
                            CheckSource.From(frame.Id)
                        )
                    );
                    if (checkedSave is not ResolvedOpResult<CheckOutcome> resolvedSave)
                        throw new InvalidOperationException("Spell saving throw did not resolve.");
                    checks.Add(resolvedSave.Value);
                }
                IReadOnlyList<TypedDamagePart> rolledDamage = TypedDamageResolver.Roll(
                    save.Damage,
                    Array.Empty<TypedFlatDamage>(),
                    context.Rolls
                );
                for (
                    int targetIndex = 0;
                    targetIndex < frame.Op.Selection.Creatures.Count;
                    targetIndex++
                )
                {
                    CreatureId target = frame.Op.Selection.Creatures[targetIndex];
                    PreparedSpellSaveTarget prepared = PrepareSave(
                        frame,
                        context.Snapshot,
                        save,
                        saveIndex,
                        targetIndex,
                        target,
                        checks[targetIndex],
                        rolledDamage
                    );
                    preparedSaves.Add(prepared);
                }
            }
            if (preparedSaves.Count > 0)
                return await CommitPreparedSpellCast(
                    context,
                    new PreparedSpellSaveCast(frame.Op, preparedSaves)
                );
            if (definition.Attacks.Count > 0)
            {
                CreatureId target = frame.Op.Selection.Creatures.Single();
                OpResult<SpellAttackResolution> result = await context.Dispatch(
                    new ResolveSpellAttackOp(frame.Op.Actor, frame.Op.Spell, target)
                );
                if (result is not ResolvedOpResult<SpellAttackResolution> resolved)
                    throw new InvalidOperationException("Spell attack resolution did not resolve.");
                return await CommitPreparedSpellCast(
                    context,
                    new PreparedSpellAttackCast(frame.Op, resolved.Value)
                );
            }
            throw new InvalidOperationException(
                "A validated spell definition has no supported resolution category."
            );
        }

        private static async ValueTask<CastSpellOutcome> CommitPreparedSpellCast(
            OpHandlerContext context,
            PreparedSpellCast prepared
        )
        {
            OpResult<CastSpellOutcome> committed = await context.Dispatch(
                new CommitPreparedSpellCastOp(prepared)
            );
            return committed is ResolvedOpResult<CastSpellOutcome> resolved
                ? resolved.Value
                : throw new InvalidOperationException(
                    committed is InvalidOpResult<CastSpellOutcome> invalid
                        ? invalid.Reason
                        : "Prepared spell cast commitment did not resolve."
                );
        }

        private PreparedSpellSaveTarget PrepareSave(
            OpFrame<CastSpellActionOp> frame,
            RulesSnapshot snapshot,
            SpellSaveDefinition save,
            int saveIndex,
            int targetIndex,
            CreatureId target,
            CheckOutcome check,
            IReadOnlyList<TypedDamagePart> rolledDamage
        )
        {
            if (!snapshot.PreparedInputs.TryGet(target, out PreparedCreatureInputs inputs))
                throw new InvalidOperationException(
                    "A validated spell-save target lost its prepared inputs."
                );
            IReadOnlyList<TypedDamagePart> damage = TypedDamageResolver.ResolveBasicSave(
                rolledDamage,
                check.Degree,
                inputs
                    .Immunities.Where(value => value.Kind == PreparedImmunityKind.Damage)
                    .Select(value => new TypedDamageImmunity(value.Type)),
                inputs.Weaknesses.Select(value => new TypedDefenseAdjustment(
                    value.Type,
                    value.Value
                )),
                inputs.Resistances.Select(value => new TypedDefenseAdjustment(
                    value.Type,
                    value.Value
                ))
            );
            int finalDamage = damage.Sum(part => part.Amount);
            RuleSource source = RuleSource.FromSlug(frame.Op.Spell.Spell.Value);
            SpellSaveConditionDirective[] applicableConditions = save
                .Conditions.Where(value => value.Degree == check.Degree)
                .ToArray();
            List<ApplyConditionOp> conditionOperations = new();
            foreach (SpellSaveConditionDirective directive in applicableConditions)
            {
                if (
                    !ConditionRuleDefinitions.TryGetCanonicalSlug(
                        directive.DefinitionId,
                        out string condition
                    )
                )
                    throw new InvalidOperationException(
                        "A spell save condition lost its canonical definition."
                    );
                conditionOperations.Add(
                    new ApplyConditionOp(
                        condition,
                        target,
                        frame.Op.Actor,
                        source,
                        directive.Duration,
                        directive.State
                    )
                );
            }

            return new PreparedSpellSaveTarget(
                target,
                check,
                damage,
                finalDamage,
                new HealthChangeOriginId(
                    $"spell-{frame.RootId.Value}-save-{saveIndex}-target-{targetIndex}"
                ),
                source,
                conditionOperations
            );
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
