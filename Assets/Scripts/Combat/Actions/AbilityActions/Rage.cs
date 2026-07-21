using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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
        /// <returns>
        /// <see langword="true"/> when Rage was applied; otherwise <see langword="false"/> when
        /// its prerequisites fail or another action already owns the actor's reservation.
        /// </returns>
        /// <remarks>
        /// Direct callers whose actor has an <see cref="ActionController"/> receive the same action
        /// reservation normally established by <see cref="ActionController.TakeAction"/>.
        /// Eligibility is checked before cost, the authoritative cost settles before prepared Rage
        /// state is published, and the reservation remains owned until every awaited host effect
        /// completes or fails.
        /// </remarks>
        public async ValueTask<bool> UseRageAsync(GameObject actor)
        {
            ActionController actionController = actor?.GetComponent<ActionController>();
            if (actionController != null && actionController.IsTakingAction)
                return false;

            if (actionController != null)
                actionController.IsTakingAction = true;
            try
            {
                return await ApplyRageAsync(actor);
            }
            finally
            {
                if (actionController != null)
                    actionController.IsTakingAction = false;
            }
        }

        private async ValueTask<bool> ApplyRageAsync(GameObject actor)
        {
            Debug.Log(actor + " is attempting to use Rage");
            RageRequest request = new() { Creature = UnityCreatureRulesAdapter.From(actor) };
            if (!RageRule.CanApply(request))
            {
                Debug.Log(actor + " cannot Rage");
                return false;
            }

            await PayCostAsync(actor?.GetComponent<ActionController>());
            RageRuleResult result = RageRule.Apply(request);
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
                new RageRequest { Creature = UnityCreatureRulesAdapter.From(actor) }
            );
        }

        /// <summary>
        /// Ends Rage for an actor and applies cleanup effects returned by the pure Rage rule.
        /// </summary>
        /// <param name="actor">The Unity actor whose Rage should end.</param>
        /// <remarks>
        /// Every emitted cleanup effect is attempted in deterministic order. If committed health
        /// notification fails, later cleanup phases still settle before the original failure (or
        /// ordered aggregate) is reported.
        /// </remarks>
        /// <exception cref="AggregateException">
        /// More than one ordered Rage cleanup effect reports a failure after every effect is
        /// attempted.
        /// </exception>
        public async ValueTask EndRageAsync(GameObject actor)
        {
            RageRuleResult result = RageRule.End(UnityCreatureRulesAdapter.From(actor));
            List<Exception> failures = new();
            foreach (RuleEffect effect in result.Effects)
            {
                try
                {
                    await UnityRuleEffectApplier.ApplyAsync(actor, new[] { effect });
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("Multiple Rage cleanup effects failed.", failures);
        }

        protected override IEnumerator MFInvoke(GameObject actor)
        {
            // ActionController.TakeAction and MultiFrameEntityAction own this invocation's single
            // reservation. Calling the core avoids releasing it before the outer action finally.
            yield return CoroutineRunner.Await(ApplyRageAsync(actor));
        }
    }
}
