using System.Collections.Generic;
using System.Threading.Tasks;
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
        public static async ValueTask ApplyAsync(GameObject actor, IEnumerable<RuleEffect> effects)
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
                            await actionController.SpendActionsAsync(effect.ActionCost);
                        break;
                    case RuleEffectType.SetTakingActionFalse:
                        if (actionController != null)
                            actionController.IsTakingAction = false;
                        break;
                    case RuleEffectType.GainSourceTempHp:
                        if (creature != null)
                            await creature.GrantSourceTemporaryHitPointsAsync(
                                RuleSource.FromSlug(effect.Source),
                                effect.Amount
                            );
                        break;
                    case RuleEffectType.RemoveSourceTempHp:
                        if (creature != null)
                            await creature.RemoveSourceTemporaryHitPointsAsync(
                                RuleSource.FromSlug(effect.Source)
                            );
                        break;
                    case RuleEffectType.AddTempHpImmunity:
                        if (creature != null)
                            await creature.AddTemporaryHitPointImmunityAsync(
                                RuleSource.FromSlug(effect.Source)
                            );
                        break;
                }
            }
        }
    }
}
