using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity.Light;
using Game.Rules.Unity.Spells;
using Game.Rules.Unity.Strike;
using GridPrivate;

namespace Game.Rules.Unity.Composition
{
    /// <summary>Builds the explicit production module list and its shared typed catalogs.</summary>
    internal sealed class UnityEncounterModuleSet
    {
        private UnityEncounterModuleSet(
            UnityEncounterComposition composition,
            CombatActionCatalog actionCatalog,
            RuleRegistry registry
        )
        {
            Composition = composition;
            ActionCatalog = actionCatalog;
            Registry = registry;
        }

        internal UnityEncounterComposition Composition { get; }
        internal CombatActionCatalog ActionCatalog { get; }
        internal RuleRegistry Registry { get; }

        internal static UnityEncounterModuleSet Create(
            UnityCombatRulesBridge owner,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            IReadOnlyDictionary<CreatureId, ActionController> controllers,
            Tile[,] tiles,
            StrideActionDefinition strideDefinition,
            bool installUnityAuthority
        )
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            UnityStrikeContext strikeContext = new(creatures, tiles);
            UnitySpellAttackContext spellAttackContext = new(creatures, tiles);
            UnitySpellDefinitionCatalog spellCatalog = UnitySpellDefinitionCatalog.Load();
            RageActionDefinition rageDefinition = new(
                new UnityRageConditionStateProvider(creatures)
            );
            CombatActionCatalog actionCatalog = new(
                strideDefinition,
                strikeContext,
                spellCatalog,
                new UnitySpellBookProvider(creatures),
                rageDefinition
            );

            RuleRegistryBuilder registryBuilder = new();
            foreach (
                PreparedRuleDefinitionSpec specification in Pf2eCharacterPreparer.CompileDefinitionSpecs(
                    Pf2eItemCatalog.Instance
                )
            )
                registryBuilder.Define(specification);
            RageRules.DefineRuleBindings(registryBuilder);
            registryBuilder.AddOutcomeRule();
            registryBuilder.Define(
                UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId
            );
            foreach (
                RuleDefinitionId definitionId in spellCatalog
                    .Definitions.SelectMany(definition => definition.Effects)
                    .Select(effect => effect.DefinitionId)
                    .Distinct()
            )
                registryBuilder.Define(definitionId);
            ConditionRuleDefinitions.DefineAll(registryBuilder);
            RuleRegistry registry = registryBuilder.Build();

