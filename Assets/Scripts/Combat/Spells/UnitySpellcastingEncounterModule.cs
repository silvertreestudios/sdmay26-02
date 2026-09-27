using System;
using System.Collections.Generic;
using System.Linq;
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
            IUnityEncounterRuntimeModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly ISpellActionCatalog catalog;
        private readonly UnitySpellAttackContext attackContext;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly bool installUnityAuthority;

        internal UnitySpellcastingEncounterModule(
            UnityCombatRulesBridge owner,
            ISpellActionCatalog catalog,
            UnitySpellAttackContext attackContext,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            bool installUnityAuthority
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
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
            builder.AddInitiativeModifiers(
                SpellFeatureRules.CollectInitiativeModifiers(
                    builder.CreatureId,
                    builder.RuleBindings,
                    builder.ActiveEffects
                )
            );
            ISpellBook book = catalog.GetSpellBook(builder.CreatureId);
            SpellSlotResourceSeed restoredSeed =
                builder.Controller.GetComponent<SpellSlotResourceSeed>();
            IReadOnlyList<SpellSlotState> states =
                restoredSeed != null && book is PreparedSpellBook restoredBook
                    ? restoredBook.RestoreSlotStates(builder.CreatureId, restoredSeed.Resources)
                    : book.CreateInitialSlotStates(builder.CreatureId);
            if (
                restoredSeed != null
                && restoredSeed.Resources.Count > 0
                && book is not PreparedSpellBook
            )
                throw new InvalidOperationException(
                    "Saved spell-slot resources require a prepared spellbook."
                );
            builder.AddSpellSlots(states);
            if (!installUnityAuthority)
                return;
            if (book is PreparedSpellBook preparedBook && states.Count > 0)
                builder.AddInstallation(
                    new ConfigureSpellSlotSeedInstallation(
                        builder.Controller,
                        builder.CreatureId,
                        preparedBook,
                        restoredSeed
                    )
                );
            builder.AddInstallation(
                UnitySpellActionInstaller.Prepare(builder.Controller, builder.CreatureId, catalog)
            );
        }

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (lifetime == null)
                throw new ArgumentNullException(nameof(lifetime));
            if (installUnityAuthority)
                lifetime.Add(new RegistrationToken(ProjectSpellSlots));
        }

        private void ProjectSpellSlots()
        {
            RulesSnapshot snapshot = owner.Snapshot;
            foreach (KeyValuePair<CreatureId, CreatureComponent> actor in creatures)
            {
                if (actor.Value == null)
                    continue;
                SpellSlotResourceSeed seed = actor.Value.GetComponent<SpellSlotResourceSeed>();
                seed?.Project(snapshot);
            }
        }

        private sealed class ConfigureSpellSlotSeedInstallation
            : IUnityCombatantInstallationContribution
        {
            private readonly ActionController controller;
            private readonly CreatureId owner;
            private readonly PreparedSpellBook book;
            private readonly SpellSlotResourceSeed existingSeed;

            internal ConfigureSpellSlotSeedInstallation(
                ActionController controller,
                CreatureId owner,
                PreparedSpellBook book,
                SpellSlotResourceSeed existingSeed
            )
            {
                this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
                this.owner = owner;
                this.book = book ?? throw new ArgumentNullException(nameof(book));
                this.existingSeed = existingSeed;
            }

            public void Apply()
            {
                SpellSlotResourceSeed seed =
                    existingSeed ?? controller.gameObject.AddComponent<SpellSlotResourceSeed>();
                seed.Configure(owner, book);
            }
        }
    }
}
