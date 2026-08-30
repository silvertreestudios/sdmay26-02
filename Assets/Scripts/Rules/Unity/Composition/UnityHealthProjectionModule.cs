using System;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Composition
{
    /// <summary>Owns health and defeat projection from committed rules Facts into Unity.</summary>
    internal sealed class UnityHealthProjectionModule : IUnityEncounterRuntimeModule
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly bool enabled;

        internal UnityHealthProjectionModule(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            bool enabled
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.enabled = enabled;
        }

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            if (!enabled)
                return;
            HealthProjectionObserver observer = new(creatures);
            lifetime.Add(dispatcher.RegisterFactObserver<HealthFact>(observer));
            lifetime.Add(dispatcher.RegisterFactObserver<CreatureDefeatCommittedFact>(observer));
        }

        private sealed class HealthProjectionObserver
            : IFactObserver<HealthFact>,
                IFactObserver<CreatureDefeatCommittedFact>
        {
            private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

            internal HealthProjectionObserver(
                IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
            ) => this.creatures = creatures;

            /// <inheritdoc/>
            public void OnFactCommitted(HealthFact fact, OpId rootId, RulesSnapshot currentSnapshot)
            {
                CreatureComponent creature = RequireCreature(fact.Creature);
                HealthState health = currentSnapshot.Health[fact.Creature];
                creature.ProjectCommittedHealth(health);
                if (fact is DamageAppliedFact && health.Current > 0)
                    creature.PresentCommittedHit();
            }

            /// <inheritdoc/>
            public void OnFactCommitted(
                CreatureDefeatCommittedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            )
            {
                RequireCreature(fact.Creature).PresentCommittedDefeat();
            }

            private CreatureComponent RequireCreature(CreatureId id)
            {
                if (!creatures.TryGetValue(id, out CreatureComponent creature) || creature == null)
                    throw new InvalidOperationException(
                        $"Encounter creature {id.Value} has no required Unity mapping."
                    );
                return creature;
            }
        }
    }
}
