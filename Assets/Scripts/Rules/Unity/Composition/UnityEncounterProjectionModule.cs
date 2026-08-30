using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Composition
{
    /// <summary>Owns encounter Fact projection and causal-tree presentation settlement.</summary>
    internal sealed class UnityEncounterProjectionModule : IUnityEncounterRuntimeModule
    {
        private readonly UnityCombatRulesBridge owner;

        internal UnityEncounterProjectionModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            EncounterProjectionObserver projection = new(owner);
            EncounterSettlementObserver settlement = new(owner);
            lifetime.Add(dispatcher.RegisterRootSettlementObserver(settlement));
            lifetime.Add(dispatcher.RegisterCausalTreeSettlementObserver(settlement));
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
            public void OnFactCommitted(EncounterStartedFact fact, RulesSnapshot currentSnapshot)
            {
                owner.ProjectEncounterStarted();
            }

            /// <inheritdoc/>
            public void OnFactCommitted(TurnBeganFact fact, RulesSnapshot currentSnapshot)
            {
                owner.EnqueueEncounterPresentation(fact, () => owner.ProjectTurnBegan(fact.Turn));
            }

            /// <inheritdoc/>
            public void OnFactCommitted(TurnEndedFact fact, RulesSnapshot currentSnapshot)
            {
                owner.EnqueueEncounterPresentation(fact, () => owner.ProjectTurnEnded(fact.Turn));
            }

            /// <inheritdoc/>
            public void OnFactCommitted(
                EncounterOutcomeCommittedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                owner.EnqueueEncounterPresentation(
                    fact,
                    () => owner.ProjectEncounterEnded(fact.Outcome)
                );
            }
        }

        private sealed class EncounterSettlementObserver
            : IRootSettlementObserver,
                ICausalTreeSettlementObserver
        {
            private readonly UnityCombatRulesBridge owner;

            internal EncounterSettlementObserver(UnityCombatRulesBridge owner) =>
                this.owner = owner;

            /// <inheritdoc/>
            public ValueTask OnRootSettled(
                OpId rootId,
                OpId? causalParentRootId,
                RulesSnapshot snapshot
            )
            {
                owner.RecordSettledEncounterRoot(rootId, causalParentRootId);
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnCausalTreeSettled(OpId rootId, RulesSnapshot snapshot)
            {
                owner.DrainEncounterPresentationTree(rootId);
                return default;
            }
        }
    }
}
