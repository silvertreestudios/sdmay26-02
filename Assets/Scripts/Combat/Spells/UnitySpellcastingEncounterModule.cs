using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Spells;
using Game.Rules.Unity.Vfx;
using GridPrivate;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>Selects lasting spell visuals from authoritative active-effect state.</summary>
    public static class SpellPersistentVfxSelector
    {
        /// <summary>Returns the production cue for a supported lasting spell effect.</summary>
        public static PersistentVfxSelection? Select(ActiveEffectInstance effect)
        {
            if (effect.State is not SpellEffectState state)
                return null;
            if (
                effect.DefinitionId != SpellFeatureRules.ShieldEffect
                && effect.DefinitionId != SpellFeatureRules.GuidanceEffect
                && effect.DefinitionId != SpellFeatureRules.BlessEffect
                && effect.DefinitionId != SpellFeatureRules.InfuseVitalityEffect
            )
                return null;
            if (!SpellVfxCueSelector.TryGetPersistent(state.Spell.Spell, out VfxCueId cue))
                return null;
            if (effect.DefinitionId == SpellFeatureRules.GuidanceEffect)
                return new PersistentVfxSelection(
                    state.Target,
                    cue,
                    new VfxCueId("spell/guidance/consume")
                );
            return effect.DefinitionId == SpellFeatureRules.InfuseVitalityEffect
                ? new PersistentVfxSelection(state.Target, cue, ResolveHandOrWeaponAnchor)
                : new PersistentVfxSelection(state.Target, cue);
        }

        private static PersistentVfxAnchor ResolveHandOrWeaponAnchor(CreatureComponent owner)
        {
            Animator animator = owner.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null)
                    return new PersistentVfxAnchor(hand, Vector3.zero, true);
            }
            Transform namedHand = owner
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate =>
                    candidate.name.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0
                    && candidate.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0
                );
            return namedHand != null
                ? new PersistentVfxAnchor(namedHand, Vector3.zero, true)
                : new PersistentVfxAnchor(owner.transform, new Vector3(0.35f, 0.8f, 0.15f), true);
        }
    }

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
        private readonly UnityVfxPlayback vfx;
        private readonly UnityActionPresentationCoordinator actionPresentation;

        internal UnitySpellcastingEncounterModule(
            UnityCombatRulesBridge owner,
            ISpellActionCatalog catalog,
            UnitySpellAttackContext attackContext,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            bool installUnityAuthority,
            UnityVfxPlayback vfx,
            UnityActionPresentationCoordinator actionPresentation
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.attackContext =
                attackContext ?? throw new ArgumentNullException(nameof(attackContext));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.installUnityAuthority = installUnityAuthority;
            this.vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
            this.actionPresentation =
                actionPresentation ?? throw new ArgumentNullException(nameof(actionPresentation));
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
                new UnitySpellActionPresenter(creatures, catalog, vfx)
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
            {
                lifetime.Add(new RegistrationToken(ProjectSpellSlots));
                UnityPersistentVfxObserver persistent = new(
                    vfx,
                    creatures,
                    SpellPersistentVfxSelector.Select,
                    actionPresentation
                );
                lifetime.Add(persistent);
                lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(persistent));
                lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(persistent));
                lifetime.Add(
                    dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(persistent)
                );
            }
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
