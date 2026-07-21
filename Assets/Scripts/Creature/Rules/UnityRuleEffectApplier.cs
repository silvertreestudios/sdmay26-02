using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Applies generic rule effects back to Unity components without knowing which PF2e rule produced them.
    /// </summary>
    public static class UnityRuleEffectApplier
    {
        /// <summary>
        /// Applies an ordered list of generic effects to an actor GameObject.
        /// </summary>
        /// <param name="actor">The Unity actor receiving the side effects.</param>
        /// <param name="effects">The generic effects emitted by a Unity-free rule.</param>
        public static void Apply(GameObject actor, IEnumerable<RuleEffect> effects)
        {
            if (actor == null || effects == null)
                return;

            CreatureComponent creature = actor.GetComponent<CreatureComponent>();
            ActionController actionController = actor.GetComponent<ActionController>();

            foreach (RuleEffect effect in effects)
            {
                if (effect == null)
                    continue;

                switch (effect.Type)
                {
                    case RuleEffectType.SpendActions:
                        if (actionController != null)
                            actionController.SpendActions(effect.ActionCost);
                        break;
                    case RuleEffectType.SetTakingActionFalse:
                        if (actionController != null)
                            actionController.IsTakingAction = false;
                        break;
                    case RuleEffectType.GainSourceTempHp:
                        creature?.GrantSourceTemporaryHitPoints(
                            RuleSource.FromSlug(effect.Source),
                            effect.Amount
                        );
                        break;
                    case RuleEffectType.RemoveSourceTempHp:
                        creature?.RemoveSourceTemporaryHitPoints(
                            RuleSource.FromSlug(effect.Source)
                        );
                        break;
                    case RuleEffectType.AddTempHpImmunity:
                        creature?.AddTemporaryHitPointImmunity(RuleSource.FromSlug(effect.Source));
                        break;
                }
            }
        }
    }
}
