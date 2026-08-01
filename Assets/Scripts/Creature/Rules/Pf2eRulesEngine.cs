using System.Collections.Generic;
using Game.Creature;

namespace Game.Creature.Rules
{
    /// <summary>Imports remaining Unity-native passive abilities before encounter composition.</summary>
    public static class Pf2eRulesEngine
    {
        /// <summary>Applies imported passive MonoBehaviour abilities before authoritative enrollment.</summary>
        public static void ApplyCombatStartRules(IEnumerable<ActionController> combatants)
        {
            if (combatants == null)
                return;
            foreach (ActionController controller in combatants)
            {
                CreatureComponent creature = controller?.GetComponent<CreatureComponent>();
                if (controller == null || creature?.passives == null)
                    continue;
                foreach (string passive in creature.passives)
                {
                    if (string.IsNullOrWhiteSpace(passive))
                        continue;
                    DefinedAbilities.TryGet(passive)?.Apply(controller.gameObject);
                }
            }
        }
    }
}
