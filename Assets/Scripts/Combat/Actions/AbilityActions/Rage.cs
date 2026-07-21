using System.Collections;
using System.Threading.Tasks;
using Game.Creature.Rules;
using UnityEngine;

namespace Game.AbilityActions
{
    /// <summary>
    /// Unity action wrapper for Rage; rules live in RageRule and this class only bridges action flow to Unity components.
    /// </summary>
    [System.Serializable]
    public class Rage : MultiFrameEntityAction
    {
        public override string ActionName => "Rage";

        /// <summary>
        /// Creates a Rage action with the action point cost supplied by the action system.
        /// </summary>
        /// <param name="cost">The number of action points Rage spends when successfully used.</param>
        public Rage(uint cost)
            : base(cost) { }

        /// <summary>
        /// Attempts to start Rage for an actor by evaluating the pure rule and applying its generic Unity side effects.
        /// </summary>
        /// <param name="actor">The Unity actor attempting to Rage.</param>
        /// <returns>True when Rage was applied.</returns>
        public async ValueTask<bool> UseRageAsync(GameObject actor)
        {
            Debug.Log(actor + " is attempting to use Rage");
            RageRuleResult result = RageRule.Apply(
                new RageRequest
                {
                    Creature = UnityCreatureRulesAdapter.From(actor),
                    ActionCost = ActionCost,
                }
            );
            await UnityRuleEffectApplier.ApplyAsync(actor, result.Effects);
            if (!result.Applied)
                Debug.Log(actor + " cannot Rage");
            return result.Applied;
        }

        /// <summary>
        /// Checks whether the actor can currently Rage without mutating Unity or prepared rule state.
        /// </summary>
        /// <param name="actor">The Unity actor being checked.</param>
        /// <returns>True when the actor satisfies the Rage rule prerequisites.</returns>
        public bool RageAllowed(GameObject actor)
        {
            return RageRule.CanApply(
                new RageRequest
                {
                    Creature = UnityCreatureRulesAdapter.From(actor),
                    ActionCost = ActionCost,
                }
            );
        }

        /// <summary>
        /// Ends Rage for an actor and applies cleanup effects returned by the pure Rage rule.
        /// </summary>
        /// <param name="actor">The Unity actor whose Rage should end.</param>
        public async ValueTask EndRageAsync(GameObject actor)
        {
            RageRuleResult result = RageRule.End(UnityCreatureRulesAdapter.From(actor));
            await UnityRuleEffectApplier.ApplyAsync(actor, result.Effects);
        }

        protected override IEnumerator MFInvoke(GameObject actor)
        {
            yield return CoroutineRunner.Await(UseRageAsync(actor));
        }
    }
}
