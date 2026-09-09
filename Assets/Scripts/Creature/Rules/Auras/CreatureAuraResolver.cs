using System.Collections.Generic;
using Game.Creature;
using GridPrivate;
using UnityEngine;

namespace Game.Creature.Rules
{
    public static class CreatureAuraResolver
    {
        public static List<Vector3Int> GetAuraCells(
            IEnumerable<ActionController> combatants,
            Tile[,] tiles
        )
        {
            return CreatureAuraArea.GetCells(GetVisualAuras(combatants), tiles);
        }

        public static List<CreatureAuraInstance> GetVisualAuras(
            IEnumerable<ActionController> combatants
        )
        {
            List<CreatureAuraInstance> visualAuras = new();
            foreach (CreatureAuraInstance instance in GetActiveAuras(combatants))
            {
                if (instance.Rule.HasVisual(instance.Aura))
                    visualAuras.Add(instance);
            }
            return visualAuras;
        }

        private static IEnumerable<CreatureAuraInstance> GetActiveAuras(
            IEnumerable<ActionController> combatants
        )
        {
            if (combatants == null)
                yield break;

            foreach (ActionController controller in combatants)
            {
                GameObject sourceObject = controller == null ? null : controller.gameObject;
                CreatureComponent source =
                    sourceObject == null ? null : sourceObject.GetComponent<CreatureComponent>();
                if (source == null || source.auras == null || !sourceObject.activeInHierarchy)
                    continue;

                foreach (CreatureAura aura in source.auras)
                {
                    ICreatureAuraRule rule = DefinedAuras.TryGet(aura?.slug);
                    if (rule == null || aura.radiusFeet <= 0)
                        continue;

                    yield return new CreatureAuraInstance(controller, source, aura, rule);
                }
            }
        }
    }
}