            IUnityEncounterModule[] modules =
            {
                new UnityPreparedRulesEncounterModule(),
                new RottingAuraEncounterModule(owner),
                new ConditionEncounterModule(registry),
                new SlowedEncounterModule(),
                new UnityRageEncounterModule(rageDefinition),
                new UnityStrikeEncounterModule(
                    strikeContext,
                    controllers,
                    creatures,
                    installUnityAuthority
                ),
                new UnitySpellcastingEncounterModule(
                    owner,
                    actionCatalog,
                    spellAttackContext,
                    creatures,
                    installUnityAuthority
                ),
                new UnityLightEncounterModule(spellCatalog, creatures),
                new UnityHealthProjectionModule(creatures, installUnityAuthority),
                new UnityEncounterProjectionModule(owner),
            };
            return new UnityEncounterModuleSet(
                new UnityEncounterComposition(modules),
                actionCatalog,
                registry
            );
        }
    }

    /// <summary>Seeds compiled stateless prepared bindings through the shared enrollment path.</summary>
    internal sealed class UnityPreparedRulesEncounterModule : IUnityCombatantEnrollmentModule
    {
        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            PreparedRulePackage compilation = Pf2eCharacterPreparer.Compile(
                builder.Creature,
                builder.Creature.Build ?? new CharacterBuild()
            );
            builder.AddPreparedRules(
                compilation.Inputs,
                compilation.Bindings.Select(seed => seed.Create(builder.CreatureId))
            );
        }
    }

    /// <summary>Owns Rage's dispatcher and combatant-state composition.</summary>
    internal sealed class UnityRageEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly RageActionDefinition definition;

        internal UnityRageEncounterModule(RageActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseRageRules(definition);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            RageActorState state = CreateEnrollmentState(builder.PreparedInputs);
            builder.AddRuleBindings(RageRules.CreateInitialBindings(builder.CreatureId, state));
            builder.AddInstallation(
                new UnityRageActionInstallation(builder.Controller, definition, state.OwnsRage)
            );
        }

        private static RageActorState CreateEnrollmentState(PreparedCreatureInputs inputs)
        {
            bool HasOwned(string slug) =>
                inputs.BoundOptions.Any(option => option.Option == $"item:owned:{slug}");
            return new RageActorState(
                HasOwned("rage"),
                HasOwned("quick-tempered"),
                false,
                false,
                inputs.ArmorCategory == "heavy",
                HasOwned("invulnerable-rager"),
                inputs.Level,
                inputs.Abilities.Constitution
            );
        }

        private sealed class UnityRageActionInstallation : IUnityCombatantInstallationContribution
        {
            private readonly ActionController controller;
            private readonly IReadOnlyList<EntityAction> removals;
            private readonly IReadOnlyList<EntityAction> additions;

            internal UnityRageActionInstallation(
                ActionController controller,
                RageActionDefinition definition,
                bool ownsRage
            )
            {
                this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
                if (definition == null)
                    throw new ArgumentNullException(nameof(definition));
                removals = Array.AsReadOnly(
                    controller.GetActions().Where(action => action is RulesRageAction).ToArray()
                );
                additions = ownsRage
                    ? Array.AsReadOnly<EntityAction>(new[] { new RulesRageAction(definition) })
                    : Array.AsReadOnly(Array.Empty<EntityAction>());
            }

            public void Apply()
            {
                foreach (EntityAction action in removals)
                    controller.RemoveAction(action);
                foreach (EntityAction action in additions)
                    controller.AddAction(action);
            }
        }
    }

    /// <summary>Adapts the current Unity condition component into Rage's temporary condition seam.</summary>
    internal sealed class UnityRageConditionStateProvider : IRageConditionStateProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        internal UnityRageConditionStateProvider(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        ) => this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));

        /// <inheritdoc/>
        public RageConditionState Get(CreatureId actor)
        {
            if (!creatures.TryGetValue(actor, out CreatureComponent creature) || creature == null)
                throw new InvalidOperationException(
                    $"Rage actor {actor.Value} has no current Unity condition boundary."
                );
            Conditions conditions = creature.GetComponent<Conditions>();
            return new RageConditionState(
                HasCondition(conditions, "fatigued"),
                HasCondition(conditions, "encumbered")
            );
        }

        private static bool HasCondition(Conditions conditions, string expected) =>
            conditions != null
            && conditions.ActiveConditionNames.Any(value =>
                string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)
            );
    }

    /// <summary>Combines required typed action catalogs without teaching the bridge feature IDs.</summary>
    internal sealed class CombatActionCatalog
        : IActionCatalog,
            IStrikeActionCatalog,
            ISpellActionCatalog
    {
        private readonly StrideActionDefinition stride;
        private readonly IStrikeActionCatalog strike;
        private readonly ISpellDefinitionCatalog spell;
        private readonly ISpellBookProvider spellBooks;
        private readonly IReadOnlyList<IActionCatalog> featureCatalogs;

        internal CombatActionCatalog(
            StrideActionDefinition stride,
            IStrikeActionCatalog strike,
            ISpellDefinitionCatalog spell,
            ISpellBookProvider spellBooks,
            params IActionCatalog[] featureCatalogs
        )
        {
            this.stride = stride ?? throw new ArgumentNullException(nameof(stride));
            this.strike = strike ?? throw new ArgumentNullException(nameof(strike));
            this.spell = spell ?? throw new ArgumentNullException(nameof(spell));
            this.spellBooks = spellBooks ?? throw new ArgumentNullException(nameof(spellBooks));
            if (featureCatalogs == null || featureCatalogs.Any(catalog => catalog == null))
                throw new ArgumentException(
                    "Feature action catalogs cannot be null.",
                    nameof(featureCatalogs)
                );
            this.featureCatalogs = Array.AsReadOnly(featureCatalogs.ToArray());
        }

        /// <inheritdoc/>
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
        {
            if (definitionId == StrideActionDefinition.DefinitionId)
                return stride.GetBaseProfile(definitionId);
            if (definitionId == StrikeActionDefinition.DefinitionId)
                throw new InvalidOperationException(
                    "Strike profiles require the selected item on StrikeActionOp."
                );
            if (definitionId == ReloadActionDefinition.DefinitionId)
                throw new InvalidOperationException(
                    "Reload profiles require the selected item on ReloadActionOp."
                );
            foreach (IActionCatalog catalog in featureCatalogs)
            {
                try
                {
                    return catalog.GetBaseProfile(definitionId);
                }
                catch (KeyNotFoundException)
                {
                    // Each feature owns its definition IDs. Continue in explicit module order.
                }
            }
            throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
        }

        /// <inheritdoc/>
        public StrikeItemDefinition GetStrikeItem(ItemId item) => strike.GetStrikeItem(item);

        /// <inheritdoc/>
        public bool TryGetSpell(
            SpellReference reference,
            out Game.Rules.Runtime.SpellDefinition definition
        ) => spell.TryGetSpell(reference, out definition);

        /// <inheritdoc/>
        public ISpellBook GetSpellBook(CreatureId creature) => spellBooks.GetSpellBook(creature);
    }
}
