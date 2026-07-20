using System.Collections.Generic;
using Game.Creature;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Creature.Rules
{
    public static class CreatureAuraResolver
    {
        public static List<CreatureAuraEffectResult> ApplyTurnStartAuras(
            ActionController acting,
            IEnumerable<ActionController> combatants,
            Tile[,] tiles,
            IPf2eDiceRoller diceRoller = null
        )
        {
            return ApplyAuras(CreatureAuraTiming.TurnStart, acting, combatants, tiles, diceRoller);
        }

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

        private static List<CreatureAuraEffectResult> ApplyAuras(
            CreatureAuraTiming timing,
            ActionController acting,
            IEnumerable<ActionController> combatants,
            Tile[,] tiles,
            IPf2eDiceRoller diceRoller
        )
        {
            List<CreatureAuraEffectResult> results = new();
            if (acting == null || combatants == null || tiles == null)
                return results;

            diceRoller ??= new UnityPf2eDiceRoller();
            foreach (CreatureAuraInstance instance in GetActiveAuras(combatants))
            {
                if (instance.Rule.Timing != timing)
                    continue;

                AreaTargetResult area = CreatureAuraArea.EvaluateEmanation(
                    instance.SourceObject,
                    instance.Aura,
                    tiles
                );
                if (!CreatureAuraArea.AffectsCreature(area, acting.gameObject))
                    continue;

                CreatureComponent targetCreature = acting.GetComponent<CreatureComponent>();
                CreatureAuraContext context = new(
                    instance.SourceController,
                    acting,
                    instance.SourceCreature,
                    targetCreature,
                    instance.Aura,
                    tiles,
                    area,
                    diceRoller
                );
                if (!instance.Rule.CanAffect(context))
                    continue;

                foreach (CreatureAuraEffectResult result in instance.Rule.Apply(context))
                    results.Add(result);
            }

            return results;
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
