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

        /// <summary>Creates an immutable selection from stable creature identities.</summary>
        /// <param name="creatures">Selected creature IDs in player-declared order.</param>
        public SpellCastSelection(IEnumerable<CreatureId> creatures)
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
        }

        /// <summary>Gets the shared selection for spells requiring no player-selected creatures.</summary>
        public static SpellCastSelection Empty { get; } =
            new SpellCastSelection(Array.Empty<CreatureId>());

        /// <summary>Gets player-selected creature IDs in their declared order.</summary>
        public IReadOnlyList<CreatureId> Creatures => creatures;

        /// <inheritdoc/>
        public bool Equals(SpellCastSelection other) =>
            other != null && creatures.SequenceEqual(other.creatures);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellCastSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int hash = 17;
            foreach (CreatureId creature in creatures)
                hash = HashCode.Combine(hash, creature);
            return hash;
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

    /// <summary>Reports one resolved spell cast and every active effect it created.</summary>
    public sealed class CastSpellOutcome
    {
        /// <summary>Creates the structural result of a resolved generic spell cast.</summary>
        /// <param name="actor">The caster that resolved the action.</param>
        /// <param name="spell">The exact spell identity and cast rank.</param>
        /// <param name="createdEffects">All active effects committed by the cast.</param>
        public CastSpellOutcome(
            CreatureId actor,
            SpellReference spell,
            IEnumerable<ActiveEffectId> createdEffects
        )
        {
            Actor = actor;
            Spell = spell;
            CreatedEffects = new ReadOnlyCollection<ActiveEffectId>(
                (
                    createdEffects ?? throw new ArgumentNullException(nameof(createdEffects))
                ).ToArray()
            );
        }

        /// <summary>Gets the creature that cast the spell.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the exact spell and rank that resolved.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the active effects created by the cast.</summary>
        public IReadOnlyList<ActiveEffectId> CreatedEffects { get; }
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
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            CastSpellActionDefinition definition = new CastSpellActionDefinition(catalog);
            return builder
                .RegisterActionValidator(new CastSpellActionValidator(definition))
                .RegisterHandler<CastSpellActionOp, CastSpellOutcome>(
                    new CastSpellActionHandler(catalog)
                );
        }
    }

    internal sealed class CastSpellActionValidator : IActionValidator<CastSpellActionOp>
    {
        private readonly CastSpellActionDefinition definition;

        public CastSpellActionValidator(CastSpellActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        public ActionValidationResult Validate(
            OpFrame<CastSpellActionOp> frame,
            RulesSnapshot snapshot
        ) => definition.Validate(snapshot, frame.Op);
    }

    internal sealed class CastSpellActionHandler : IOpHandler<CastSpellActionOp, CastSpellOutcome>
    {
        private readonly ISpellDefinitionCatalog catalog;

        public CastSpellActionHandler(ISpellDefinitionCatalog catalog) =>
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        public async ValueTask<CastSpellOutcome> Handle(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context
        )
        {
            if (!catalog.TryGetSpell(frame.Op.Spell, out SpellDefinition definition))
                throw new InvalidOperationException("A validated spell definition disappeared.");

            List<ActiveEffectId> created = new();
            for (int index = 0; index < definition.Effects.Count; index++)
            {
                SpellEffectDirective directive = definition.Effects[index];
                CreatureId target = ResolveTarget(directive, frame.Op);
                string instanceKey = $"{frame.Id.Value}-{index}";
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
            return new CastSpellOutcome(frame.Op.Actor, frame.Op.Spell, created);
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
