using System;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Composition
{
    /// <summary>Queues encounter presentation from committed Facts.</summary>
    internal sealed class UnityEncounterProjectionModule : IUnityEncounterRuntimeModule
    {
        private readonly UnityCombatRulesBridge owner;

        internal UnityEncounterProjectionModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            EncounterProjectionObserver projection = new(owner);
            lifetime.Add(dispatcher.RegisterFactObserver<EncounterStartedFact>(projection));
            lifetime.Add(dispatcher.RegisterFactObserver<TurnBeganFact>(projection));
            lifetime.Add(dispatcher.RegisterFactObserver<TurnEndedFact>(projection));
            lifetime.Add(
                dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(projection)
            );
        }

        private sealed class EncounterProjectionObserver
            : IFactObserver<EncounterStartedFact>,
                IFactObserver<TurnBeganFact>,
                IFactObserver<TurnEndedFact>,
                IFactObserver<EncounterOutcomeCommittedFact>
        {
            private readonly UnityCombatRulesBridge owner;

            internal EncounterProjectionObserver(UnityCombatRulesBridge owner) =>
                this.owner = owner;

            /// <inheritdoc/>
            public void OnFactCommitted(
                EncounterStartedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            )
            {
                owner.EnqueueEncounterPresentation(owner.ProjectEncounterStarted);
            }

            /// <inheritdoc/>
            public void OnFactCommitted(
                TurnBeganFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            )
            {
                owner.EnqueueEncounterPresentation(() => owner.ProjectTurnBegan(fact.Turn));
            }

            /// <inheritdoc/>
            public void OnFactCommitted(
                TurnEndedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            )
            {
                owner.EnqueueEncounterPresentation(() => owner.ProjectTurnEnded(fact.Turn));
            }

            /// <inheritdoc/>
            public void OnFactCommitted(
                EncounterOutcomeCommittedFact fact,
                OpId rootId,
                RulesSnapshot currentSnapshot
            )
            {
                owner.EnqueueEncounterPresentation(() => owner.ProjectEncounterEnded(fact.Outcome));
            }
        }
    }
}
