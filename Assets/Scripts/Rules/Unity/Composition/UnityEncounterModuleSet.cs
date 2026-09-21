using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Actors;
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
            UnityActionPresentationCoordinator actionPresentationCoordinator,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            IReadOnlyDictionary<CreatureId, ActionController> controllers,
            Tile[,] tiles,
            StrideActionDefinition strideDefinition,
            ICombatantFriendshipProvider friendshipProvider,
            bool installUnityAuthority,
            IReadOnlyList<UnityEncounterExtension> extensions
        )
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (actionPresentationCoordinator == null)
                throw new ArgumentNullException(nameof(actionPresentationCoordinator));
            if (friendshipProvider == null)
                throw new ArgumentNullException(nameof(friendshipProvider));
            if (extensions == null || extensions.Any(extension => extension == null))
                throw new ArgumentException(
                    "Encounter extensions cannot contain null.",
                    nameof(extensions)
                );
            UnityStrikeContext strikeContext = new(creatures, tiles);
            UnitySpellAttackContext spellAttackContext = new(creatures, tiles, friendshipProvider);
            UnityRottingAuraModule rottingAura = new(creatures, tiles);
            UnitySpellDefinitionCatalog spellCatalog = UnitySpellDefinitionCatalog.Load();
            UnitySpellCreatureDataProvider spellCreatureData = new(creatures);
            RageActionDefinition rageDefinition = new(new UnityRageActorStateProvider(creatures));
            CombatActionCatalog actionCatalog = new(
                strideDefinition,
                strikeContext,
                spellCatalog,
                new UnitySpellBookProvider(creatures),
                SpellFeatureRules.CreateCatalog(spellCreatureData, friendshipProvider),
                new IActionCatalog[] { rageDefinition }.Concat(
                    extensions.Select(extension => extension.ActionCatalog)
                )
            );

            RuleRegistryBuilder registryBuilder = new();
            registryBuilder.Define(ConditionRules.DefinitionId).EffectState<ConditionState>();
            RottingAuraRules.DefineRuleBinding(registryBuilder, rottingAura);
            SlowedRules.DefineRuleBinding(registryBuilder);
            RageRules.DefineRuleBindings(registryBuilder);
            SpellFeatureRules.DefineRuleBindings(registryBuilder);
            registryBuilder.AddOutcomeRule();
            foreach (
                RuleDefinitionId definitionId in spellCatalog
                    .Definitions.SelectMany(definition => definition.Effects)
                    .Select(effect => effect.DefinitionId)
                    .Distinct()
            )
                registryBuilder.Define(definitionId).EffectState<SpellEffectState>();

            UnityActionPresentationRegistry actionPresentation = new(actionPresentationCoordinator);
            List<IUnityEncounterModule> modules = new()
            {
                rottingAura,
                new DungeonRulesEffectPersistenceModule(
                    owner,
                    DungeonRulesEffectPersistence.Codecs
                ),
                new ConditionEncounterModule(owner),
                new UnitySlowedModule(),
                new UnityRageModule(rageDefinition),
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
            };
            modules.AddRange(extensions.Select(extension => extension.Module));
            modules.Add(new UnityActionPresentationModule(actionPresentation));
            modules.Add(new UnityLightModule(spellCatalog, creatures));
            modules.Add(
                new UnityHealthProjectionModule(
                    creatures,
                    actionPresentationCoordinator,
                    installUnityAuthority
                )
            );
            modules.Add(new UnityEncounterProjectionModule(owner));
            UnityEncounterComposition composition = new(modules);
            composition.ConfigureActionPresentation(actionPresentation);
            return new UnityEncounterModuleSet(composition, actionCatalog, registryBuilder.Build());
        }
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
        private readonly IReadOnlyDictionary<SpellId, ISpellCastRule> spellRules;
        private readonly IReadOnlyList<IActionCatalog> featureCatalogs;

        internal CombatActionCatalog(
            StrideActionDefinition stride,
            IStrikeActionCatalog strike,
            ISpellDefinitionCatalog spell,
            ISpellBookProvider spellBooks,
            IReadOnlyDictionary<SpellId, ISpellCastRule> spellRules,
            IEnumerable<IActionCatalog> featureCatalogs
        )
        {
            this.stride = stride ?? throw new ArgumentNullException(nameof(stride));
            this.strike = strike ?? throw new ArgumentNullException(nameof(strike));
            this.spell = spell ?? throw new ArgumentNullException(nameof(spell));
            this.spellBooks = spellBooks ?? throw new ArgumentNullException(nameof(spellBooks));
            this.spellRules = spellRules ?? throw new ArgumentNullException(nameof(spellRules));
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

        /// <inheritdoc/>
        public bool TryGetCastRule(SpellId spellId, out ISpellCastRule rule) =>
            spellRules.TryGetValue(spellId, out rule);
    }
}
