using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Spells;
using GridPrivate;

namespace Game.Combat.Spells
{
    /// <summary>Owns generic spellcasting rules, presentation, and action installation composition.</summary>
    internal sealed class UnitySpellcastingEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterActionPresentationModule,
            IUnityEncounterTopologyModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly ISpellActionCatalog catalog;
        private readonly UnitySpellAttackContext attackContext;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly bool installUnityAuthority;

        internal UnitySpellcastingEncounterModule(
            ISpellActionCatalog catalog,
            UnitySpellAttackContext attackContext,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            bool installUnityAuthority
        )
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.attackContext =
                attackContext ?? throw new ArgumentNullException(nameof(attackContext));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.installUnityAuthority = installUnityAuthority;
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder)
        {
            builder.UseSpellcastingRules(catalog, attackContext);
            SpellFeatureRules.ConfigureDispatcher(builder);
        }

        /// <inheritdoc/>
        public void ConfigureActionPresentation(UnityActionPresentationRegistry registry)
        {
            if (!installUnityAuthority)
                return;
            registry.Register<CastSpellActionOp, CastSpellOutcome>(
                CastSpellActionDefinition.DefinitionId,
                new UnitySpellActionPresenter(creatures, catalog)
            );
        }

        /// <inheritdoc/>
        public void RefreshTopology(Tile[,] tiles) => attackContext.ReplaceTiles(tiles);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            builder.AddSpellSlots(
                catalog.GetSpellBook(builder.CreatureId).CreateInitialSlotStates(builder.CreatureId)
            );
            if (!installUnityAuthority)
                return;
            builder.AddInstallation(
                UnitySpellActionInstaller.Prepare(builder.Controller, builder.CreatureId, catalog)
            );
        }
    }
}
