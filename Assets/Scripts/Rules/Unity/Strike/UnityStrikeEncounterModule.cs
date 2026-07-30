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
            if (!installUnityAuthority)
                return;

            UnityStrikePresentationObserver presentation = new(controllers, creatures, context);
            lifetime.Add(
                dispatcher.RegisterResolvedOpObserver<ResolveStrikeOp, StrikeResolution>(
                    presentation
                )
            );
            lifetime.Add(
                dispatcher.RegisterResolvedOpObserver<StrikeActionOp, StrikeResolution>(
                    presentation
                )
            );
        }

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            IUnityCombatantStateContribution state = context.PrepareCombatant(
                builder.CreatureId,
                builder.Creature
            );
            builder.Own((IDisposable)state);
            builder.AddState(state);
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
