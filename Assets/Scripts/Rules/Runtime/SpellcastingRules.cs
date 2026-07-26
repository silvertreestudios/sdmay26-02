using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Requests one generic, definition-backed Cast a Spell action.</summary>
    public sealed class CastSpellActionOp : ActionOp<CastSpellOutcome>
    {
        /// <summary>Gets the shared action definition used by every spell cast.</summary>
        public static ActionDefinitionId CastDefinitionId { get; } =
            new ActionDefinitionId("cast-spell");

        /// <summary>Creates an immutable request to cast one exact spell variant.</summary>
        /// <param name="actor">The creature paying costs and creating effects.</param>
        /// <param name="spell">The exact spell identity and requested cast rank.</param>
        /// <param name="variant">The definition-supported action-cost variant.</param>
        /// <param name="authorization">The spellbook-authorized cantrip or slot resource.</param>
        public CastSpellActionOp(
            CreatureId actor,
            SpellReference spell,
            SpellActionVariant variant,
            SpellCastAuthorization authorization
        )
            : base(actor, CastDefinitionId)
        {
            if (!authorization.IsAuthorized)
                throw new ArgumentException(
                    "A cast operation requires an authorized resource.",
                    nameof(authorization)
                );
            Spell = spell;
            Variant = variant;
            Authorization = authorization;
        }

        /// <summary>Gets the exact spell and rank requested by the caster.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the selected action-cost variant.</summary>
        public SpellActionVariant Variant { get; }

        /// <summary>Gets the frozen spellbook resource authorization.</summary>
        public SpellCastAuthorization Authorization { get; }

        /// <inheritdoc/>
        public override ActionProfile GetBaseProfile(IActionCatalog catalog)
        {
            if (catalog is not ISpellActionCatalog spells)
                throw new InvalidOperationException(
                    "Cast a Spell requires an action catalog with spell definitions."
                );
            if (!spells.TryGetSpell(Spell, out SpellDefinition definition))
                return ActionProfile.Create(
                    ActionCost.FromActions(Variant.Actions),
                    Array.Empty<Trait>()
                );
            if (!definition.Variants.Contains(Variant))
                return ActionProfile.Create(
                    ActionCost.FromActions(Variant.Actions),
                    definition.Traits
                );
            RuleCost[] costs =
                Authorization.Kind == SpellCastResourceKind.SpellSlot
                    ? new RuleCost[] { RuleCost.SpellSlot(Authorization.Pool) }
                    : Array.Empty<RuleCost>();
            return new ActionProfile(
                ActionCost.FromActions(Variant.Actions),
                costs,
                definition.Traits
            );
        }
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
        /// <param name="catalog">The spell definitions and action profiles used by casts.</param>
        /// <param name="books">The encounter spellbooks used for authoritative authorization.</param>
        /// <returns>The same builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseSpellcastingRules(
            this RuleDispatcherBuilder builder,
            ISpellActionCatalog catalog,
            ISpellBookProvider books
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            return builder
                .RegisterActionValidator(new CastSpellActionValidator(catalog, books))
                .RegisterHandler<CastSpellActionOp, CastSpellOutcome>(
                    new CastSpellActionHandler(catalog)
                );
        }
    }

    internal sealed class CastSpellActionValidator : IActionValidator<CastSpellActionOp>
    {
        private readonly ISpellActionCatalog catalog;
        private readonly ISpellBookProvider books;

        public CastSpellActionValidator(ISpellActionCatalog catalog, ISpellBookProvider books)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.books = books ?? throw new ArgumentNullException(nameof(books));
        }

        public ActionValidationResult Validate(
            OpFrame<CastSpellActionOp> frame,
            RulesSnapshot snapshot
        )
        {
            if (!snapshot.Creatures.Contains(frame.Op.Actor))
                return ActionValidationResult.Invalid("The caster is not registered.");
            if (!catalog.TryGetSpell(frame.Op.Spell, out SpellDefinition definition))
                return ActionValidationResult.Invalid("The spell reference is unknown.");
            if (!definition.Variants.Contains(frame.Op.Variant))
                return ActionValidationResult.Invalid("The spell action variant is unavailable.");
            SpellCastAuthorization authorized = books
                .GetSpellBook(frame.Op.Actor)
                .Authorize(
                    frame.Op.Actor,
                    frame.Op.Spell,
                    new SnapshotSpellSlotStateReader(snapshot)
                );
            if (!authorized.IsAuthorized)
                return ActionValidationResult.Invalid(authorized.Reason);
            if (!authorized.Equals(frame.Op.Authorization))
                return ActionValidationResult.Invalid(
                    "The requested spell rank or slot pool is not authorized."
                );
            return ActionValidationResult.Valid;
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

            List<ActiveEffectId> created = new();
            for (int index = 0; index < definition.Effects.Count; index++)
            {
                SpellEffectDirective directive = definition.Effects[index];
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
                    new SpellEffectState(frame.Op.Spell, frame.Op.Actor)
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
    }
}
