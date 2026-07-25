using System;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Lets unmigrated attack actions participate in the single rules-owned MAP state without
    /// exposing a mutable Unity mirror or a feature-specific bridge workflow.
    /// </summary>
    public static class UnityAttackStateAdapter
    {
        /// <summary>Gets the number of prior attacks for an attached combat controller.</summary>
        public static int GetAttackCount(ActionController controller)
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return 0;
            return bridge.Snapshot.MultipleAttackPenalty.TryGet(
                actor,
                out MultipleAttackPenaltyState state
            )
                ? state.AttackCount
                : 0;
        }

        /// <summary>Advances MAP after one legally resolved legacy attack action.</summary>
        public static void Advance(ActionController controller)
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return;
            OpResult<MultipleAttackPenaltyState> result = bridge.Dispatch(
                new AdvanceMultipleAttackPenaltyOp(actor)
            );
            if (result is InvalidOpResult<MultipleAttackPenaltyState> invalid)
                throw new InvalidOperationException(invalid.Reason);
            if (result is not ResolvedOpResult<MultipleAttackPenaltyState>)
                throw new InvalidOperationException("MAP advancement did not resolve.");
        }
    }
}
