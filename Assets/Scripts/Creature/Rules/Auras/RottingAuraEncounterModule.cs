using System;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using GridPrivate;

namespace Game.Creature.Rules
{
    /// <summary>Composes Rotting Aura's pure rules and narrow Unity adapters.</summary>
    internal sealed class RottingAuraEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterRuntimeModule,
            IUnityEncounterTopologyModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly UnityRottingAuraContext context;

        internal RottingAuraEncounterModule(UnityRottingAuraContext context) =>
            this.context = context ?? throw new ArgumentNullException(nameof(context));

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseRottingAuraRules();

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime) =>
            lifetime.Add(dispatcher.RegisterFactObserver<RottingAuraResolvedFact>(context));

        /// <inheritdoc/>
        public void RefreshTopology(Tile[,] tiles) => context.ReplaceTiles(tiles);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder) =>
            builder.AddRuleBindings(new[] { RottingAuraRules.CreateBinding(builder.CreatureId) });
    }
}
