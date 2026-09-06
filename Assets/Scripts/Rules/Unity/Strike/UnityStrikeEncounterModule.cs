using System;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using Game.Strikes;
using GridPrivate;

namespace Game.Rules.Unity.Strike
{
    /// <summary>Owns Strike dispatcher, projection, state, and action installation composition.</summary>
    internal sealed class UnityStrikeEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterRuntimeModule,
            IUnityEncounterActionPresentationModule,
            IUnityEncounterTopologyModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly UnityStrikeContext context;
        private readonly IReadOnlyDictionary<CreatureId, ActionController> controllers;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly bool installUnityAuthority;

        internal UnityStrikeEncounterModule(
            UnityStrikeContext context,
            IReadOnlyDictionary<CreatureId, ActionController> controllers,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            bool installUnityAuthority
        )
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.installUnityAuthority = installUnityAuthority;
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseStrikeRules(context, context, context);

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            lifetime.Add(dispatcher.RegisterFactObserver<AmmunitionSpentFact>(context));
            lifetime.Add(dispatcher.RegisterFactObserver<StrikeItemLoadedChangedFact>(context));
        }

        /// <inheritdoc/>
        public void ConfigureActionPresentation(UnityActionPresentationRegistry registry)
        {
            if (!installUnityAuthority)
                return;
            registry.Register<StrikeActionOp, StrikeResolution>(
                StrikeActionDefinition.DefinitionId,
                new UnityStrikeActionPresenter(controllers, creatures, context)
            );
        }

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            IDisposable preparation = context.PrepareCombatant(
                builder.CreatureId,
                builder.Creature,
                out IReadOnlyList<EquipmentState> equipment,
                out IReadOnlyList<AmmunitionState> ammunition
            );
            builder.Own(preparation);
            builder.AddEquipment(equipment);
            builder.AddAmmunition(ammunition);
            if (installUnityAuthority)
            {
                builder.AddInstallation(
                    UnityStrikeActionInstaller.Prepare(
                        builder.Controller,
                        builder.CreatureId,
                        context
                    )
                );
            }
        }

        /// <inheritdoc/>
        public void RefreshTopology(Tile[,] tiles) => context.ReplaceTiles(tiles);
    }
}
