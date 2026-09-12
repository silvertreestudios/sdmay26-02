using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Composition
{
    /// <summary>Owns health and defeat projection from committed rules Facts into Unity.</summary>
    internal sealed class UnityHealthProjectionModule : IUnityEncounterRuntimeModule
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly UnityActionPresentationCoordinator actionPresentation;
        private readonly bool enabled;

        internal UnityHealthProjectionModule(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityActionPresentationCoordinator actionPresentation,
            bool enabled
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.actionPresentation =
                actionPresentation ?? throw new ArgumentNullException(nameof(actionPresentation));
            this.enabled = enabled;
        }

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            if (!enabled)
                return;
            HealthProjectionObserver observer = new(creatures, actionPresentation);
            lifetime.Add(dispatcher.RegisterFactObserver<HealthFact>(observer));
            lifetime.Add(dispatcher.RegisterFactObserver<CreatureDefeatCommittedFact>(observer));
        }

        private sealed class HealthProjectionObserver
            : IFactObserver<HealthFact>,
                IFactObserver<CreatureDefeatCommittedFact>
        {
            private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
            private readonly UnityActionPresentationCoordinator actionPresentation;

            internal HealthProjectionObserver(
                IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
                UnityActionPresentationCoordinator actionPresentation
            )
            {
                this.creatures = creatures;
                this.actionPresentation = actionPresentation;
            }

            /// <inheritdoc/>
            public void OnFactCommitted(HealthFact fact, OpId rootId, RulesSnapshot currentSnapshot)
            {
                CreatureComponent creature = RequireCreature(fact.Creature);
                HealthState health = currentSnapshot.Health[fact.Creature];
                creature.ProjectCommittedHealth(health);
                bool presentHit = fact is DamageAppliedFact && health.Current > 0;
                if (!presentHit)
                    return;
                if (!actionPresentation.TryEnqueue(rootId, () => PresentHit(creature)))
                    creature.PresentCommittedHit();
            }

            /// <inheritdoc/>
            public void OnFactCommitted(
                CreatureDefeatCommittedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            )
            {
                CreatureComponent creature = RequireCreature(fact.Creature);
                if (
                    !actionPresentation.TryEnqueueAfterAction(rootId, () => PresentDefeat(creature))
                )
                    creature.PresentCommittedDefeat();
            }

            private static IEnumerator PresentHit(CreatureComponent creature)
            {
                if (creature != null)
                    creature.PresentCommittedHit();
                yield break;
            }

            private static IEnumerator PresentDefeat(CreatureComponent creature)
            {
                if (creature == null)
                    yield break;
                CreatureAnimationController animation = creature
                    .GetComponent<CreaturePresentation>()
                    ?.AnimationController;
                creature.PresentCommittedDefeat();
                while (
                    creature != null
                    && creature.gameObject != null
                    && creature.gameObject.activeInHierarchy
                    && animation != null
                    && animation.isActiveAndEnabled
                    && animation.IsDeathPlaying
                )
                    yield return null;
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
