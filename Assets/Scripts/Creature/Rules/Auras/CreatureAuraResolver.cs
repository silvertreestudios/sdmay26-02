using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Creature.Rules
{
    public static class CreatureAuraResolver
    {
        // The encounter turn-start adapter is the sole mutation root. Aura calculation and
        // presentation remain feature-owned while health commits through the active dispatcher.
        internal static async ValueTask<List<CreatureAuraEffectResult>> ApplyTurnStartAurasAwaited(
            ActionController acting,
            IEnumerable<ActionController> combatants,
            Tile[,] tiles,
            Func<CreatureComponent, int, RuleSource, ValueTask<DamageOutcome>> applyDamage,
            Func<CreatureComponent, bool> canReceiveAura,
            Func<CreatureAuraEffectResult, ValueTask> presentResult,
            IPf2eDiceRoller diceRoller = null
        )
        {
            if (applyDamage == null)
                throw new ArgumentNullException(nameof(applyDamage));
            if (canReceiveAura == null)
                throw new ArgumentNullException(nameof(canReceiveAura));
            if (presentResult == null)
                throw new ArgumentNullException(nameof(presentResult));
            List<CreatureAuraEffectResult> results = new();
            if (acting == null || combatants == null || tiles == null)
                return results;

            diceRoller ??= new UnityPf2eDiceRoller();
            foreach (CreatureAuraInstance instance in GetActiveAuras(combatants))
            {
                if (instance.Rule.Timing != CreatureAuraTiming.TurnStart)
                    continue;
                AreaTargetResult area = CreatureAuraArea.EvaluateEmanation(
                    instance.SourceObject,
                    instance.Aura,
                    tiles
                );
                if (!CreatureAuraArea.AffectsCreature(area, acting.gameObject))
                    continue;

                CreatureComponent targetCreature = acting.GetComponent<CreatureComponent>();
                if (!canReceiveAura(targetCreature))
                    continue;
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
                if (instance.Rule is not RottingAuraRule rottingAura)
                    throw new InvalidOperationException(
                        $"Aura rule '{instance.Rule.Slug}' has no encounter resolver."
                    );
                CreatureAuraEffectResult result = rottingAura.Resolve(context);
                await applyDamage(
                    targetCreature,
                    Math.Max(0, result.AppliedDamage),
                    RuleSource.FromSlug(RottingAuraRule.RuleSlug)
                );
                await presentResult(result);
                results.Add(result);
            }
            return results;
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
